---
title: "Template — Slides"
slug: template-slides
template: slides.tmpl
---

# Template Scenario
## How to use this repo

---

## Each scenario has four files

| File | DevOps phase | Purpose |
|------|-------------|---------|
| `brief.md` | Plan | Learning objectives, context |
| `design.md` | Plan | Formal decision logic |
| `outcome.md` | Plan → Test | Acceptance criteria, test matrix |
| `roadmap.md` | Plan | Open questions, implementation status |

---

## The decision pipeline

```
brief.md        ← start here
    ↓
design.md       ← understand the logic
    ↓
project/Decisions/   ← implement
    ↓
outcome.md      ← verify
```

---

## Pseudonymization

All scenarios use neutral identifiers:

- Real system names → `SystemA`, `Channel_A`
- Real field names → `Attribute_A`, `Indicator_X`
- Real enum values → `Compliant`, `Non_Compliant`

The original source is **never** stored in this repo.

---

## Open questions belong in roadmap.md

```
AND Attribute_B = Low
AND/OR Attribute_C = Low   ← ambiguous
```

Log it. Send the test matrix to the requestor. Wait for the answer.

---

## Key takeaways

- Design before code
- Pseudonymize before storing
- Test matrix surfaces ambiguities
- Multiple implementations, one test matrix
