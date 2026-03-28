#!/usr/bin/env dotnet-script
// workflow_tree.cs — load UiPath XAML through the CoreWF activity object model
// Usage:
//   dotnet run --file scripts/workflow_tree.cs
//   dotnet run --file scripts/workflow_tree.cs -- path/to/project.json

#:package UiPath.Workflow@6.0.3
#:package UiPath.System.Activities@24.10.4
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

var projectJsonPath = args.Length > 0 ? args[0] : FindProjectJson();
if (projectJsonPath is null || !File.Exists(projectJsonPath))
{
    Console.Error.WriteLine("project.json not found");
    return 1;
}

projectJsonPath = Path.GetFullPath(projectJsonPath);
var projectRoot  = Path.GetDirectoryName(projectJsonPath)!;

using var jsonDoc = JsonDocument.Parse(File.ReadAllText(projectJsonPath));
var projectJson = jsonDoc.RootElement;

Console.WriteLine($"project.json : {projectJsonPath}");
Console.WriteLine($"name         : {projectJson.GetProperty("name").GetString()}");
Console.WriteLine($"expressionLang: {projectJson.GetProperty("expressionLanguage").GetString()}");
Console.WriteLine($"targetFramework: {projectJson.GetProperty("targetFramework").GetString()}");
Console.WriteLine();

// ── Collect implementation XAML files ────────────────────────────────────────
var implFiles = Directory
    .EnumerateFiles(projectRoot, "*.xaml", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}."))
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}")
             && !f.Contains($"{Path.DirectorySeparatorChar}Framework{Path.DirectorySeparatorChar}")
             && !Path.GetFileName(f).Equals("README.xaml", StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f)
    .ToList();

Console.WriteLine($"Implementation XAML files: {implFiles.Count}");
Console.WriteLine();

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
        if (exprProp?.GetValue(argValue) is ITextExpression te
            && !string.IsNullOrWhiteSpace(te.ExpressionText))
            return te.ExpressionText;
    }
    catch { }
    return null;
}

// ── Model builder ─────────────────────────────────────────────────────────────
// Build traverses the WF activity graph and returns a WfNode tree.
// It never prints; all rendering is deferred to Render().
//
// allExpressions is a side accumulator: Build appends every expression text it
// discovers (both node-local named ones and non-structural ones found by
// ExtractExpressionsFromObject).  Duplicates are expected and preserved;
// dedup happens only at the summary distinct-count line.
static WfNode? Build(
    Activity activity,
    IReadOnlyDictionary<string, string> annotations,
    ref int idCounter,
    List<string> allExpressions)
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
    foreach (var propName in new[] { "Condition", "Value", "To", "Message" })
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
        catch { rawChildren = Enumerable.Empty<Activity>(); }
    }

    var children = new List<WfNode>();
    foreach (var child in rawChildren)
    {
        var childNode = Build(child, annotations, ref idCounter, allExpressions);
        if (childNode is not null) children.Add(childNode);
    }

    return new WfNode(id, typeName, displayName, annotation, arguments, variables, expressions, children);
}

// ── Renderer ──────────────────────────────────────────────────────────────────
// Render walks a WfNode tree and produces console output.
// Depth is presentation-derived; it is not stored on the node.
static void Render(WfNode node, int depth)
{
    var indent  = new string(' ', depth * 2);
    var indentP = indent + "  ";

    Console.WriteLine($"{indent}[{depth}] {node.Type}  [{node.DisplayName}]");

    if (!string.IsNullOrEmpty(node.Annotation))
        Console.WriteLine($"{indentP}// {node.Annotation.Replace("\r\n", " | ").Replace('\n', '|').Trim()}");

    foreach (var arg in node.Arguments)
        Console.WriteLine($"{indentP}arg {arg.Name} : {arg.Type}");

    foreach (var v in node.Variables)
        Console.WriteLine($"{indentP}var {v.Name} : {v.Type}");

    foreach (var expr in node.Expressions)
        Console.WriteLine($"{indentP}.{expr.Name} = {expr.Value}");

    foreach (var child in node.Children)
        Render(child, depth + 1);
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


// ── Build a XamlSchemaContext that knows about UiPath types ──────────────────
// ActivityXamlServices.Load(stream, settings) uses the internal
// DynamicActivityReaderSchemaContext which does NOT scan XmlnsDefinitionAttribute
// from loaded assemblies.  We bypass it by seeding a standard XamlSchemaContext
// with both the CoreWF assembly and UiPath.System.Activities, then passing a
// XamlXmlReader built from that context to Load(XamlReader, settings).
Assembly uiPathAsm;
try
{
    uiPathAsm = Assembly.Load("UiPath.System.Activities");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not load UiPath.System.Activities: {ex.Message}");
    return 1;
}

Console.WriteLine($"UiPath.System.Activities : {uiPathAsm.Location}");
Console.WriteLine($"  MultipleAssign  : {uiPathAsm.GetType("UiPath.Core.Activities.MultipleAssign")?.FullName ?? "NOT FOUND"}");
Console.WriteLine($"  AssignOperation : {uiPathAsm.GetType("UiPath.Core.Activities.AssignOperation")?.FullName ?? "NOT FOUND"}");
Console.WriteLine($"  QueueItem       : {uiPathAsm.GetType("UiPath.Core.QueueItem")?.FullName ?? "NOT FOUND"}");
Console.WriteLine();

// Do NOT pass uiPathAsm to the XamlSchemaContext constructor.
// That constructor calls GetCustomAttributes() on every passed assembly to scan
// XmlnsDefinitionAttribute entries. On UiPath.System.Activities that scan tries
// to decode Persistence/Telemetry attribute types that live in
// UiPath.Activities.Contracts — a Studio-only DLL not available here.
//
// Instead we subclass and override GetXamlType for the UiPath namespace,
// resolving types by direct Assembly.GetType() name lookup which never
// triggers assembly-level attribute scanning.
var schemaContext = new UiPathXamlSchemaContext(uiPathAsm);

// ── Per-file report ───────────────────────────────────────────────────────────
// Tee output to a file next to this script so it can be inspected after the run.
var teeFile = Path.Combine(Path.GetDirectoryName(projectJsonPath)!, "..", "workflow_tree_output.txt");
teeFile = Path.GetFullPath(teeFile);
using var tee = new StreamWriter(teeFile, append: false, System.Text.Encoding.UTF8);
var originalOut = Console.Out;
Console.SetOut(new TeeWriter(originalOut, tee));

Console.WriteLine($"Output also written to: {teeFile}");
Console.WriteLine("── Per-file activity tree ───────────────────────────────────────");

foreach (var xamlPath in implFiles)
{
    var relPath = Path.GetRelativePath(projectRoot, xamlPath);
    Console.WriteLine();
    Console.WriteLine($"  {relPath}");
    Console.WriteLine($"  {new string('─', relPath.Length)}");

    Activity root;
    try
    {
        using var stream = File.OpenRead(xamlPath);
        using var reader = new XamlXmlReader(stream, schemaContext);
        root = ActivityXamlServices.Load(reader,
            new ActivityXamlServicesSettings { CompileExpressions = false });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  LOAD ERROR: {ex.Message}");
        continue;
    }

    var annotations    = ExtractAnnotations(xamlPath);
    var allExpressions = new List<string>();
    int idCounter      = 0;
    var wfNode         = Build(root, annotations, ref idCounter, allExpressions);

    if (wfNode is not null) Render(wfNode, 2);

    var freq = new Dictionary<string, int>();
    if (wfNode is not null) CollectStats(wfNode, freq);

    Console.WriteLine();
    Console.WriteLine("  activity counts:");
    foreach (var (type, count) in freq.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"    {count,4}  {type}");
    // annotations.Count = distinct DisplayNames with annotations in raw XAML (source-of-truth).
    // CollectStats-based counting over-reports when multiple nodes share a DisplayName.
    Console.WriteLine($"  annotations : {annotations.Count}");
    Console.WriteLine($"  expressions : {allExpressions.Distinct().Count()} distinct ({allExpressions.Count} total)");
}

return 0;

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

// ── TeeWriter — writes to two TextWriters simultaneously ─────────────────────
class TeeWriter : TextWriter
{
    private readonly TextWriter _a, _b;
    public TeeWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }
    public override System.Text.Encoding Encoding => _a.Encoding;
    public override void Write(char value) { _a.Write(value); _b.Write(value); }
    public override void WriteLine(string? value) { _a.WriteLine(value); _b.WriteLine(value); }
    protected override void Dispose(bool disposing) { if (disposing) { _a.Flush(); _b.Flush(); } base.Dispose(disposing); }
}

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
    };

    public static bool IsScaffolding(string typeName) => Scaffolding.Contains(typeName);
}

// ── UiPath-aware XamlSchemaContext ────────────────────────────────────────────
// Passes only System.Activities to the base constructor (safe to attribute-scan).
// Resolves the UiPath XAML namespace via direct Assembly.GetType() — no attribute
// scan on UiPath.System.Activities, so UiPath.Activities.Contracts is never needed.
class UiPathXamlSchemaContext : XamlSchemaContext
{
    private const string UiPathNs = "http://schemas.uipath.com/workflow/activities";
    private static readonly string[] UiPathClrNamespaces =
        new[] { "UiPath.Core.Activities", "UiPath.Core" };

    private readonly Assembly _uiPathAsm;

    public UiPathXamlSchemaContext(Assembly uiPathAsm)
        // Include UiPath.Workflow.dll — it defines XmlnsDefinition entries for
        // Microsoft.VisualBasic.Activities (VisualBasic.Settings) and other
        // CoreWF namespaces.  It has NO reference to UiPath.Activities.Contracts,
        // so scanning its attributes is safe.
        : base(new[]
        {
            typeof(Activity).Assembly,                       // System.Activities
            Assembly.Load("UiPath.Workflow"),                // VisualBasic.Settings etc.
            typeof(Dictionary<,>).Assembly,                  // System.Private.CoreLib — scg: namespace
        })
    {
        _uiPathAsm = uiPathAsm;
    }

    protected override XamlType? GetXamlType(
        string xamlNamespace, string name, params XamlType[] typeArguments)
    {
        var baseType = base.GetXamlType(xamlNamespace, name, typeArguments);
        if (baseType is not null && !baseType.IsUnknown) return baseType;

        if (xamlNamespace == UiPathNs)
        {
            foreach (var ns in UiPathClrNamespaces)
            {
                var clrType = _uiPathAsm.GetType($"{ns}.{name}");
                if (clrType is not null)
                    return GetXamlType(clrType);
            }
        }

        return baseType;
    }
}
