---
title: Pattern — Rule-Based
slug: pattern-rulebased
---

# Pattern: Rule-Based

## Summary

Each business rule is evaluated independently into a named Boolean variable. A final step combines all rule flags with `OrElse` (or any other combinator) to produce the output value.

## Mechanics

```
MultipleAssign "Evaluate Named Rules":
  b_Rule_Override         = <condition>
  b_Rule_GroupA_Case1     = <condition>
  b_Rule_GroupA_Case2     = <condition>
  b_Rule_GroupB_Case1     = <condition>
  ...

Assign out_DecisionValue =
  If(b_Rule_Override OrElse
     b_Rule_GroupA_Case1 OrElse
     b_Rule_GroupA_Case2 OrElse
     b_Rule_GroupB_Case1,
     "Positive", "Negative")
```

Rule names encode intent (`b_Rule_RG1_Compliant`, `b_Rule_Override`), making each flag self-documenting.

## Characteristics

| Property | Notes |
|----------|-------|
| Activities used | `MultipleAssign` (×2), `Assign` |
| Expression complexity per rule | Low — each flag has one focused expression |
| Rule isolation | Each rule is independently readable and testable |
| Debuggability | High — each flag is a visible variable in the Locals panel |
| Combinator flexibility | `OrElse`, `AndAlso`, or weighted sum can all be substituted at the combine step |

## Fits Well When

- Rules have meaningful names that map to business terminology.
- Audit trails require tracing which specific rule fired.
- Rules are subject to independent change by different stakeholders.
- The combine step may evolve (e.g. majority vote, priority order).

## Watch Out For

- All rules are always evaluated regardless of short-circuit potential; avoid rules with side effects.
- Rule variable names must remain synchronized with the combine expression.
- Adding a new rule requires both a new flag variable and an update to the combine expression.
