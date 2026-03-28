---
title: Pattern — Bitmask Lookup
slug: pattern-bitmask-lookup
---

# Pattern: Bitmask Lookup

## Summary

Each named boolean rule is assigned a power-of-2 weight. The weights of all firing rules are summed into a single integer. That integer is used as a key into a lookup table that maps specific combinations to outcomes.

## Mechanics

```
Weights:
  b_Rule_A  → 1   (2^0)
  b_Rule_B  → 2   (2^1)
  b_Rule_C  → 4   (2^2)
  b_Rule_D  → 8   (2^3)
  ...

Combined = If(b_Rule_A, 1, 0) +
           If(b_Rule_B, 2, 0) +
           If(b_Rule_C, 4, 0) +
           If(b_Rule_D, 8, 0)

out_DecisionValue = lookupTable(Combined)
```

The lookup table maps integers to outcomes. With N rules the table has 2^N possible keys. Only keys that represent valid or expected combinations need entries; a default handles all others.

Example lookup (Dictionary or Select Case):

```
0  → "Negative"
1  → "Override"
2  → "Approved"
3  → "Approved"      ← Rule_A + Rule_B fired together
4  → "Escalate"
...
```

## Characteristics

| Property | Notes |
|----------|-------|
| Activities used | `MultipleAssign`, `Assign` |
| Combination encoding | Each unique combination of fired rules has a unique integer key |
| Outcome granularity | Supports as many distinct outcomes as there are table entries |
| Explicitness | Every combination that matters is listed; unlisted combinations get the default |
| Debuggability | Medium — the combined integer is inspectable; table lookup is a single step |

## Fits Well When

- There are **3 or more distinct outcomes** and specific combinations of rules must map to different ones.
- The same set of rules can fire in multiple combinations and each combination has a business-defined meaning.
- A sparse lookup (only a few meaningful combinations out of 2^N) makes the table manageable.

## Watch Out For

- 2^N entries grow fast: 6 rules → 64 keys, 10 rules → 1024 keys. Keep the rule set small or use a sparse dictionary.
- Impossible combinations (two mutually exclusive rules both firing) will produce a key that must either be handled or guarded against.
- Weights must be powers of 2 exactly; any other assignment breaks the one-to-one mapping between key and combination.
