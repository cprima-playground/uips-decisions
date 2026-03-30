---
title: "Template — Outcome"
slug: template-outcome
---

<!-- TEMPLATE INSTRUCTIONS
Purpose: Defines what "done" looks like — for the design and for each implementation.
Two concerns: Acceptance Criteria (prose, verifier perspective) and Test Matrix (executable).

How to build the test matrix:
- Start with the happy path → Positive
- Add the clear negative (no conditions met) → Negative
  - Add each override/escape condition alone
  - Add boundary and ambiguous cases (empty values, AND/OR uncertainty)
  - Mark ambiguous rows "Unresolved" and log them in roadmap.md
Column names should mirror the business fields used in design.md.
-->

## Acceptance Criteria

- All rows in the agreed test matrix produce the expected output
- Empty/null/sentinel inputs are handled intentionally and consistently
- The decision logic is encapsulated and invocable as a workflow
- At least one test row isolates each override, precedence rule, or special case
- Unresolved rows are marked clearly and excluded from hard assertions

## Test Matrix

Use one normalized output column.

Examples:

- `Expected_DecisionValue`
- `Expected_RoutingDecision`
- `Expected_RequiredActions`

For multi-action outputs, decide the representation up front:

- ordered string: `NotifyOwner|RequestDocument`
- sorted string: `NotifyOwner|RequestDocument`
- or one boolean column per action

| # | Input_A | Input_B | Input_C | Expected_Output | Status | Notes |
|---|---------|---------|---------|-----------------|--------|-------|
| 1 | Value_1 | Value_2 | false | Result_A | Expected | happy path |
| 2 | Value_1 | Value_3 | false | Result_B | Expected | negative or alternate path |
| 3 | Value_1 | Value_3 | true | Result_A | Expected | override or escalation |
| 4 | *(empty)* | Value_2 | false | Result_B | Expected | empty sentinel handled |
| 5 | Value_2 | Value_4 | false | ? | Unresolved | depends on open question #1 |

## Verification Notes

- Map each test row back to one explicit rule, branch, or decision-table row.
- For 201 and 301 scenarios, include at least one row that proves precedence.
- For event race or escalation scenarios, include time- or event-driven edge cases
  in abstract form even if execution is later simulated rather than timed literally.
