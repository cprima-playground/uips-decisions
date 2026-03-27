# EligibilityDecision — Decision Requirements (Pseudonymized)

## Decision: DecisionValue_EligibilityDecision

### Rule Group 1 — CaseCategory: Event_A or Event_B

```
IF CaseCategory = (Event_A OR Event_B)
AND (
    (ComplianceAssessment = Compliant)
    OR
    (
        ComplianceAssessment = Non_Compliant
        AND Attribute_A = Verification_not_available
        AND (Attribute_B = No_or_low_impact OR Attribute_C = No_or_low_impact)
    )
    OR
    (
        ComplianceAssessment = Non_Compliant
        AND Attribute_A = Special_case_without_certified_component
        AND (Attribute_B = No_or_low_impact OR Attribute_C = No_or_low_impact)
    )
    OR (ComplianceAssessment = empty)
    OR (SecondaryIndicator = true)
)
THEN DecisionValue_EligibilityDecision = Positive
ELSE DecisionValue_EligibilityDecision = Negative
```

### Rule Group 2 — CaseCategory: Event_C or Event_D

```
IF CaseCategory = (Event_C OR Event_D)
AND (
    (Indicator_X = Y AND Detail_X = Noticeable_irregularity)
    OR (Indicator_X = Y AND Detail_X = empty)
    OR (Indicator_X = N AND ComplianceAssessment = Compliant)
    OR (
        Indicator_X = N
        AND ComplianceAssessment = Non_Compliant
        AND Attribute_A = Verification_not_available
        AND (Attribute_B = No_or_low_impact OR Attribute_C = No_or_low_impact)
    )
    OR (
        Indicator_X = N
        AND ComplianceAssessment = Non_Compliant
        AND Attribute_A = Special_case_without_certified_component
        AND (Attribute_B = No_or_low_impact OR Attribute_C = No_or_low_impact)
    )
    OR (Indicator_X = N AND ComplianceAssessment = empty)
    OR (SecondaryIndicator = true)
)
THEN DecisionValue_EligibilityDecision = Positive
ELSE DecisionValue_EligibilityDecision = Negative
```

---

## Open Questions

| # | Question | Status |
|---|----------|--------|
| 1 | `AND/OR` on Attribute_B / Attribute_C: does at least one need to match (OR), or must both match (AND)? | **Unresolved** |
