# SystemB — Decision Pipeline Requirements (Pseudonymized)

## Overview

SystemB is a **decision pipeline**: a sequence of independent sub-decisions whose outputs feed into a final routing decision.

---

## Sub-Decision 1 — DetermineChannel

```
channel = DetermineChannel(request)
  if request.hasRegistryId
     AND RegistryParticipantExists
     AND DocumentTypesAccepted
    return Channel_A
  else
    return Channel_B
```

---

## Sub-Decision 2 — IsPublicSectorRelevant

```
isPublicSector = IsPublicSectorRelevant(request)
  return request.registryId matches PublicSector pattern
```

---

## Sub-Decision 3 — DetermineVatClassification

```
vatClassification = DetermineVatClassification(vatId)
  if vatId starts with DomesticPrefix
    return NATIONAL
  else
    return INTERNATIONAL
```

---

## Sub-Decision 4 — DetermineCustomerStatus

```
customerStatus = DetermineCustomerStatus(customerNumber)
  if customerNumber exists in ExternalRegistry_X
    return EXISTING
  else
    return NEW
```

---

## Sub-Decision 5 — IsSegment_P_Customer

```
isSegment_P = IsSegment_P_Customer(customerNumber)
  if customerNumber in Segment_P
    → NotifySegment_P_Case()   // fire and continue (non-blocking)
    return true
```

---

## Sub-Decision 6 — AggregateErrors

```
(customerErrors, internalErrors) = AggregateErrors(validationResult, enrichmentResults)
  customerErrors = all errors attributable to customer input
  internalErrors = all errors attributable to internal/system issues
```

---

## Sub-Decision 7 — DetermineRouting

```
routingAction = DetermineRouting(customerErrors, internalErrors)
  if customerErrors and internalErrors
    return ???                    // ⚠ OPEN: precedence unresolved
  if customerErrors only
    return ROUTE_TO_CUSTOMER
  if internalErrors only
    return ROUTE_TO_INTERNAL
  return HAPPY_PATH
```

---

## Open Questions

| # | Question | Status |
|---|----------|--------|
| 1 | When both `customerErrors` and `internalErrors` are present, which routing takes precedence? | **Unresolved** |
