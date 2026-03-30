#!/usr/bin/env dotnet-script
// read_xaml.cs  —  explore UiPath/CoreWF XAML files via project.json entrypoints
// Usage:
//   dotnet run --file scripts/read_xaml.cs                        (uses project/ next to scripts/)
//   dotnet run --file scripts/read_xaml.cs -- path/to/project.json

using System.Text.Json;
using System.Xml.Linq;

// ── Resolve project.json ──────────────────────────────────────────────────────
// Walk up from cwd looking for project/project.json (repo layout convention)
static string? FindProjectJson()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "project", "project.json");
        if (File.Exists(candidate)) return candidate;
        // also accept project.json directly in cwd (when script is run from project/)
        candidate = Path.Combine(dir.FullName, "project.json");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return null;
}

var projectJsonPath = args.Length > 0
    ? args[0]
    : FindProjectJson();

projectJsonPath = Path.GetFullPath(projectJsonPath);
if (projectJsonPath is null || !File.Exists(projectJsonPath))
{
    Console.Error.WriteLine($"project.json not found: {projectJsonPath}");
    return 1;
}

var projectRoot = Path.GetDirectoryName(projectJsonPath)!;
Console.WriteLine($"project.json : {projectJsonPath}");
Console.WriteLine($"project root : {projectRoot}");
Console.WriteLine();

// ── Parse project.json ────────────────────────────────────────────────────────
using var jsonDoc = JsonDocument.Parse(File.ReadAllText(projectJsonPath));
var root = jsonDoc.RootElement;

Console.WriteLine($"name             : {root.GetProperty("name").GetString()}");
Console.WriteLine($"expressionLang   : {root.GetProperty("expressionLanguage").GetString()}");
Console.WriteLine($"targetFramework  : {root.GetProperty("targetFramework").GetString()}");
Console.WriteLine();

// ── Collect XAML files ────────────────────────────────────────────────────────
// Strategy A: entryPoints listed in project.json
var entryPoints = root.GetProperty("entryPoints")
    .EnumerateArray()
    .Select(ep => ep.GetProperty("filePath").GetString()!)
    .Select(fp => Path.GetFullPath(Path.Combine(projectRoot, fp)))
    .Where(File.Exists)
    .ToList();

// Strategy B: all *.xaml under project root (broader, includes implementation files)
var allXaml = Directory
    .EnumerateFiles(projectRoot, "*.xaml", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}."))  // skip hidden dirs
    .Order()
    .ToList();

Console.WriteLine($"Entry points in project.json : {entryPoints.Count}");
Console.WriteLine($"All .xaml files under root   : {allXaml.Count}");
Console.WriteLine();

// ── Walk one file and report activity local names ─────────────────────────────
static IEnumerable<XElement> AllElements(XDocument doc) =>
    doc.Descendants();

static string LocalName(XElement el) =>
    el.Name.LocalName;

var SAP10   = "http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation";

Console.WriteLine("── Per-file activity inventory ──────────────────────────────────");
Console.WriteLine();

// Report implementation XAMLs only (skip Tests/, Framework/, README)
var implFiles = allXaml
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}")
             && !f.Contains($"{Path.DirectorySeparatorChar}Framework{Path.DirectorySeparatorChar}")
             && !Path.GetFileName(f).Equals("README.xaml", StringComparison.OrdinalIgnoreCase))
    .ToList();

foreach (var xamlPath in implFiles)
{
    XDocument doc;
    try { doc = XDocument.Load(xamlPath); }
    catch (Exception ex) { Console.WriteLine($"  PARSE ERROR {xamlPath}: {ex.Message}"); continue; }

    var relPath = Path.GetRelativePath(projectRoot, xamlPath);

    // Collect activity elements: exclude metadata wrappers
    // Elements that are metadata / argument wrappers, not activities
    var metadataLocalNames = new HashSet<string>
    {
        // Project / namespace metadata
        "Members", "Property", "AssemblyReference", "Collection",
        "TextExpression.NamespacesForImplementation",
        "TextExpression.ReferencesForImplementation",
        "VisualBasic.Settings",
        // View state / presentation
        "WorkflowViewStateService.ViewState", "Dictionary",
        "WorkflowViewState.IdRef",
        // Variable declarations
        "Sequence.Variables", "Variable",
        // Argument / value wrappers (hold expressions but are not activities)
        "InArgument", "OutArgument", "Literal",
        "Boolean", "String", "Null", "Reference",
        // Activity child slots (structural, not the activity itself)
        "InvokeWorkflowFile.Arguments",
        "Assign.To", "Assign.Value",
        "MultipleAssign.AssignOperations",
        "AssignOperation.To", "AssignOperation.Value",
        "If.Condition", "If.Then", "If.Else",
        "Transition.To", "Transition.Condition", "Transition.Action",
        "State.Entry", "State.Exit", "State.Transitions",
        "StateMachine.InitialState",
        "LogMessage.Message",
        "List",
    };

    var elements = AllElements(doc)
        .Where(el => !metadataLocalNames.Contains(LocalName(el)))
        .ToList();

    // Extract annotation texts
    XNamespace sap2010 = SAP10;
    var annotations = AllElements(doc)
        .Select(el => el.Attribute(sap2010 + "Annotation.AnnotationText")?.Value)
        .Where(a => a is not null)
        .ToList();

    // Extract expression strings  (content of InArgument / Condition / Value elements
    // that look like VB.NET: wrapped in [ ] by WF4 convention)
    var expressions = AllElements(doc)
        .Where(el => LocalName(el) is "InArgument" or "OutArgument"
                                   or "Literal" or "If.Condition")
        .Select(el => el.Value.Trim())
        .Where(v => v.StartsWith('[') && v.EndsWith(']') && v.Length > 2)
        .Select(v => v[1..^1])   // strip the [ ]
        .Distinct()
        .ToList();

    // Frequency table of local names
    var freq = elements
        .GroupBy(LocalName)
        .OrderByDescending(g => g.Count())
        .ToList();

    Console.WriteLine($"  {relPath}");
    Console.WriteLine($"    elements (excl. metadata) : {elements.Count}");
    Console.WriteLine($"    annotations               : {annotations.Count}");
    Console.WriteLine($"    expressions               : {expressions.Count}");
    Console.WriteLine($"    activity breakdown:");
    foreach (var g in freq.Take(12))
        Console.WriteLine($"      {g.Count(),4}  {g.Key}");
    Console.WriteLine();
}

return 0;
