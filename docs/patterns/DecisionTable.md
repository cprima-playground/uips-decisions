---
title: Pattern — Decision Table
slug: pattern-decisiontable
---

# Pattern: Decision Table

## Summary

Rules are encoded as rows in an inline table. Each row represents one positive rule as an array of column values. A wildcard sentinel (`"*"`) marks "don't care" columns. A LINQ `.Any()` scan finds the first matching row; no match yields the default outcome.

## Mechanics

```
Table (List of String arrays):
  Row 0: ["*",  "RG1", "Compliant", "*", "*"]   → Positive
  Row 1: ["*",  "RG1", "",          "*", "*"]   → Positive
  Row 2: ["True","*",  "*",          "*", "*"]   → Positive
  ...

Match = table.Any(Function(r)
    (r(0)="*" OrElse r(0)=v_Col0) AndAlso
    (r(1)="*" OrElse r(1)=v_Col1) AndAlso
    ...
)

out_DecisionValue = If(Match, "Positive", "Negative")
```

The table is constructed and matched inline within a single `Assign` expression using `New List(Of String()) From { ... }.Any(...)`.

## Characteristics

| Property | Notes |
|----------|-------|
| Activities used | `Assign` (×2), `MultipleAssign` |
| Expression complexity | Medium — table literal + LINQ predicate |
| Rule representation | Declarative rows; each row is independently readable |
| Extensibility | Add a row; no code restructuring needed |
| Debuggability | Low — entire match is one expression; no per-row visibility |

## Fits Well When

- Rules can be expressed as a cross-product of column values.
- The number of rules is expected to grow over time.
- A business analyst or auditor needs to review the rule set without reading code.

## Watch Out For

- `New List(Of String()) From { ... }` cannot be stored in a typed variable in UiPath XAML (`scg:List(x:String())` is not valid as a XAML type argument); the entire expression must be inline.
- All column values are strings; type-sensitive comparisons (Boolean, Integer) require normalization before the table lookup.
- Wildcard matching only handles equality; range or regex conditions need a different approach.
