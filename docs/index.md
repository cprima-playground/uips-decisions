---
title: UiPath Studio — Decision Implementations
slug: index
---

# UiPath Studio — Decision Implementations

Educational reference for implementing business decision logic in UiPath Studio.

The repository demonstrates that the **same business decision logic** can be
implemented in multiple structurally different ways. Each implementation is
correct; each makes different trade-offs. The unit of comparison is one
scenario × multiple patterns.

---

## Curriculum Levels

| Level | Label | Focus |
|-------|-------|-------|
| 101 | Foundations | Single-step decisions, easy to localize |
| 201 | Composed Decisions | Multi-step, table- or rule-oriented logic |
| 301 | Stateful and Governed | Explicit state, precedence, change management |

Start at **101** unless learners already understand basic pattern trade-offs.

---

## Active Scenarios

| Scenario | Archetype | Levels | Starter pattern | Patterns shown |
|----------|-----------|--------|-----------------|----------------|
| [EligibilityDecision](scenarios/EligibilityDecision/brief.md) | Eligibility / Gate | 101 – 301 | If / Else | If/Else, Expression, Decision Table, Rule-Based, State Machine |
| [RoutingPipeline](scenarios/RoutingPipeline/brief.md) | Routing / Triage | 201 | Pipeline | Pipeline, Expression, Bitmask Lookup |

---

## Roadmap Scenarios (proposed)

| Scenario | Archetype | Target level | Candidate patterns |
|----------|-----------|-------------|--------------------|
| ApprovalMatrix | Approval Matrix | 201 | Switch/Case, Decision Table, Rule-Based |
| RequiredActionsDecision | Required Actions | 201 | Decision Table, Rule-Based, Pipeline |
| EscalationDecision | Escalation | 301 | If/Else, Decision Table, State Machine |

---

## Where to Start

**Learner** — open `docs/scenarios/EligibilityDecision/brief.md`, read
`design.md`, then open `project/EligibilityDecision/IfElse.xaml` in Studio.
Find the activity that makes the decision — the one where the Axis 2 annotation explains the rule.

**Tutor** — read `docs/tutor_guide.md` first. It covers learning progression,
the annotation system, how to run the test matrix, and recommended session
structures.

---

## Key Documents

| Document | Purpose |
|----------|---------|
| [Tutor Guide](tutor_guide.md) | Didactical guidelines for instructors and mentors |
| [Conventions](conventions.md) | Coding and annotation conventions used across the repo |
| `docs/taxonomy.yml` | Controlled vocabulary: levels, archetypes, patterns |
| `docs/manifest.yml` | Curriculum index: scenarios, levels, roadmap |
| [Scenarios](scenarios/) | Brief, design, outcome, and roadmap per scenario |
| [Patterns](patterns/) | One document per implementation pattern |
