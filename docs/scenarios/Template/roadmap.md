---
title: "Template — Roadmap"
slug: template-roadmap
---

<!-- TEMPLATE INSTRUCTIONS
Purpose: Living document tracking open questions and planned implementations.

Open Questions: log ambiguities as soon as found. Never delete resolved rows.
When resolved: update design.md or outcome.md and record the resolution here.

Status vocabulary (DevOps phases):
  planned — identified, not yet started
  code    — implementation in progress
  test    — implementation complete, test cases running
  done    — verified against outcome.md test matrix

Repo layout:
  project/<ScenarioName>/<Approach>.xaml
  project/Tests/<ScenarioName>/TestCase_<ScenarioName>_<Approach>.xaml
  project/Tests/<ScenarioName>/Workflow_<ScenarioName>_<Approach>.xaml
-->

## Scenario Metadata

- **Scenario ID:** `ExampleScenario`
- **Archetype:** `eligibility | routing | approval_matrix | required_actions | escalation`
- **Target level:** `101 | 201 | 301`
- **Manifest status:** `proposed | active`

## Open Questions

| # | Question | Raised | Resolved | Resolution |
|---|----------|--------|----------|------------|
| 1 | Is `Assessment = empty` treated as Negative or as a separate case? | 2026-03-27 | No | — |

## Planned Implementations

| Approach | Workflow File | Test Wrapper | Test Case | Status |
|----------|---------------|--------------|-----------|--------|
| IfElse | `project/ExampleScenario/IfElse.xaml` | `project/Tests/ExampleScenario/Workflow_ExampleScenario_IfElse.xaml` | `project/Tests/ExampleScenario/TestCase_ExampleScenario_IfElse.xaml` | planned |
| DecisionTable | `project/ExampleScenario/DecisionTable.xaml` | `project/Tests/ExampleScenario/Workflow_ExampleScenario_DecisionTable.xaml` | `project/Tests/ExampleScenario/TestCase_ExampleScenario_DecisionTable.xaml` | planned |
| RuleBased | `project/ExampleScenario/RuleBased.xaml` | `project/Tests/ExampleScenario/Workflow_ExampleScenario_RuleBased.xaml` | `project/Tests/ExampleScenario/TestCase_ExampleScenario_RuleBased.xaml` | planned |

## Candidate Patterns

List the implementation patterns worth comparing for this scenario.
Use IDs from `docs/taxonomy.yml`.

- `if_else`
- `decision_table`
- `rule_based`

## Promotion Criteria

State what must be true before the scenario moves from roadmap candidate to
active teaching scenario.

- `brief.md`, `design.md`, and `outcome.md` are scenario-specific and stable
- at least one implementation exists
- the test matrix covers the consequential rules
- open questions are explicit rather than buried in prose
