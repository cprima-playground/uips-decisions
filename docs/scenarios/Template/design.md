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

## Decision: ExampleDecision

<!-- For a single decision: one IF/THEN/ELSE block (as below).
     For a pipeline: one H3 per sub-decision, each with its own block. -->

```
IF Category = (Type_A OR Type_B)
AND (
    (Assessment = Approved)
    OR (Assessment = Pending AND Flag_X = true)
    OR (Override = true)        // override: sufficient alone
)
THEN ExampleDecision = Positive
ELSE ExampleDecision = Negative
```

<!-- AND/OR note: if the original requirement uses "AND/OR" between two conditions,
     preserve the ambiguity here as a comment and open a question in roadmap.md:

     AND Attribute_B = Low
     AND/OR Attribute_C = Low   // ← ambiguous: OR (at least one) or AND (both)?
-->
