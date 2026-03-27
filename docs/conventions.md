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
- Named `<ScenarioName>_<Approach>.xaml`, e.g. `EligibilityDecision_IfElse.xaml`.
- Approach names: `IfElse`, `DecisionTable`, `RuleBased`, `StateMachine`, …

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
| `in_TransactionItem` | In | `DataRow` | One row from the in-memory queue |
| `out_DecisionValue` | Out | `String` | Decision result, e.g. `Positive` / `Negative` |

---

## TransactionItem: DataRow, not QueueItem

`UiPath.Core.QueueItem` cannot be instantiated in-memory — it is an Orchestrator-managed object.
For in-memory testing and local execution, `DataRow` is used as the TransactionItem equivalent.
This maps naturally to reading rows from Excel and is the REFramework's built-in non-queue mode.

---

## In-Memory Queue Helper

`Framework/GetInMemoryQueue.xaml` reads a sheet from `Data/TestData/E2E.xlsx` and returns a `DataTable`.
Test cases iterate the rows and invoke the decision workflow once per row.

| Argument | Direction | Type |
|----------|-----------|------|
| `in_Config` | In | `Dictionary(Of String, Object)` |
| `in_FilePath` | In | `String` |
| `in_SheetName` | In | `String` |
| `out_QueueDataTable` | Out | `DataTable` |

---

## Expression Language

Visual Basic .NET (`VisualBasic`), as set in `project.json`.

---

## Test Data

- Test data is loaded manually into Studio from `Data/TestData/E2E.xlsx`.
- Each scenario has its own sheet, named after the scenario.
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
