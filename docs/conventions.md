---
title: Code Conventions
slug: conventions
---

# Code Conventions

Design and implementation policies for UiPath Studio decision workflows in this repository.

---

## Project Structure

```
project/
├── <ScenarioName>/          one folder per scenario, decision workflows live here
├── Framework/               shared helper workflows (not scenario-specific)
├── Tests/
│   └── <ScenarioName>/      test cases for each scenario, created in Studio
└── Data/
    ├── Config.xlsx           runtime configuration (Settings, Constants, Assets sheets)
    └── TestData/
        └── E2E.xlsx          end-to-end test data, one sheet per scenario
```

---

## Workflow Files

- One implementation approach per `.xaml` file.
- Named `<Approach>.xaml` inside the scenario folder, e.g. `EligibilityDecision/IfElse.xaml`.
- Approach names: `IfElse`, `DecisionTable`, `RuleBased`, `StateMachine`, …
- Test cases named `TestCase_<ScenarioName>_<Approach>.xaml` inside `Tests/<ScenarioName>/`.

---

## Argument Conventions

| Prefix | Direction | Meaning |
|--------|-----------|---------|
| `in_`  | In        | Input argument |
| `out_` | Out       | Output argument |
| `io_`  | In/Out    | In/Out argument |

### Standard signature for decision workflows

| Argument | Direction | Type | Notes |
|----------|-----------|------|-------|
| `in_Config` | In | `Dictionary(Of String, Object)` | Standard REFramework config dictionary |
| `in_TransactionItem` | In | `UiPath.Core.QueueItem` | Transaction item with inputs in `SpecificContent` |
| `out_DecisionValue` | Out | `String` | Decision result, e.g. `Positive` / `Negative` |

---

## TransactionItem: QueueItem

`UiPath.Core.QueueItem` is used as `in_TransactionItem` in decision workflows.
Input fields are read from `in_TransactionItem.SpecificContent("in_FieldName")`.

For testing, the `QueueItem` is instantiated in the test case's `... Given` block:

```vb
New UiPath.Core.QueueItem With {
    .SpecificContent = New Dictionary(Of String, Object) From {
        {"in_FieldName", in_FieldName},
        ...
    }
}
```

No queue object or Orchestrator connection is needed.

---

## Expression Language

Visual Basic .NET (`VisualBasic`), as set in `project.json`.

---

## Test Cases

Test cases follow the **Given / When / Then** structure:

- `... Given` — initialise `Config` via `InitAllSettings`, instantiate `TransactionItem` as a `QueueItem`
- `... When` — invoke the decision workflow
- `... Then` — assert `out_DecisionValue` against `Expected_DecisionValue`

Variables declared at test case level: `Config As Dictionary(Of String, Object)`, `TransactionItem As QueueItem`.

Test case arguments mirror the E2E.xlsx column names exactly (e.g. `in_CaseCategory`, `Expected_DecisionValue`).
All input arguments are typed `String` (Excel source).

**The test case is responsible for casting to the correct type** before placing values into `SpecificContent`.
The decision workflow receives already-typed values and must not perform its own parsing.

Common casts in VB.NET:

| Target type | Expression |
|---|---|
| `Boolean` | `Boolean.Parse(in_SecondaryIndicator)` |
| `Integer` | `Integer.Parse(in_SomeNumber)` |
| `String` | no cast needed |
| empty sentinel | `If(String.IsNullOrEmpty(in_Field), Nothing, in_Field)` |

---

## Test Data

- Test data is loaded manually into Studio from `Data/TestData/E2E.xlsx`.
- Each scenario has its own sheet, named after the scenario.
- Input columns are prefixed `in_` to match `SpecificContent` key names.
- The `Expected_DecisionValue` column holds the expected output for assertion.
- Rows prefixed `[OPEN]` depend on unresolved open questions and must not be used for assertions until resolved.

---

## Config

- `Config.xlsx` has three sheets: `Settings`, `Constants`, `Assets`.
- Loaded at runtime by `Framework/InitAllSettings.xaml` into a `Dictionary(Of String, Object)`.
- Log level is an enum in UiPath and is **not** a configurable setting.
- `Config_Dev.xlsx` may override `Config.xlsx` values for local development.

---

## Naming: Scenarios

Folders and files use the scenario name directly, not numeric prefixes.
Current scenarios: `EligibilityDecision`, `RoutingPipeline`.
