---
title: Pattern — If/Else
slug: pattern-ifelse
---

# Pattern: If/Else

## Summary

Nested `If` activities that structurally mirror the decision tree. Each branch tests one condition; the matched branch assigns the output value.

## Mechanics

```
If <condition A>
├── Then → output = "X"
└── Else
    └── If <condition B>
        ├── Then → output = "Y"
        └── Else → output = "Z"
```

Conditions are written as UiPath/VB.NET expressions directly in the `If.Condition` property. Each `Assign` at a leaf node sets the output argument.

## Characteristics

| Property | Notes |
|----------|-------|
| Activities used | `If`, `Assign`, `MultipleAssign` |
| Expression complexity per node | Low — one predicate per `If` |
| Visual nesting | Grows with rule depth |
| Debuggability | High — each branch is a discrete, steppable node |
| Mutability | Each new condition adds a nesting level |

## Fits Well When

- Rules form a tree with mutually exclusive branches.
- Studio's visual debugger will be used to trace execution.
- Maintainers are not fluent in compound VB.NET expressions.

## Watch Out For

- Deep nesting past 3–4 levels makes the canvas hard to navigate.
- Conditions shared across multiple branches (e.g. a global override) must sit at the root or be duplicated.
