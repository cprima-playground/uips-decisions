---
title: "Template — Slides"
slug: template-slides
template: slides.tmpl
---

# Template Scenario
## Scenario Authoring Template

---

## Each scenario starts as documentation

| File | DevOps phase | Purpose |
|------|-------------|---------|
| `brief.md` | Plan | Scenario entry point, context, learning objectives |
| `design.md` | Plan | Technology-agnostic decision logic |
| `outcome.md` | Plan -> Test | Acceptance criteria, test matrix |
| `roadmap.md` | Plan -> Code | Open questions, candidate patterns, implementation status |
| `slides.md` | Optional | Teaching deck for the scenario |

---

## Authoring flow

```
brief.md        ← start here
    ↓
design.md       ← formalize the decision
    ↓
outcome.md      ← define expected behavior
    ↓
roadmap.md      ← choose patterns and track open questions
    ↓
project/<ScenarioName>/   ← implement when stable
```

---

## Pseudonymization

All scenarios use neutral identifiers:

- Real system names → `SystemA`, `Channel_A`
- Real field names → `Attribute_A`, `Indicator_X`
- Real enum values → `Compliant`, `Non_Compliant`

The original source is **never** stored in this repo.

---

## Output shapes

This template is not limited to binary gates.

- Binary: `Allow | Deny`
- Multi-outcome: `QUEUE_A | QUEUE_B | OPEN`
- Multi-action: `NotifyOwner | RequestDocument | Escalate`

---

## Key takeaways

- Design before code
- Pseudonymize before storing
- Test matrix surfaces ambiguities
- One scenario can support several implementation styles
