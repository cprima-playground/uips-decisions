---
title: "Template — Design"
slug: template-design
---

<!-- TEMPLATE INSTRUCTIONS
Purpose: Captures the solution design — the formal logic to be implemented.
This is NOT the original business requirement. It is an interpretation/formalization,
already pseudonymized. The original source is never stored in this repo.

Pseudonymization rules:
- Replace real system/product names with neutral identifiers (SystemA, Channel_A)
- Replace real field names with generic ones (Attribute_A, Indicator_X)
- Replace real enum values with descriptive neutrals (Compliant, Non_Compliant)
- Never commit the mapping between real names and neutral identifiers

Ambiguity notation: when the original uses "AND/OR", preserve it as a comment
and log it as an open question in roadmap.md.
-->

## Scenario Contract

- **Scenario ID:** `ExampleScenario`
- **Primary archetype:** `eligibility | routing | approval_matrix | required_actions | escalation`
- **Output shape:** `binary | multi_outcome | multi_action`
- **Teaching level(s):** `101 | 201 | 301`

## Inputs

List the business inputs in neutral names. Example:

- `Category`
- `Assessment`
- `Amount`
- `Region`
- `Override`
- `HasTimeout`

## Outputs

State the output contract explicitly.

Examples:

- Binary: `DecisionValue = Allow | Deny`
- Multi-outcome: `RoutingDecision = QUEUE_A | QUEUE_B | MANUAL_REVIEW`
- Multi-action: `RequiredActions = { RequestDocument, NotifyOwner, Escalate }`

## Decision Logic

<!-- For a single decision: use one block.
     For a composed scenario: add one H3 per sub-decision or stage. -->

```
IF Category = (Type_A OR Type_B)
AND (
    (Assessment = Approved)
    OR (Assessment = Pending AND Flag_X = true)
    OR (Override = true)        // override: sufficient alone
)
THEN DecisionValue = Allow
ELSE DecisionValue = Deny
```

## Optional: Sub-Decisions

Use this section for pipelines or layered decisions.

### Sub-Decision 1 - ExampleStage

```
stageResult = ExampleStage(input)
  if Condition_A
    return Result_A
  else
    return Result_B
```

### Sub-Decision 2 - FinalDecision

```
finalResult = FinalDecision(stageResult, timeoutFlag, overrideFlag)
  ...
```

<!-- AND/OR note: if the original requirement uses "AND/OR" between two conditions,
     preserve the ambiguity here as a comment and open a question in roadmap.md:

     AND Attribute_B = Low
     AND/OR Attribute_C = Low   // ← ambiguous: OR (at least one) or AND (both)?
-->

## Notes

- Record sentinel values explicitly: empty string, null, `OPEN`, `UNKNOWN`, etc.
- If precedence matters, state it directly in the design instead of leaving it
  implicit in prose.
- If output is multi-action, define ordering and representation rules early.
