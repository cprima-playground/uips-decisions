---
title: Pattern — Expression
slug: pattern-expression
---

# Pattern: Expression

## Summary

The entire decision is encoded as a single compound VB.NET boolean expression inside one `Assign` activity. No branching activities are used.

## Mechanics

```
Assign out_DecisionValue =
  If(
    <condition A> OrElse
    <condition B> OrElse
    (<condition C> AndAlso <condition D>),
    "Positive",
    "Negative"
  )
```

All inputs are extracted first (via `MultipleAssign`), then the single expression is evaluated. The result is written directly to the output argument.

## Characteristics

| Property | Notes |
|----------|-------|
| Activities used | `Assign`, `MultipleAssign` |
| Expression complexity | High — entire logic in one expression |
| Visual footprint | Minimal — two activities |
| Debuggability | Low — expression evaluates atomically; no intermediate state visible |
| Mutability | Editing requires understanding the full compound expression |

## Fits Well When

- Logic is a flat union of conditions (`OR` of `AND` clauses).
- Compact representation is preferred over visual structure.
- The expression can be unit-tested externally (e.g. in a spreadsheet or formula tool).

## Watch Out For

- Long expressions become unreadable in UiPath's expression editor.
- VB.NET expressions in UiPath must be single-line; line breaks are not allowed.
- No intermediate variable values are available during debugging.
