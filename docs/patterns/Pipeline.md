---
title: Pattern — Pipeline
slug: pattern-pipeline
---

# Pattern: Pipeline

## Summary

A sequence of independent sub-decisions executed in order. Each step produces an intermediate result that may be consumed by subsequent steps. The final step produces the overall output.

## Mechanics

```
Sequence
├── Step 1: SubDecision_A  → out: result_A
├── Step 2: SubDecision_B  → out: result_B
├── Step 3: SubDecision_C  → in: result_A, result_B  → out: result_C
│           ...
└── Step N: FinalDecision  → in: result_X, result_Y  → out: FinalOutput
```

Each step is implemented as an `InvokeWorkflowFile` calling a dedicated sub-decision workflow. Intermediate results are held in local variables and passed as arguments to the steps that need them. Steps that do not depend on each other are logically independent even if executed sequentially.

## Characteristics

| Property | Notes |
|----------|-------|
| Activities used | `Sequence`, `InvokeWorkflowFile`, `Assign` |
| Decomposition | Each sub-decision is a separate, testable workflow |
| Data flow | Explicit — intermediate results are named variables |
| Side effects | Individual steps may trigger notifications or external calls without blocking the pipeline |
| Testability | Each sub-decision workflow can be tested in isolation; the pipeline itself is tested end-to-end |

## Fits Well When

- The overall decision is composed of several logically independent sub-decisions.
- Sub-decisions have different input sources (some from the transaction item, some from external lookups).
- Individual sub-decisions are likely to change independently over time.
- Sub-decisions need to be reused in other pipelines or contexts.

## Watch Out For

- Ordering matters when a later step depends on the output of an earlier one; make dependencies explicit in variable naming.
- Steps with external lookups or side effects break pure functional reasoning; document these clearly.
- The pipeline as a whole does not short-circuit; all steps execute regardless of intermediate results unless explicit early-exit logic is added.
