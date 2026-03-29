#!/usr/bin/env dotnet-script
// workflow_tree.cs — load UiPath XAML through the CoreWF activity object model
// Usage:
//   dotnet run --file scripts/workflow_tree.cs
//   dotnet run --file scripts/workflow_tree.cs -- path/to/project.json
//   dotnet run --file scripts/workflow_tree.cs -- --json
//   dotnet run --file scripts/workflow_tree.cs -- --json-out path/to/output.json
//   dotnet run --file scripts/workflow_tree.cs -- --expr-json-out path/to/expr.json

#:package UiPath.Workflow@6.0.3
#:package UiPath.System.Activities@24.10.4
#:package UiPath.Excel.Activities@2.24.3
#:package UiPath.Mail.Activities@2.2.10
#:package UiPath.MicrosoftOffice365.Activities@2.7.24
#:package UiPath.Testing.Activities@24.10.4
#:package UiPath.UIAutomation.Activities@25.10.12
#:package Microsoft.CodeAnalysis.VisualBasic@4.5.0-2.22527.10
#:property TargetFramework=net6.0-windows7.0
#:property PublishAot=false

using System.Activities;
using System.Activities.Expressions;
using System.Activities.XamlIntegration;
using System.Activities.Statements;
using System.Reflection;
using System.Text.Json;
using System.Xaml;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

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
// positional:               optional project.json path
// --text-out <path>         IR tree to file  (summary still to stdout)
// --json                    JSON to stdout — suppresses all human text
// --json-out <path>         JSON to file     (summary still to stdout)
// --trace-resolve           emit PROBE lines to stderr + post-run Studio assembly inventory
// --expr-json-out <path>    Roslyn VB expression analysis to JSON file (Layer 2)
// --expr-text-out <path|-> Roslyn-annotated IR tree to file (Layer 2)
// --rule-json-out <path>   Rule model (conditions + assignments) to JSON (Layer 3)
string? projectJsonArg = null;
bool    emitJson       = false;
string? jsonOutArg     = null;
string? textOutArg     = null;
bool    traceResolve   = false;
string? exprJsonOutArg  = null;
string? exprTextOutArg  = null;
string? ruleJsonOutArg  = null;

for (int i = 0; i < args.Length; i++)
{
    if      (args[i] == "--text-out"      && i + 1 < args.Length) { textOutArg = args[++i]; }
    else if (args[i] == "--json")                                   { emitJson = true; }
    else if (args[i] == "--json-out"      && i + 1 < args.Length) { emitJson = true; jsonOutArg = args[++i]; }
    else if (args[i] == "--trace-resolve")                          { traceResolve = true; }
    else if (args[i] == "--expr-json-out"  && i + 1 < args.Length) { exprJsonOutArg  = args[++i]; }
    else if (args[i] == "--expr-text-out"  && i + 1 < args.Length) { exprTextOutArg  = args[++i]; }
    else if (args[i] == "--rule-json-out"  && i + 1 < args.Length) { ruleJsonOutArg  = args[++i]; }
    else if (!args[i].StartsWith("--"))                             { projectJsonArg = args[i]; }
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
    string relPath,
    ref int warnCount)
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
            warnCount++;
            Console.Error.WriteLine(FormatGetActivitiesWarn(relPath, typeName, displayName, ex2));
            rawChildren = Enumerable.Empty<Activity>();
        }
    }
    catch (Exception ex)
    {
        warnCount++;
        Console.Error.WriteLine(FormatGetActivitiesWarn(relPath, typeName, displayName, ex));
        rawChildren = Enumerable.Empty<Activity>();
    }

    var children = new List<WfNode>();
    foreach (var child in rawChildren)
    {
        var childNode = Build(child, annotations, ref idCounter, allExpressions, relPath, ref warnCount);
        if (childNode is not null) children.Add(childNode);
    }

    return new WfNode(id, typeName, displayName, annotation, arguments, variables, expressions, children, "RuntimeResolved");
}

// ── XAML fallback builder ─────────────────────────────────────────────────────
// Used when ActivityXamlServices.Load() fails (Level A).  Parses the raw XAML
// element tree and emits WfNodes marked Resolution = "XamlFallback".
//
// Activity detection: any element with a 'DisplayName' attribute is an activity.
// This covers all UiPath designer-placed activities reliably.
//
// Transparent wrappers: slot elements (dotted names like If.Then, ForEach.Body)
// and delegate containers (ActivityAction, Catch) are looked through to reach
// their activity children.

static bool XamlIsActivity(XElement el) =>
    el.Attribute("DisplayName") is not null;

static bool XamlIsTransparentWrapper(XElement el)
{
    var ln = el.Name.LocalName;
    return ln.Contains('.') || ln is "ActivityAction" or "Catch";
}

static IEnumerable<XElement> XamlActivityChildren(XElement el)
{
    foreach (var child in el.Elements())
    {
        if (XamlIsActivity(child))
            yield return child;
        else if (XamlIsTransparentWrapper(child))
            foreach (var inner in XamlActivityChildren(child))
                yield return inner;
        // else: skip — metadata, view state, variable declarations, etc.
    }
}

static List<WfVariable> XamlExtractVariables(XElement el)
{
    XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    return el.Elements()
        .Where(c => c.Name.LocalName.EndsWith(".Variables"))
        .SelectMany(sv => sv.Elements().Where(v => v.Name.LocalName == "Variable"))
        .Select(v => new WfVariable(
            v.Attribute("Name")?.Value ?? "?",
            v.Attribute(xamlNs + "TypeArguments")?.Value ?? "?"))
        .ToList();
}

static List<WfArgument> XamlExtractArguments(XElement el)
{
    XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    return el.Elements(xamlNs + "Members")
        .SelectMany(m => m.Elements(xamlNs + "Property"))
        .Select(p =>
        {
            var typeStr = p.Attribute("Type")?.Value ?? "?";
            var dir = typeStr.StartsWith("InArgument")  ? "In"
                    : typeStr.StartsWith("OutArgument") ? "Out"
                    : "InOut";
            return new WfArgument(p.Attribute("Name")?.Value ?? "?", dir, typeStr);
        })
        .ToList();
}

static List<WfExpression> XamlExtractExpressions(XElement el, List<string> allExpressions)
{
    var exprs = new List<WfExpression>();

    // WorkflowFileName — plain string attribute (no [ ] wrapper)
    var wfFileName = el.Attribute("WorkflowFileName")?.Value;
    if (wfFileName is not null)
        exprs.Add(new WfExpression("WorkflowFileName", wfFileName));

    // Inline attribute expressions wrapped in [ ]
    foreach (var (attrName, propName) in new[]
    {
        ("Condition", "Condition"), ("Message", "Message"),
        ("Value", "Value"), ("To", "To"),
    })
    {
        var raw = el.Attribute(attrName)?.Value?.Trim();
        if (raw is null || !raw.StartsWith('[') || !raw.EndsWith(']') || raw.Length <= 2) continue;
        var text = raw[1..^1];
        exprs.Add(new WfExpression(propName, text));
        allExpressions.Add(text);
    }

    // Slot child expressions — look for InArgument/OutArgument text in slot wrappers
    foreach (var (slotSuffix, propName) in new[]
    {
        (".Condition", "Condition"), (".Value", "Value"),
        (".To", "To"), (".Message", "Message"),
    })
    {
        var slot = el.Elements().FirstOrDefault(c => c.Name.LocalName.EndsWith(slotSuffix));
        if (slot is null) continue;
        var argEl = slot.Elements().FirstOrDefault(c =>
            c.Name.LocalName is "InArgument" or "OutArgument");
        var raw = argEl?.Value?.Trim();
        if (raw is null || !raw.StartsWith('[') || !raw.EndsWith(']') || raw.Length <= 2) continue;
        var text = raw[1..^1];
        if (exprs.Any(e => e.Name == propName && e.Value == text)) continue; // dedup with inline
        exprs.Add(new WfExpression(propName, text));
        allExpressions.Add(text);
    }

    return exprs;
}

static WfNode BuildFromXaml(
    XElement el,
    IReadOnlyDictionary<string, string> annotations,
    ref int idCounter,
    List<string> allExpressions,
    string relPath)
{
    XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    var ln          = el.Name.LocalName;
    var typeName    = ln == "Activity" ? "DynamicActivity" : ln;
    var displayName = el.Attribute("DisplayName")?.Value
                   ?? el.Attribute(xamlNs + "Class")?.Value
                   ?? typeName;
    var id = (++idCounter).ToString();

    annotations.TryGetValue(displayName, out var annotation);

    var arguments   = XamlExtractArguments(el);
    var variables   = XamlExtractVariables(el);
    var expressions = XamlExtractExpressions(el, allExpressions);

    var children = new List<WfNode>();
    foreach (var c in XamlActivityChildren(el))
        children.Add(BuildFromXaml(c, annotations, ref idCounter, allExpressions, relPath));

    int? xamlLine = null;
    if (el is IXmlLineInfo xli && xli.HasLineInfo())
        xamlLine = xli.LineNumber;

    return new WfNode(id, typeName, displayName, annotation, arguments, variables, expressions, children, "XamlFallback", xamlLine);
}

// ── XAML line number helpers ──────────────────────────────────────────────────
// For RuntimeResolved nodes we have no XElement, so we do a best-effort match
// by DisplayName after the WF tree is built.  First occurrence wins; duplicates
// (activities sharing a DisplayName) get whichever line appears first in the file.
static Dictionary<string, int> BuildDisplayNameLineMap(string xamlPath)
{
    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var doc = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
    foreach (var el in doc.Descendants())
    {
        var dn = el.Attribute("DisplayName")?.Value;
        if (dn is null) continue;
        if (el is IXmlLineInfo li && li.HasLineInfo() && !map.ContainsKey(dn))
            map[dn] = li.LineNumber;
    }
    return map;
}

static WfNode AttachXamlLines(WfNode node, Dictionary<string, int> map)
{
    var line     = node.XamlLine ?? (map.TryGetValue(node.DisplayName, out var l) ? (int?)l : null);
    var children = node.Children.Select(c => AttachXamlLines(c, map)).ToList();
    return node with { XamlLine = line, Children = children };
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

    var typeLabel = node.Resolution == "XamlFallback" ? $"~{node.Type}" : node.Type;
    output.WriteLine($"{indent}[{typeLabel}]  {node.DisplayName}");

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

// ── Assembly resolver — NuGet transitive deps only ────────────────────────────
// Studio DLLs are intentionally excluded.  Activities that require Studio at
// construction time (e.g. ForEachRow → UiPath.Activities.Contracts) will cause
// ActivityXamlServices.Load() to throw, triggering Level-A XAML fallback.
//
// --trace-resolve verifies no Studio assembly sneaks in via NuGet transitive deps.
// studioLoadedNames tracks UiPath.* names resolved from outside AppContext.BaseDirectory.
// With Studio excluded, this set should always be empty.
var studioLoadedNames     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
bool studioUsedForCurrent = false;

var assemblyProbePaths = new List<string> { AppContext.BaseDirectory };

AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
{
    var name = new AssemblyName(args.Name).Name;
    if (name is null) return null;
    foreach (var dir in assemblyProbePaths)
    {
        // Studio paths (net472, Studio root) are restricted to UiPath.* names.
        // AppContext.BaseDirectory (index 0) is unrestricted — all NuGet-restored deps are safe.
        if (dir != AppContext.BaseDirectory && !name.StartsWith("UiPath")) continue;
        var path = Path.Combine(dir, $"{name}.dll");
        if (File.Exists(path))
            try
            {
                var asm = Assembly.LoadFrom(path);
                if (dir != AppContext.BaseDirectory)
                {
                    studioLoadedNames.Add(name);
                    studioUsedForCurrent = true;
                }
                if (traceResolve)
                    Console.Error.WriteLine($"PROBE loaded  {name,-50}  {dir}");
                return asm;
            }
            catch { }
    }
    // Only log misses for UiPath.* names — non-UiPath misses are expected framework noise.
    if (name.StartsWith("UiPath") && traceResolve)
        Console.Error.WriteLine($"PROBE miss    {name}");
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

// Snapshot Studio deps loaded during init (dependency loading + schema context construction).
// After this point the resolver resets to track only per-workflow loads.
// Per-workflow 'studio-assisted' means a NEW Studio assembly was pulled in during THAT
// workflow's processing — not that it merely benefits from assemblies loaded at init.
var initStudioNames = new HashSet<string>(studioLoadedNames, StringComparer.OrdinalIgnoreCase);
studioLoadedNames.Clear();
studioUsedForCurrent = false;

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

// Header: operational metadata → stderr; suppress in pure machine mode (--json to stdout)
if (!jsonToStdout)
{
    Console.Error.WriteLine($"project.json  : {projectJsonPath}");
    Console.Error.WriteLine($"name          : {projectName}");
    Console.Error.WriteLine($"entrypoints   : {entryPoints.Count}");
    if (traceResolve && initStudioNames.Count > 0)
        Console.Error.WriteLine($"studio (init) : {string.Join(", ", initStudioNames.OrderBy(x => x))}");
    Console.Error.WriteLine();
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

    studioUsedForCurrent = false;

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
        Console.Error.WriteLine($"LOAD ERROR [{relPath}]: {ex.Message}");
        Console.Error.WriteLine($"           → switching to Level-A XAML fallback");
        // Level A XAML fallback: WF object model unavailable; parse raw XAML structure.
        // All nodes in this workflow will carry Resolution = "XamlFallback".
        try
        {
            var xamlDoc2        = XDocument.Load(fullPath, LoadOptions.SetLineInfo);
            var annotations2    = ExtractAnnotations(fullPath);
            var allExpressions2 = new List<string>();
            int idCounter2      = 0;
            var wfNode2 = BuildFromXaml(xamlDoc2.Root!, annotations2,
                                        ref idCounter2, allExpressions2, relPath);

            workflows[relPath] = new WfWorkflow(
                relPath, wfNode2,
                annotations2.Count,
                allExpressions2.Distinct().Count(),
                allExpressions2.Count);

            foreach (var (nodeId2, rawFileName2) in FindInvokeEdges(wfNode2))
            {
                var targetRel2  = NormalizeRelPath(rawFileName2);
                var targetFull2 = Path.GetFullPath(Path.Combine(projectRoot, targetRel2));
                var resolved2   = File.Exists(targetFull2);
                invokeEdges.Add(new InvokeEdge(relPath, nodeId2, targetRel2, resolved2));
                if (resolved2 && queuedOrBuilt.Add(targetRel2))
                    queue.Enqueue(targetRel2);
            }

            if (!jsonToStdout)
            {
                var freq2     = new Dictionary<string, int>();
                CollectStats(wfNode2, freq2);
                var actCount2 = freq2.Values.Sum();
                Console.WriteLine($"  [xaml-fallback   ]  {relPath,-52}  {actCount2,4} activities  " +
                                  $"{annotations2.Count,3} annotations  " +
                                  $"{allExpressions2.Distinct().Count(),3} expressions");
            }
        }
        catch (Exception ex2)
        {
            Console.Error.WriteLine($"XAML FALLBACK ERROR [{relPath}]: {ex2.Message}");
        }
        continue;
    }

    var annotations    = ExtractAnnotations(fullPath);
    var allExpressions = new List<string>();
    int idCounter      = 0;
    int warnCount      = 0;
    var wfNode         = Build(root, annotations, ref idCounter, allExpressions, relPath, ref warnCount);
    if (wfNode is null) continue;

    // Level B XAML fallback: WF load succeeded but GetActivities hit a boundary
    // (constructor called Studio-only dep).  Rebuild the whole tree from XAML so all
    // nodes are present, and clear the partial expression accumulator first.
    bool usedXamlFallback = false;
    if (warnCount > 0)
    {
        Console.Error.WriteLine($"           → WF descent incomplete ({warnCount} warn(s)), rebuilding from XAML");
        try
        {
            var xamlDocB = XDocument.Load(fullPath, LoadOptions.SetLineInfo);
            allExpressions.Clear();
            int idCounterB = 0;
            wfNode = BuildFromXaml(xamlDocB.Root!, annotations, ref idCounterB, allExpressions, relPath);
            usedXamlFallback = true;
        }
        catch (Exception exB)
        {
            Console.Error.WriteLine($"XAML FALLBACK ERROR [{relPath}]: {exB.Message}");
            usedXamlFallback = true; // still mark as fallback — WF tree was incomplete
        }
    }

    // Classify resolution boundary:
    //   xaml-fallback    — WF descent partially failed (GetActivities WARN fired)
    //   studio-assisted  — WF descent succeeded but triggered a new Studio assembly load
    //   pure-nuget       — WF descent succeeded using only NuGet-restored assemblies
    // Note: once a Studio assembly is loaded into the AppDomain it stays loaded, so
    // subsequent workflows that share the same activity types won't re-trigger the
    // resolver.  'studio-assisted' therefore marks the FIRST workflow to cause each load;
    // later workflows appear 'pure-nuget' even if they use the same types.
    var resolution = usedXamlFallback       ? "xaml-fallback"
                   : studioUsedForCurrent   ? "studio-assisted"
                   :                          "pure-nuget";

    // Attach source line numbers to RuntimeResolved nodes (best-effort DisplayName match)
    if (!usedXamlFallback)
    {
        var lineMap = BuildDisplayNameLineMap(fullPath);
        wfNode = AttachXamlLines(wfNode, lineMap);
    }

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
        Console.WriteLine($"  [{resolution,-16}]  {relPath,-52}  {actCount,4} activities  " +
                          $"{annotations.Count,3} annotations  " +
                          $"{allExpressions.Distinct().Count(),3} expressions");
    }
}

var wfProject = new WfProject(projectName, mainPath, entryPoints, workflows, invokeEdges);

// Post-run Studio assembly inventory (--trace-resolve only)
if (traceResolve)
{
    Console.Error.WriteLine();
    if (studioLoadedNames.Count > 0)
    {
        Console.Error.WriteLine($"── Studio assemblies loaded during BFS ({studioLoadedNames.Count}) ──");
        foreach (var n in studioLoadedNames.OrderBy(x => x))
            Console.Error.WriteLine($"  {n}");
    }
    else
    {
        Console.Error.WriteLine("── Studio assemblies loaded during BFS: none ──");
    }
    Console.Error.WriteLine();
}

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
    Console.Error.WriteLine($"tree written to: {resolvedTextOut}");

// ── JSON emit ─────────────────────────────────────────────────────────────────
if (emitJson)
{
    if (jsonOutArg is not null)
    {
        using var jsonWriter = new StreamWriter(jsonOutArg, false,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        RenderJson(wfProject, jsonWriter);
        Console.Error.WriteLine($"JSON written to: {jsonOutArg}");
    }
    else
    {
        RenderJson(wfProject, Console.Out);
    }
}

// ── Expression analysis (Layer 2) ────────────────────────────────────────────
if (exprJsonOutArg is not null || exprTextOutArg is not null || ruleJsonOutArg is not null)
{
    var analyses = AnalyzeProject(wfProject);

    if (exprJsonOutArg is not null)
    {
        var analysisJson = JsonSerializer.Serialize(analyses,
            new JsonSerializerOptions { WriteIndented = true });
        using var exprWriter = new StreamWriter(exprJsonOutArg, false,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        exprWriter.Write(analysisJson);
        Console.Error.WriteLine($"expr-analysis written to: {exprJsonOutArg}  ({analyses.Count} expressions)");
    }

    if (exprTextOutArg is not null)
    {
        // Build lookup: (workflowPath, exprName, sourceText) → analysis
        // On collision (identical expression in same slot of same workflow) last writer wins — acceptable.
        var lookup = analyses
            .GroupBy(a => (a.WorkflowPath, a.ExpressionName, a.SourceText))
            .ToDictionary(g => g.Key, g => g.Last());

        using var exprTextWriter = exprTextOutArg == "-"
            ? (TextWriter)new StreamWriter(Console.OpenStandardOutput(), System.Text.Encoding.UTF8, leaveOpen: true)
            : new StreamWriter(exprTextOutArg, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var wf in wfProject.Workflows.Values)
        {
            var irNode   = NormalizeTree(wf.Root);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { wf.Path };
            exprTextWriter.WriteLine();
            exprTextWriter.WriteLine($"  {wf.Path}");
            exprTextWriter.WriteLine($"  {new string('─', wf.Path.Length)}");
            RenderWithAnalysis(irNode, 2, exprTextWriter, wfProject, visiting, lookup, wf.Path);
        }

        if (exprTextOutArg != "-")
            Console.Error.WriteLine($"expr-text written to: {exprTextOutArg}  ({analyses.Count} expressions)");
    }

    if (ruleJsonOutArg is not null)
    {
        var model    = BuildRuleModel(wfProject.Name, analyses);
        var ruleJson = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ruleJsonOutArg, ruleJson,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.Error.WriteLine($"rule-model written to: {ruleJsonOutArg}  " +
            $"({model.Conditions.Count} conditions, {model.Assignments.Count} assignments)");
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

// ── Layer 2: Roslyn VB expression analysis ────────────────────────────────────
// Operates on the WfNode tree AFTER it is fully built (both RuntimeResolved and
// XamlFallback nodes expose the same WfExpression strings).  No WF loading is
// touched; no Studio DLLs are required.

// Best-effort VB type inference.  Creates a minimal VisualBasicCompilation for
// the expression, reads the SemanticModel, and returns the display string of the
// inferred type.  Returns null on any failure — this path is never load-bearing.
static string? TryInferType(string expressionText)
{
    try
    {
        var wrapped =
            "Imports System\n" +
            "Imports System.Data\n" +
            "Module M\n" +
            "  Sub S()\n" +
            $"    Dim __ = {expressionText}\n" +
            "  End Sub\n" +
            "End Module";
        var tree = VisualBasicSyntaxTree.ParseText(wrapped);
        var compilation = VisualBasicCompilation.Create("ExprInference",
            new[] { tree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Data.DataTable).Assembly.Location),
            },
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model    = compilation.GetSemanticModel(tree);
        var initExpr = tree.GetRoot()
            .DescendantNodes().OfType<EqualsValueSyntax>()
            .FirstOrDefault()?.Value;
        if (initExpr is null) return null;
        var t = model.GetTypeInfo(initExpr);
        return (t.Type ?? t.ConvertedType)?.ToDisplayString();
    }
    catch { return null; }
}

// ── Predicate extractor ───────────────────────────────────────────────────────
// Classifies simple VB boolean patterns into a structured WfPredicate.
// Returns null for patterns that don't cleanly match — no false structure imposed.
static WfPredicate? ExtractPredicate(string sourceText)
{
    try
    {
        // Wrap in a Sub so VB parses `x = "A"` inside a conditional context (equality,
        // not assignment).  Module/Sub wrapper mirrors TryInferType for consistency.
        var wrapped =
            "Module M\n" +
            "  Sub S()\n" +
            "    If (" + sourceText + ") Then\n" +
            "    End If\n" +
            "  End Sub\n" +
            "End Module";
        var tree    = VisualBasicSyntaxTree.ParseText(wrapped);
        // IfStatementSyntax.Condition is the condition expression.
        var ifStmt = tree.GetRoot().DescendantNodes()
                         .OfType<IfStatementSyntax>().FirstOrDefault();
        if (ifStmt is null) return null;
        // Unwrap outer parentheses added by our wrapper: If (expr) Then → expr
        ExpressionSyntax first = ifStmt.Condition;
        while (first is ParenthesizedExpressionSyntax paren)
            first = paren.Expression;
        switch (first)
        {
            // x = "A"  /  x <> "A"  /  x > 5  /  x <= 10  etc.
            case BinaryExpressionSyntax bin
                when bin.Kind() is SyntaxKind.EqualsExpression
                               or SyntaxKind.NotEqualsExpression
                               or SyntaxKind.GreaterThanExpression
                               or SyntaxKind.GreaterThanOrEqualExpression
                               or SyntaxKind.LessThanExpression
                               or SyntaxKind.LessThanOrEqualExpression:
                var op   = bin.OperatorToken.ValueText;
                var lhs  = bin.Left  is IdentifierNameSyntax  lid ? lid.Identifier.ValueText : null;
                var rhs  = bin.Right is LiteralExpressionSyntax rl ? rl.Token.ValueText       : null;
                var bkind = bin.Kind() == SyntaxKind.EqualsExpression   ? "equality"
                          : bin.Kind() == SyntaxKind.NotEqualsExpression ? "inequality"
                          : "comparison";
                return new WfPredicate(bkind, lhs, op, rhs, null, Array.Empty<WfPredicate>());

            // x Is Nothing  /  x IsNot Nothing
            case BinaryExpressionSyntax isBin
                when isBin.Kind() is SyntaxKind.IsExpression or SyntaxKind.IsNotExpression:
                var isVar  = isBin.Left is IdentifierNameSyntax iv ? iv.Identifier.ValueText : null;
                var isKind = isBin.Kind() == SyntaxKind.IsExpression ? "null-check" : "not-null-check";
                return new WfPredicate(isKind, isVar, isBin.OperatorToken.ValueText, null, null,
                    Array.Empty<WfPredicate>());

            // x OrElse y  /  x AndAlso y
            case BinaryExpressionSyntax cmpd
                when cmpd.Kind() is SyntaxKind.OrElseExpression or SyntaxKind.AndAlsoExpression:
                var ckind = cmpd.Kind() == SyntaxKind.OrElseExpression ? "compound-or" : "compound-and";
                var cleft  = ExtractPredicate(cmpd.Left.ToString());
                var cright = ExtractPredicate(cmpd.Right.ToString());
                if (cleft is null || cright is null) return null;
                return new WfPredicate(ckind, null, null, null, null,
                    new[] { cleft, cright }.ToList().AsReadOnly());

            // Not expr
            case UnaryExpressionSyntax neg when neg.Kind() == SyntaxKind.NotExpression:
                var inner = ExtractPredicate(neg.Operand.ToString());
                if (inner is null) return null;
                return new WfPredicate("negation", null, null, null, null,
                    new[] { inner }.ToList().AsReadOnly());

            // String.IsNullOrEmpty(x)  /  String.IsNullOrWhiteSpace(x)
            case InvocationExpressionSyntax inv:
                var fname = inv.Expression.ToString();
                if (fname is "String.IsNullOrEmpty" or "String.IsNullOrWhiteSpace")
                {
                    var farg = inv.ArgumentList.Arguments.FirstOrDefault()?.ToString();
                    return new WfPredicate("known-function", farg, null, null, fname,
                        Array.Empty<WfPredicate>());
                }
                return null;

            default:
                return null;
        }
    }
    catch { return null; }
}

static WfExpressionAnalysis AnalyzeExpression(
    string workflowPath, WfNode wfNode, WfExpression expr)
{
    var sourceText = expr.Value;

    // Parse as a VB script fragment.  SourceCodeKind.Script handles bare
    // expressions without requiring a full module/class wrapper.
    var tree = VisualBasicSyntaxTree.ParseText(
        sourceText,
        VisualBasicParseOptions.Default.WithKind(SourceCodeKind.Script));
    var root = tree.GetRoot();

    // Navigate to the first real expression node.
    // Script root: CompilationUnitSyntax → first statement child → first expression child.
    var firstExpr = root.DescendantNodes().OfType<ExpressionSyntax>().FirstOrDefault();
    var syntaxKind = firstExpr?.Kind().ToString() ?? root.Kind().ToString();

    var identifiers    = new List<string>();
    var memberAccesses = new List<string>();
    var invocations    = new List<string>();
    var literals       = new List<string>();

    foreach (var n in root.DescendantNodes())
    {
        switch (n)
        {
            case IdentifierNameSyntax id:
                identifiers.Add(id.Identifier.ValueText); break;
            case MemberAccessExpressionSyntax ma:
                memberAccesses.Add(ma.ToString()); break;
            case InvocationExpressionSyntax inv:
                invocations.Add(inv.Expression.ToString()); break;
            case LiteralExpressionSyntax lit:
                literals.Add(lit.Token.ValueText); break;
        }
    }

    var diagnostics = tree.GetDiagnostics()
        .Select(d => $"{d.Id}: {d.GetMessage()}")
        .ToList();

    var predicate = expr.Name == "Condition" ? ExtractPredicate(sourceText) : null;

    return new WfExpressionAnalysis(
        workflowPath,
        wfNode.Id,
        wfNode.Type,
        wfNode.DisplayName,
        wfNode.XamlLine,
        expr.Name,
        sourceText,
        syntaxKind,
        identifiers.Distinct().ToList().AsReadOnly(),
        memberAccesses.Distinct().ToList().AsReadOnly(),
        invocations.Distinct().ToList().AsReadOnly(),
        literals.ToList().AsReadOnly(),
        diagnostics.AsReadOnly(),
        TryInferType(sourceText),
        predicate);
}

// ── Rule model builder (Layer 3) ──────────────────────────────────────────────
static WfRuleModel BuildRuleModel(string projectName, List<WfExpressionAnalysis> analyses)
{
    var conditions  = new List<WfConditionRule>();
    var assignExprs = new List<WfExpressionAnalysis>();

    foreach (var a in analyses)
    {
        if (a.ExpressionName == "Condition")
            conditions.Add(new WfConditionRule(
                a.WorkflowPath, a.NodeId, a.NodeDisplayName, a.XamlLine,
                a.SourceText, a.Predicate));
        else if (a.ExpressionName is "Value" or "To")
            assignExprs.Add(a);
    }

    var assignments = assignExprs
        .GroupBy(a => (a.WorkflowPath, a.NodeId))
        .Select(g => new WfAssignmentRule(
            g.Key.WorkflowPath, g.Key.NodeId,
            g.First().NodeDisplayName, g.First().XamlLine,
            g.FirstOrDefault(x => x.ExpressionName == "To")?.SourceText    ?? "",
            g.FirstOrDefault(x => x.ExpressionName == "Value")?.SourceText ?? ""))
        .ToList();

    return new WfRuleModel(projectName, conditions.AsReadOnly(), assignments.AsReadOnly());
}

static void CollectNodeExpressions(
    WfNode node, string workflowPath, List<WfExpressionAnalysis> results)
{
    foreach (var expr in node.Expressions)
    {
        if (expr.Name == "WorkflowFileName") continue;     // file path, not VB
        if (string.IsNullOrWhiteSpace(expr.Value)) continue;
        results.Add(AnalyzeExpression(workflowPath, node, expr));
    }
    foreach (var child in node.Children)
        CollectNodeExpressions(child, workflowPath, results);
}

// Mirrors Render() but augments each expression line with Roslyn analysis details.
// Lookup key: (workflowPath, exprName, sourceText) — stable across NormalizeTree.
static void RenderWithAnalysis(
    WfNode node, int depth, TextWriter output,
    WfProject project, HashSet<string> visiting,
    IReadOnlyDictionary<(string, string, string), WfExpressionAnalysis> lookup,
    string currentWfPath)
{
    var indent  = new string(' ', depth * 2);
    var indentP = indent + "  ";
    var indentA = indentP + "  ";

    var typeLabel = node.Resolution == "XamlFallback" ? $"~{node.Type}" : node.Type;
    output.WriteLine($"{indent}[{typeLabel}]  {node.DisplayName}");

    if (!string.IsNullOrEmpty(node.Annotation))
        output.WriteLine($"{indentP}// {node.Annotation.Replace("\r\n", " | ").Replace('\n', '|').Trim()}");

    foreach (var arg in node.Arguments)
        output.WriteLine($"{indentP}arg {arg.Direction} {arg.Name} : {arg.Type}");

    foreach (var v in node.Variables)
        output.WriteLine($"{indentP}var {v.Name} : {v.Type}");

    foreach (var expr in node.Expressions)
    {
        if (lookup.TryGetValue((currentWfPath, expr.Name, expr.Value), out var a))
        {
            var type    = a.InferredType ?? "?";
            var warn    = a.Diagnostics.Count > 0 ? " ⚠" : "";
            var ids     = a.Identifiers.Count  > 0 ? $"  ids:{string.Join(",", a.Identifiers)}" : "";
            var lits    = a.Literals.Count     > 0 ? $"  lit:{string.Join(",", a.Literals.Select(l => $"\"{l}\""))}" : "";
            var lineRef = a.XamlLine.HasValue ? $"  @L{a.XamlLine}" : "";
            output.WriteLine($"{indentP}.{expr.Name} = {expr.Value}   ∷ {type}{warn}{ids}{lits}{lineRef}");
        }
        else
        {
            output.WriteLine($"{indentP}.{expr.Name} = {expr.Value}");
        }
    }

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
                RenderWithAnalysis(NormalizeTree(callee.Root), depth + 1, output,
                    project, visiting, lookup, targetPath);
                visiting.Remove(targetPath);
            }
        }
        return;
    }

    foreach (var child in node.Children)
        RenderWithAnalysis(child, depth + 1, output, project, visiting, lookup, currentWfPath);
}

static List<WfExpressionAnalysis> AnalyzeProject(WfProject project)
{
    var results = new List<WfExpressionAnalysis>();
    foreach (var wf in project.Workflows.Values)
        CollectNodeExpressions(wf.Root, wf.Path, results);
    return results;
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
    List<WfNode> Children,
    string Resolution = "RuntimeResolved",// "RuntimeResolved" | "XamlFallback"
    int? XamlLine = null                  // source line in XAML file; null when not resolvable
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

// ── Expression analysis model (Layer 2) ──────────────────────────────────────

// Structured representation of a simple VB boolean predicate.
// null when the expression does not match a supported pattern.
record WfPredicate(
    string Kind,         // "equality" | "inequality" | "comparison" | "null-check"
                         // | "not-null-check" | "known-function" | "compound-and"
                         // | "compound-or" | "negation"
    string? Variable,    // LHS identifier for simple predicates; null for compound/function
    string? Operator,    // "=" | "<>" | ">" | ">=" | "<" | "<=" | "Is" | "IsNot"
    string? Literal,     // RHS literal value (string or number); null when not a literal
    string? FuncName,    // e.g. "String.IsNullOrEmpty" for known-function kind
    IReadOnlyList<WfPredicate> Operands  // sub-predicates for compound/negation; empty otherwise
);

record WfExpressionAnalysis(
    string WorkflowPath,
    string NodeId,
    string NodeType,
    string NodeDisplayName,
    int?   XamlLine,
    string ExpressionName,
    string SourceText,
    string SyntaxKind,
    IReadOnlyList<string> Identifiers,
    IReadOnlyList<string> MemberAccesses,
    IReadOnlyList<string> Invocations,
    IReadOnlyList<string> Literals,
    IReadOnlyList<string> Diagnostics,
    string?    InferredType,
    WfPredicate? Predicate       // non-null only for Condition expressions that match a known pattern
);

// ── Rule model (Layer 3) ──────────────────────────────────────────────────────

record WfConditionRule(
    string WorkflowPath,
    string NodeId,
    string DisplayName,
    int?   XamlLine,
    string SourceText,
    WfPredicate? Predicate
);

record WfAssignmentRule(
    string WorkflowPath,
    string NodeId,
    string DisplayName,
    int?   XamlLine,
    string Target,   // .To expression
    string Value     // .Value expression
);

record WfRuleModel(
    string ProjectName,
    IReadOnlyList<WfConditionRule>  Conditions,
    IReadOnlyList<WfAssignmentRule> Assignments
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
