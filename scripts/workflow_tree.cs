#!/usr/bin/env dotnet-script
// workflow_tree.cs — load UiPath XAML through the CoreWF activity object model
// Usage:
//   dotnet run --file scripts/workflow_tree.cs
//   dotnet run --file scripts/workflow_tree.cs -- path/to/project.json
//   dotnet run --file scripts/workflow_tree.cs -- --json
//   dotnet run --file scripts/workflow_tree.cs -- --json-out path/to/output.json

#:package UiPath.Workflow@6.0.3
#:package UiPath.System.Activities@24.10.4
#:package UiPath.Excel.Activities@2.24.3
#:package UiPath.Mail.Activities@2.2.10
#:package UiPath.MicrosoftOffice365.Activities@2.7.24
#:package UiPath.Testing.Activities@24.10.4
#:package UiPath.UIAutomation.Activities@25.10.12
#:property TargetFramework=net6.0-windows7.0
#:property PublishAot=false

using System.Activities;
using System.Activities.Expressions;
using System.Activities.XamlIntegration;
using System.Activities.Statements;
using System.Reflection;
using System.Text.Json;
using System.Xaml;
using System.Xml.Linq;

// ── Resolve project.json (copied from read_xaml.cs) ──────────────────────────
static string? FindProjectJson()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "project", "project.json");
        if (File.Exists(candidate)) return candidate;
        candidate = Path.Combine(dir.FullName, "project.json");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return null;
}

// ── Arg parsing ──────────────────────────────────────────────────────────────
// positional:           optional project.json path
// --text-out <path>     IR tree to file  (summary still to stdout)
// --json                JSON to stdout — suppresses all human text
// --json-out <path>     JSON to file     (summary still to stdout)
string? projectJsonArg = null;
bool    emitJson       = false;
string? jsonOutArg     = null;
string? textOutArg     = null;

for (int i = 0; i < args.Length; i++)
{
    if      (args[i] == "--text-out" && i + 1 < args.Length) { textOutArg = args[++i]; }
    else if (args[i] == "--json")                             { emitJson = true; }
    else if (args[i] == "--json-out" && i + 1 < args.Length) { emitJson = true; jsonOutArg = args[++i]; }
    else if (!args[i].StartsWith("--"))                       { projectJsonArg = args[i]; }
}

var projectJsonPath = projectJsonArg ?? FindProjectJson();
if (projectJsonPath is null || !File.Exists(projectJsonPath))
{
    Console.Error.WriteLine("project.json not found");
    return 1;
}

projectJsonPath = Path.GetFullPath(projectJsonPath);
var projectRoot  = Path.GetDirectoryName(projectJsonPath)!;

using var jsonDoc = JsonDocument.Parse(File.ReadAllText(projectJsonPath));
var projectJson = jsonDoc.RootElement;

// ── Parse project.json metadata ──────────────────────────────────────────────
var projectName = projectJson.GetProperty("name").GetString() ?? "";
var mainPath    = projectJson.TryGetProperty("main", out var mainEl)
                  ? NormalizeRelPath(mainEl.GetString() ?? "") : "";

List<string> entryPoints;
if (projectJson.TryGetProperty("entryPoints", out var epsEl))
{
    entryPoints = epsEl.EnumerateArray()
        .Select(ep => NormalizeRelPath(ep.GetProperty("filePath").GetString()!))
        .ToList();
}
else
{
    entryPoints = new List<string>();
}
// Fallback: seed from main if entryPoints absent or empty
if (entryPoints.Count == 0 && !string.IsNullOrEmpty(mainPath))
    entryPoints.Add(mainPath);


// ── Expression extractor — argument properties not exposed by GetActivities() ─
// GetActivities() yields argument expression activities already declared as
// RuntimeArguments, but InvokeWorkflowFile passes arguments through a custom
// Collection<WorkflowArgument> that is NOT declared as RuntimeArguments.
// This method reflects over an activity's InArgument/OutArgument properties and
// enumerable collections to capture those extra expression paths.
//
// Guards against infinite loops:
//  • Activity instances are skipped (handled by the tree walk)
//  • System.Type instances are skipped (reflection metadata, no expressions)
//  • Reflection only follows InArgument/OutArgument/IEnumerable-typed properties
static void ExtractExpressionsFromObject(object? obj, List<string> expressions)
{
    if (obj is null) return;

    // Direct expression node — capture text and stop.
    if (obj is ITextExpression te)
    {
        if (!string.IsNullOrWhiteSpace(te.ExpressionText))
            expressions.Add(te.ExpressionText);
        return;
    }

    // Activity instances are walked by the tree; skip them here to avoid cycles.
    if (obj is Activity) return;

    var type = obj.GetType();
    if (type.IsPrimitive || type == typeof(string) || obj is Type) return;

    // Unwrap InArgument<T> / OutArgument<T> via their .Expression property.
    var typeName = type.Name;
    if (typeName.StartsWith("InArgument") || typeName.StartsWith("OutArgument"))
    {
        var exprProp = type.GetProperty("Expression", BindingFlags.Public | BindingFlags.Instance);
        var expr = exprProp?.GetValue(obj);
        if (expr is not null) ExtractExpressionsFromObject(expr, expressions);
        return;
    }

    // Walk IEnumerable collections (WorkflowArgument lists, AssignOperation lists, …)
    if (obj is System.Collections.IEnumerable enumerable)
    {
        foreach (var item in enumerable)
            ExtractExpressionsFromObject(item, expressions);
        return;
    }

    // Shallow reflection — only follow InArgument/OutArgument/ITextExpression/
    // IEnumerable typed properties to avoid traversing unrelated WF graph edges.
    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (!prop.CanRead) continue;
        if (prop.GetIndexParameters().Length > 0) continue;

        var pt = prop.PropertyType;
        if (!pt.Name.StartsWith("InArgument")
            && !pt.Name.StartsWith("OutArgument")
            && !typeof(ITextExpression).IsAssignableFrom(pt)
            && !typeof(System.Collections.IEnumerable).IsAssignableFrom(pt))
            continue;

        object? value;
        try { value = prop.GetValue(obj); }
        catch { continue; }

        if (value is null || value is string) continue;
        ExtractExpressionsFromObject(value, expressions);
    }
}

// ── Annotation reader — raw XAML, keyed by DisplayName ───────────────────────
// Annotations live in the design-time XAML layer as sap2010:Annotation.AnnotationText
// attached properties.  They are not part of the WF runtime object model.
// Key: activity DisplayName (imprecise for duplicate names; a future version
// should use WorkflowViewState.IdRef for exact correlation).
static Dictionary<string, string> ExtractAnnotations(string xamlPath)
{
    XNamespace sap2010 = "http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation";
    var doc = XDocument.Load(xamlPath);
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var el in doc.Descendants())
    {
        var displayName = el.Attribute("DisplayName")?.Value;
        var text        = el.Attribute(sap2010 + "Annotation.AnnotationText")?.Value;
        if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(text))
            map.TryAdd(displayName, text);
    }
    return map;
}

// ── Inline expression helper ──────────────────────────────────────────────────
// Unwrap an InArgument<T> / OutArgument<T> / Argument and return its expression
// text, or null if there is no expression.
static string? GetArgExpr(object? argValue)
{
    if (argValue is null) return null;
    // Already an expression itself
    if (argValue is ITextExpression te2
        && !string.IsNullOrWhiteSpace(te2.ExpressionText))
        return te2.ExpressionText;
    // Literal<T> — a constant value, not a VB expression string.
    // InvokeWorkflowFile.WorkflowFileName is stored as Literal<string>.
    if (argValue.GetType().Name == "Literal`1")
    {
        var valProp = argValue.GetType().GetProperty("Value");
        return valProp?.GetValue(argValue)?.ToString();
    }
    // Try .Expression property (InArgument<T> / OutArgument<T> / Argument).
    // Use GetProperties() to avoid AmbiguousMatchException when a type has
    // multiple overloads of a property (e.g. AssignOperation inherits Activity
    // which re-declares Expression at multiple levels).
    var exprProp = argValue.GetType()
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(p => p.Name == "Expression" && p.CanRead
                             && p.GetIndexParameters().Length == 0);
    try
    {
        var inner = exprProp?.GetValue(argValue);
        if (inner is not null && !ReferenceEquals(inner, argValue))
            return GetArgExpr(inner);   // recurse — handles Literal<T> unwrap
    }
    catch { }
    return null;
}

// ── Model builder ─────────────────────────────────────────────────────────────
// Build traverses the WF activity graph and returns a WfNode tree.
// It never prints; all rendering is deferred to Render().
//
// Format a WARN line for GetActivities() failures, walking the full InnerException chain.
static string FormatGetActivitiesWarn(string relPath, string typeName, string displayName, Exception ex)
{
    var sb = new System.Text.StringBuilder();
    sb.Append($"WARN  GetActivities [{relPath}] [{typeName}] [{displayName}]:");
    var e = ex;
    while (e is not null)
    {
        sb.Append($"\n        {e.GetType().Name}: {e.Message}");
        e = e.InnerException;
    }
    return sb.ToString();
}

// allExpressions is a side accumulator: Build appends every expression text it
// discovers (both node-local named ones and non-structural ones found by
// ExtractExpressionsFromObject).  Duplicates are expected and preserved;
// dedup happens only at the summary distinct-count line.
static WfNode? Build(
    Activity activity,
    IReadOnlyDictionary<string, string> annotations,
    ref int idCounter,
    List<string> allExpressions,
    string relPath = "")
{
    var typeName = activity.GetType().Name;

    // ── Step 1: suppress expression payload nodes ────────────────────────────
    // VisualBasicValue<T>, VisualBasicReference<T>, Literal<T>, etc.
    // Expression payload nodes are suppressed from the node tree; their text is
    // captured either via node-local property extraction (step 7) or via
    // ExtractExpressionsFromObject (step 8).
    if (activity is ITextExpression te)
    {
        if (!string.IsNullOrWhiteSpace(te.ExpressionText))
            allExpressions.Add(te.ExpressionText);
        return null;
    }

    // ── Step 2: suppress runtime scaffolding ─────────────────────────────────
    if (WalkFilter.IsScaffolding(typeName)) return null;

    // ── Step 3: resolve DisplayName ──────────────────────────────────────────
    // DynamicActivity.DisplayName defaults to "DynamicActivity"; use .Name (= x:Class) instead.
    var displayName = activity is DynamicActivity da2
        ? (da2.Name ?? da2.DisplayName ?? "")
        : (activity.DisplayName ?? "");

    // ── Step 4: assign synthetic build-local Id ──────────────────────────────
    var id = (++idCounter).ToString();

    // ── Step 5: build Arguments ──────────────────────────────────────────────
    // Direction inferred from argument wrapper type name where available; best-effort otherwise.
    // "InOut" is a catch-all for any name that starts with neither "InArgument" nor "OutArgument".
    // Sufficient for the current corpus; revisit if InOutArgument or custom wrappers appear.
    var arguments = new List<WfArgument>();
    if (activity is DynamicActivity da)
    {
        foreach (var p in da.Properties)
        {
            var typeStr = p.Type?.Name ?? "?";
            var direction = typeStr.StartsWith("InArgument")  ? "In"
                          : typeStr.StartsWith("OutArgument") ? "Out"
                          : "InOut";
            arguments.Add(new WfArgument(p.Name, direction, typeStr));
        }
    }

    // ── Step 6: build Variables ──────────────────────────────────────────────
    var variables = new List<WfVariable>();
    var varsProp = activity.GetType().GetProperty("Variables",
        BindingFlags.Public | BindingFlags.Instance);
    if (varsProp?.GetValue(activity) is System.Collections.IEnumerable vars)
    {
        foreach (var v in vars)
        {
            var vt    = v.GetType();
            var vName = vt.GetProperty("Name")?.GetValue(v)?.ToString() ?? "?";
            var vType = (vt.GetProperty("Type")?.GetValue(v) as Type)?.Name ?? "?";
            variables.Add(new WfVariable(vName, vType));
        }
    }

    // ── Step 7: build node-local named Expressions ───────────────────────────
    var expressions = new List<WfExpression>();
    var actType = activity.GetType();
    foreach (var propName in new[] { "Condition", "Value", "To", "Message", "WorkflowFileName" })
    {
        var prop = actType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (prop is null) continue;
        object? val;
        try { val = prop.GetValue(activity); } catch { continue; }
        var expr = GetArgExpr(val);
        if (expr is null) continue;
        expressions.Add(new WfExpression(propName, expr));
        allExpressions.Add(expr);   // also feed accumulator; duplicates expected
    }

    // ── Step 8: capture non-structural expressions ───────────────────────────
    // Covers InvokeWorkflowFile.Arguments and other custom argument collections
    // not exposed as inline properties above.  Duplicates with step 7 are
    // expected and preserved; dedup happens only at the summary distinct-count line.
    ExtractExpressionsFromObject(activity, allExpressions);

    // ── Step 9: look up annotation ───────────────────────────────────────────
    annotations.TryGetValue(displayName, out var annotation);

    // ── Step 10: recurse into children ───────────────────────────────────────
    // GetActivities() + retry pattern: first call may throw InvalidWorkflowException
    // (VB compile errors collected as validation errors), but IsMetadataCached=true
    // afterwards; the second call iterates children without re-validating.
    IEnumerable<Activity> rawChildren;
    try
    {
        rawChildren = WorkflowInspectionServices.GetActivities(activity).ToList();
    }
    catch (InvalidWorkflowException)
    {
        try { rawChildren = WorkflowInspectionServices.GetActivities(activity).ToList(); }
        catch (Exception ex2)
        {
            Console.Error.WriteLine(FormatGetActivitiesWarn(relPath, typeName, displayName, ex2));
            rawChildren = Enumerable.Empty<Activity>();
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(FormatGetActivitiesWarn(relPath, typeName, displayName, ex));
        rawChildren = Enumerable.Empty<Activity>();
    }

    var children = new List<WfNode>();
    foreach (var child in rawChildren)
    {
        var childNode = Build(child, annotations, ref idCounter, allExpressions, relPath);
        if (childNode is not null) children.Add(childNode);
    }

    return new WfNode(id, typeName, displayName, annotation, arguments, variables, expressions, children);
}

// ── Stats collector ───────────────────────────────────────────────────────────
// Derives activity type frequency table from WfNode only.
// Expression totals come from the build-time accumulator.
// Annotation count uses annotations.Count (raw XAML source of truth) — see main loop.
static void CollectStats(WfNode node, Dictionary<string, int> freq)
{
    freq[node.Type] = freq.GetValueOrDefault(node.Type) + 1;
    foreach (var child in node.Children)
        CollectStats(child, freq);
}


// ── Path normalization ────────────────────────────────────────────────────────
// Centralised: used for main, entryPoints[].filePath, and WorkflowFileName values.
static string NormalizeRelPath(string path) =>
    path.Replace('/', Path.DirectorySeparatorChar)
        .Replace('\\', Path.DirectorySeparatorChar)
        .TrimStart(Path.DirectorySeparatorChar);

// ── Invoke edge scanner ───────────────────────────────────────────────────────
// Yields (nodeId, rawFileName) for every InvokeWorkflowFile node in the tree.
// Resolution is the caller's responsibility — rawFileName is the as-stored value.
static IEnumerable<(string NodeId, string RawFileName)> FindInvokeEdges(WfNode node)
{
    if (node.Type == "InvokeWorkflowFile")
    {
        var expr = node.Expressions.FirstOrDefault(e => e.Name == "WorkflowFileName");
        if (expr is not null)
            yield return (node.Id, expr.Value);
    }
    foreach (var child in node.Children)
        foreach (var edge in FindInvokeEdges(child))
            yield return edge;
}

// ── Text renderer (IR) ────────────────────────────────────────────────────────
// Walks the IR-normalized WfNode tree and writes an indented text representation.
// When an InvokeWorkflowFile node is encountered the called workflow is inlined
// at depth+1 using the project graph.  `visiting` guards against cycles.
static void Render(WfNode node, int depth, TextWriter output,
                   WfProject project, HashSet<string> visiting)
{
    var indent  = new string(' ', depth * 2);
    var indentP = indent + "  ";

    output.WriteLine($"{indent}[{node.Type}]  {node.DisplayName}");

    if (!string.IsNullOrEmpty(node.Annotation))
        output.WriteLine($"{indentP}// {node.Annotation.Replace("\r\n", " | ").Replace('\n', '|').Trim()}");

    foreach (var arg in node.Arguments)
        output.WriteLine($"{indentP}arg {arg.Direction} {arg.Name} : {arg.Type}");

    foreach (var v in node.Variables)
        output.WriteLine($"{indentP}var {v.Name} : {v.Type}");

    foreach (var expr in node.Expressions)
        output.WriteLine($"{indentP}.{expr.Name} = {expr.Value}");

    // Graph traversal: inline the called workflow instead of showing raw children
    if (node.Type == "InvokeWorkflowFile")
    {
        var fileExpr = node.Expressions.FirstOrDefault(e => e.Name == "WorkflowFileName");
        if (fileExpr is not null)
        {
            var targetPath = NormalizeRelPath(fileExpr.Value);
            if (visiting.Contains(targetPath))
            {
                output.WriteLine($"{indentP}── [cycle: {targetPath}]");
            }
            else if (project.Workflows.TryGetValue(targetPath, out var callee))
            {
                visiting.Add(targetPath);
                output.WriteLine($"{indentP}── {targetPath}");
                Render(NormalizeTree(callee.Root), depth + 1, output, project, visiting);
                visiting.Remove(targetPath);
            }
        }
        return;   // children are expression containers (Literal<T> etc.) — skip
    }

    foreach (var child in node.Children)
        Render(child, depth + 1, output, project, visiting);
}

// ── JSON renderer ─────────────────────────────────────────────────────────────
// Serializes all WfNode trees to a single JSON object keyed by relative XAML path.
// No DTOs — WfNode serializes directly.  Writes to the supplied TextWriter
// (Console.Out for --json, a StreamWriter for --json-out <path>).
static void RenderJson(WfProject project, TextWriter output)
{
    var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
    output.WriteLine(json);
}

// ── Assembly resolver — NuGet transitive deps + Studio-only assemblies ────────
// UiPath activities like ForEachRow load UiPath.Activities.Contracts at
// construction time (called from GetActivities()/CacheMetadata()).  That DLL is
// not on NuGet — it ships with Studio only.
//
// Probe order:
//   1. AppContext.BaseDirectory  — all NuGet transitive deps (NPOI, ClosedXML, …)
//      copied here by the dotnet runfile build
//   2. Latest Studio installation dir — Studio-only DLLs (UiPath.Activities.Contracts, …)
var assemblyProbePaths = new List<string> { AppContext.BaseDirectory };
var studioBase = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    "UiPathPlatform", "Studio");
if (Directory.Exists(studioBase))
{
    var latestStudio = Directory.GetDirectories(studioBase)
                                .OrderByDescending(d => d)
                                .FirstOrDefault();
    if (latestStudio is not null)
    {
        // Prefer net472 folder: those DLLs target .NET Framework 4.x which loads
        // cleanly in .NET 6 via the compat shim.  The Studio root contains .NET 8
        // builds that pull in System.Runtime 8.0.0.0 — incompatible with net6.0.
        var net472 = Path.Combine(latestStudio, "net472");
        if (Directory.Exists(net472)) assemblyProbePaths.Add(net472);
        assemblyProbePaths.Add(latestStudio);
    }
}

AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
{
    var name = new AssemblyName(args.Name).Name;
    if (name is null) return null;
    foreach (var dir in assemblyProbePaths)
    {
        var path = Path.Combine(dir, $"{name}.dll");
        if (File.Exists(path))
            try { return Assembly.LoadFrom(path); } catch { }
    }
    return null;
};

// ── Load UiPath activity assemblies from project.json dependencies ────────────
// Must happen BEFORE constructing UiPathXamlSchemaContext so that
// BuildTypeCache() sees all loaded assemblies.
// Failures are non-fatal — the schema context degrades gracefully per assembly.
if (projectJson.TryGetProperty("dependencies", out var depsEl))
{
    foreach (var dep in depsEl.EnumerateObject())
    {
        try   { Assembly.Load(dep.Name); }
        catch { Console.Error.WriteLine($"WARN  assembly not available: {dep.Name}"); }
    }
}

// UiPath.System.Activities is required for core types.
try { Assembly.Load("UiPath.System.Activities"); }
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not load UiPath.System.Activities: {ex.Message}");
    return 1;
}

var schemaContext = new UiPathXamlSchemaContext();

// ── Output routing ────────────────────────────────────────────────────────────
// --text-out path   → IR tree to file; summary to stdout
// --json (no path)  → JSON to stdout; suppress all human text
// --json-out path   → JSON to file; summary to stdout
// (no flags)        → one-line summary per file to stdout
bool jsonToStdout = emitJson && jsonOutArg is null;

var defaultTextOut = Path.Combine(projectRoot, "workflow_tree_output.txt");
var resolvedTextOut = textOutArg ?? defaultTextOut;
StreamWriter? treeWriter = textOutArg is not null
    ? new StreamWriter(resolvedTextOut, false,
        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
    : null;

// Header: suppress in pure machine mode (--json to stdout)
if (!jsonToStdout)
{
    Console.WriteLine($"project.json  : {projectJsonPath}");
    Console.WriteLine($"name          : {projectName}");
    Console.WriteLine($"entrypoints   : {entryPoints.Count}");
    Console.WriteLine();
}

// ── BFS graph traversal from entrypoints ─────────────────────────────────────
var workflows     = new Dictionary<string, WfWorkflow>(StringComparer.OrdinalIgnoreCase);
var invokeEdges   = new List<InvokeEdge>();
var queuedOrBuilt = new HashSet<string>(entryPoints, StringComparer.OrdinalIgnoreCase);
var queue         = new Queue<string>(entryPoints);

while (queue.Count > 0)
{
    var relPath  = queue.Dequeue();
    if (workflows.ContainsKey(relPath)) continue;

    var fullPath = Path.GetFullPath(Path.Combine(projectRoot, relPath));
    if (!File.Exists(fullPath))
    {
        Console.Error.WriteLine($"MISSING {relPath}");
        continue;
    }

    Activity root;
    try
    {
        using var stream = File.OpenRead(fullPath);
        using var reader = new XamlXmlReader(stream, schemaContext);
        root = ActivityXamlServices.Load(reader,
            new ActivityXamlServicesSettings { CompileExpressions = false });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"LOAD ERROR {relPath}: {ex.Message}");
        continue;
    }

    var annotations    = ExtractAnnotations(fullPath);
    var allExpressions = new List<string>();
    int idCounter      = 0;
    var wfNode         = Build(root, annotations, ref idCounter, allExpressions, relPath);
    if (wfNode is null) continue;

    workflows[relPath] = new WfWorkflow(
        relPath, wfNode,
        annotations.Count,
        allExpressions.Distinct().Count(),
        allExpressions.Count);

    // Resolve invoked workflows relative to project root (UiPath WorkflowFileName is project-relative)
    foreach (var (nodeId, rawFileName) in FindInvokeEdges(wfNode))
    {
        var targetRel  = NormalizeRelPath(rawFileName);
        var targetFull = Path.GetFullPath(Path.Combine(projectRoot, targetRel));
        var resolved   = File.Exists(targetFull);
        invokeEdges.Add(new InvokeEdge(relPath, nodeId, targetRel, resolved));
        if (resolved && queuedOrBuilt.Add(targetRel))
            queue.Enqueue(targetRel);
    }

    if (!jsonToStdout)
    {
        var freq     = new Dictionary<string, int>();
        CollectStats(wfNode, freq);
        var actCount = freq.Values.Sum();
        Console.WriteLine($"  {relPath,-52}  {actCount,4} activities  " +
                          $"{annotations.Count,3} annotations  " +
                          $"{allExpressions.Distinct().Count(),3} expressions");
    }
}

var wfProject = new WfProject(projectName, mainPath, entryPoints, workflows, invokeEdges);

// ── Text tree output (IR) ─────────────────────────────────────────────────────
if (treeWriter is not null)
{
    foreach (var wf in wfProject.Workflows.Values)
    {
        var irNode  = NormalizeTree(wf.Root);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { wf.Path };
        treeWriter.WriteLine();
        treeWriter.WriteLine($"  {wf.Path}");
        treeWriter.WriteLine($"  {new string('─', wf.Path.Length)}");
        Render(irNode, 2, treeWriter, wfProject, visiting);
    }
}
treeWriter?.Dispose();
if (treeWriter is not null)
    Console.WriteLine($"tree written to: {resolvedTextOut}");

// ── JSON emit ─────────────────────────────────────────────────────────────────
if (emitJson)
{
    if (jsonOutArg is not null)
    {
        using var jsonWriter = new StreamWriter(jsonOutArg, false,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        RenderJson(wfProject, jsonWriter);
        Console.WriteLine($"JSON written to: {jsonOutArg}");
    }
    else
    {
        RenderJson(wfProject, Console.Out);
    }
}

return 0;

// ── IR normalization pipeline ─────────────────────────────────────────────────
// NormalizeTree applies rules bottom-up (children first).
// Rules are pure WfNode → WfNode functions; return the node unchanged when the
// precondition is not met.  Add new calls inside NormalizeTree to extend the IR.

static WfNode NormalizeTree(WfNode node)
{
    var normalizedChildren = node.Children
        .Select(NormalizeTree)
        .ToList();
    var n = node with { Children = normalizedChildren };
    n = CollapseMultipleAssign(n);
    // future rules: n = CollapseFlowSwitch(n);
    return n;
}

// Collapses the runtime expansion of MultipleAssign back to designer-level.
// CoreWF expands MultipleAssign into: Sequence → [TryCatch(AssignOperation, Throw)] × N.
// This rule keeps the Sequence (preserving its annotation) but replaces the
// TryCatch wrappers with their AssignOperation payloads directly, so the result is:
//   MultipleAssign → Sequence (annotated) → [AssignOperation] × N
// Guard: if the expected structure is not found, the node is returned unchanged.
static WfNode CollapseMultipleAssign(WfNode node)
{
    if (node.Type != "MultipleAssign") return node;

    var assignOps = node.Children
        .Where(c => c.Type == "Sequence")
        .SelectMany(seq => seq.Children)
        .Where(tc => tc.Type == "TryCatch")
        .Select(tc => tc.Children.FirstOrDefault())
        .Where(ao => ao is not null && ao!.Type == "AssignOperation")
        .Select(ao => ao!)
        .ToList();

    if (assignOps.Count == 0) return node;
    return node with { Children = assignOps };
}

// ── Semantic model ────────────────────────────────────────────────────────────

record WfArgument(string Name, string Direction, string Type);

// DefaultExpression deferred — requires Variable<T>.Default reflection; add later.
record WfVariable(string Name, string Type);

// Named to preserve property ordering (Condition, Value, To, Message).
record WfExpression(string Name, string Value);

record WfNode(
    string Id,                            // synthetic, build-local, sequential per file; not stable across runs
    string Type,
    string DisplayName,
    string? Annotation,
    List<WfArgument> Arguments,
    List<WfVariable> Variables,
    List<WfExpression> Expressions,       // node-local named: Condition / Value / To / Message
    List<WfNode> Children
);

// ── Project-level model ───────────────────────────────────────────────────────

record WfWorkflow(
    string Path,                     // project-relative, OS separator
    WfNode Root,                     // raw WfNode — IR applied on render, not stored
    int AnnotationCount,
    int ExpressionDistinctCount,
    int ExpressionTotalCount
);

record InvokeEdge(
    string FromWorkflowPath,         // project-relative path of caller
    string FromNodeId,               // WfNode.Id of the InvokeWorkflowFile node
    string ToWorkflowPath,           // project-relative canonical path of callee
    bool Resolved                    // true if the target file exists on disk
);

record WfProject(
    string Name,                     // project.json "name"
    string MainPath,                 // project.json "main" (empty string if absent)
    List<string> EntryPoints,        // project.json "entryPoints[].filePath"
    Dictionary<string, WfWorkflow> Workflows,   // key = project-relative path
    List<InvokeEdge> InvokeEdges
);

// ── Scaffolding filter ────────────────────────────────────────────────────────
static class WalkFilter
{
    // These types are WF runtime argument-binding scaffolding.  They are never
    // present in the XAML that the UiPath designer emits; they are generated
    // internally when the WF runtime materialises arguments and variables.
    private static readonly HashSet<string> Scaffolding = new()
    {
        "EnvironmentLocationReference`1",   // argument environment pointer
        "LocationReferenceValue`1",         // variable location wrapper
        "LambdaValue`1",                    // lambda expression wrapper
        "DelegateArgumentValue`1",          // delegate argument accessor
        "Literal`1",                        // constant value expression container
    };

    public static bool IsScaffolding(string typeName) => Scaffolding.Contains(typeName);
}

// ── UiPath-aware XamlSchemaContext ────────────────────────────────────────────
// Passes only System.Activities, UiPath.Workflow, and mscorlib to the base
// constructor (safe to attribute-scan — none reference UiPath.Activities.Contracts).
//
// All UiPath activity assemblies listed in project.json are loaded before this
// context is constructed.  BuildTypeCache() then scans every UiPath-prefixed
// assembly in the AppDomain and builds a Name → Type lookup that covers all
// packages (Excel, UIAutomation, Mail, etc.) without needing to know CLR
// namespace paths in advance.
//
// Generic activities (e.g. ForEach<String>) are handled by stripping the arity
// suffix to key the open generic type, then closing it with the XAML typeArguments.
class UiPathXamlSchemaContext : XamlSchemaContext
{
    private const string UiPathNs = "http://schemas.uipath.com/workflow/activities";

    // Simple name → open-generic or concrete CLR Type, populated at construction.
    private readonly Dictionary<string, Type> _typeCache;

    public UiPathXamlSchemaContext()
        : base(new[]
        {
            typeof(Activity).Assembly,                       // System.Activities
            Assembly.Load("UiPath.Workflow"),                // VisualBasic.Settings etc.
            typeof(Dictionary<,>).Assembly,                  // System.Private.CoreLib — scg: namespace
            typeof(System.Data.DataTable).Assembly,          // System.Data.Common — sd: namespace (Variable<DataTable> etc.)
        })
    {
        _typeCache = BuildTypeCache();
    }

    // Scan every UiPath-prefixed assembly currently in the AppDomain.
    // GetExportedTypes() is wrapped — assemblies with native or missing
    // transitive dependencies degrade gracefully (their types are skipped).
    private static Dictionary<string, Type> BuildTypeCache()
    {
        var cache = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()
                                     .Where(a => a.GetName().Name?.StartsWith("UiPath") == true))
        {
            Type[] types;
            try
            {
                // GetTypes() (not GetExportedTypes()) is used because UiPath assemblies
                // commonly reference Studio-only transitive dependencies that are absent
                // here (e.g. Microsoft.Rest.ClientRuntime, UiPath.Activities.Contracts).
                // GetExportedTypes() propagates the missing-assembly FileNotFoundException
                // directly, discarding all type info.  GetTypes() raises
                // ReflectionTypeLoadException which carries the types that DID load,
                // so we recover partial results rather than skipping the assembly entirely.
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t is not null).ToArray()!;
            }
            catch { continue; }

            foreach (var t in types)
            {
                if (string.IsNullOrEmpty(t?.Name)) continue;
                // Generic types: "ForEach`1" keyed as "ForEach"
                var backtick = t.Name.IndexOf('`');
                var key = (t.IsGenericTypeDefinition && backtick > 0)
                    ? t.Name[..backtick]
                    : t.Name;
                cache.TryAdd(key, t);
            }
        }
        return cache;
    }

    protected override XamlType? GetXamlType(
        string xamlNamespace, string name, params XamlType[] typeArguments)
    {
        var baseType = base.GetXamlType(xamlNamespace, name, typeArguments);
        if (baseType is not null && !baseType.IsUnknown) return baseType;

        if (xamlNamespace == UiPathNs && _typeCache.TryGetValue(name, out var clrType))
        {
            if (clrType.IsGenericTypeDefinition && typeArguments.Length > 0)
            {
                try
                {
                    var typeArgs = typeArguments.Select(t => t.UnderlyingType).ToArray();
                    clrType = clrType.MakeGenericType(typeArgs);
                }
                catch { return baseType; }
            }
            return GetXamlType(clrType);
        }

        return baseType;
    }
}
