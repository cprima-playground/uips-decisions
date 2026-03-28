---
title: Pattern — State Machine
slug: pattern-statemachine
---

# Pattern: State Machine

## Summary

A WF4 `StateMachine` models the decision as a graph of states and transitions. Each state represents a decision stage (extract, route, evaluate, conclude). Transitions carry conditions that route execution to the next state.

## Mechanics

```
States:
  State_Extract    → extract inputs from SpecificContent
  State_Route      → dispatch to rule group or override
  State_RG1        → evaluate Rule Group 1 conditions
  State_RG2        → evaluate Rule Group 2 conditions
  State_Positive   → assign "Positive"
  State_Negative   → assign "Negative"
  State_Final      (IsFinal = True)

Transitions (trigger-less, condition-driven):
  State_Extract  → State_Route      [True]
  State_Route    → State_Positive   [v_SecondaryIndicator]
  State_Route    → State_RG1        [IsRG1]
  State_Route    → State_RG2        [IsRG2]
  State_Route    → State_Negative   [True]   ← catch-all
  State_RG1      → State_Positive   [<RG1 positive condition>]
  State_RG1      → State_Negative   [True]   ← catch-all
  ...
  State_Positive → State_Final      [True]
  State_Negative → State_Final      [True]
```

`Transition.Condition` is typed `Activity<Boolean>` in WF4. It must be written as text content in the XML element — **not** wrapped in `<InArgument>`:

```xml
<Transition.Condition>[v_SecondaryIndicator]</Transition.Condition>
```

Every state with multiple trigger-less transitions must have explicit conditions on all of them; WF4 does not allow more than one unconditional transition per state. Catch-all transitions use `[True]`.

## Characteristics

| Property | Notes |
|----------|-------|
| Activities used | `StateMachine`, `State`, `Transition`, `Assign`, `MultipleAssign` |
| Visual representation | Explicit graph — states and edges visible on canvas |
| Control flow transparency | Highest of all patterns |
| Side effects per state | Each state can carry entry/exit actions |
| Debuggability | Medium — state transitions are visible but conditions are in XML |

## Fits Well When

- The decision has distinct processing stages that benefit from explicit modelling.
- Non-developers need to understand the overall flow from a diagram.
- Future states (e.g. "Pending", "Escalated") may be added alongside binary outcomes.
- Entry or exit actions per stage (logging, data enrichment) are required.

## Watch Out For

- WF4 `Transition.Condition` is `Activity<Boolean>`, not `InArgument<Boolean>`; using the wrong wrapper causes a runtime type error.
- Every multi-transition state requires all conditions to be explicit; missing conditions cause a validation error ("Trigger-less transition must contain a condition").
- Variable scope: variables must be declared on the enclosing `Sequence`, not inside the `StateMachine` node, to be accessible across states.
- The visual layout in Studio does not persist deterministically; transitions may need manual repositioning after edits.
