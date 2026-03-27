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
Column names must match field names used in design.md.
-->

## Acceptance Criteria

- All input combinations in the test matrix produce the expected output
- The implementation handles empty/null inputs without throwing an exception
- The decision logic is encapsulated and invocable with In-arguments only
- At least one UiPath test case covers the override condition independently

## Test Matrix

| # | Category | Assessment | Flag_X | Override | Expected | Status |
|---|----------|------------|--------|----------|----------|--------|
| 1 | Type_A | Approved | — | false | Positive | Expected |
| 2 | Type_A | Pending | true | false | Positive | Expected |
| 3 | Type_A | Pending | false | false | Negative | Expected |
| 4 | Type_A | — | — | true | Positive | Expected |
| 5 | Type_B | Approved | — | false | Positive | Expected |
| 6 | Type_C | Approved | — | false | Negative | Expected |
| 7 | Type_A | *(empty)* | — | false | Negative | Unresolved |
| 8 | *(empty)* | Approved | — | false | Negative | Expected |
