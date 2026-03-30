---
title: Tutor Guide — Didactical Guidelines
slug: tutor-guide
---

# Tutor Guide

Didactical guidelines for instructors and mentors using this repository as a teaching resource.

---

## What This Repository Teaches

The repository demonstrates that the **same business decision logic** can be implemented in multiple structurally different ways inside UiPath Studio. Each implementation is correct; each makes different trade-offs.

The two scenarios progress in complexity:

| Scenario | Archetype | Levels | Shape | Patterns shown |
|----------|-----------|--------|-------|----------------|
| `EligibilityDecision` | Eligibility / Gate | 101 – 301 | Single hierarchical boolean decision | If/Else, Expression, Decision Table, Rule-Based, State Machine |
| `RoutingPipeline` | Routing / Triage | 201 | Seven sub-decisions in sequence, one routing output | Pipeline, Expression, Bitmask Lookup |

---

## Curriculum Levels

Three levels organize the scenarios and patterns by structural complexity:

| Level | Label | Teaching goal |
|-------|-------|---------------|
| 101 | Foundations | Learn to locate the decisive logic and compare simple encodings |
| 201 | Composed Decisions | Learn to separate business structure from control-flow noise |
| 301 | Stateful and Governed | Learn when explicit state or stronger governance beats local simplicity |

`EligibilityDecision` spans all three levels depending on which patterns are shown.
`RoutingPipeline` is a 201 scenario; introduce it only after learners are comfortable with single-step comparisons.

The controlled vocabulary for levels, archetypes, and patterns is in `docs/taxonomy.yml`.

---

## Learning Progression

### Stage 1 — Read before implement

Direct learners to the scenario documentation before opening Studio:

1. `docs/scenarios/<Scenario>/brief.md` — what problem is being solved
2. `docs/scenarios/<Scenario>/design.md` — the decision logic in neutral, formal language
3. `docs/scenarios/<Scenario>/outcome.md` — what a correct implementation must produce

The design document is the contract. Implementations are evaluated against it, not against each other.

### Stage 2 — Navigate the canvas to the decisive activity

Every workflow has exactly one activity where the consequential decision is encoded — the one that, if changed, would change outputs. All other activities support it.

Teaching prompt: *"Find the activity that makes the decision. Everything else is plumbing."*

Locating that activity forces learners to read the workflow top-down rather than scanning activities at random.

### Stage 3 — Read the docked annotations

Sequence-level annotations are always visible (docked). They state:
- Which pattern is in use
- What inputs arrive and what output is produced
- Any side effects

Teach learners to read the Sequence annotation before reading the activities inside it. This top-down reading habit transfers to production code they did not write.

### Stage 4 — Compare implementations side by side

Open two implementations of the same scenario in Studio side by side. Ask:

- Where is the needle in each?
- What would change if a new rule is added?
- Which one would you prefer to debug? Why?
- Which one would a non-developer maintain?

The comparison is the lesson. There is no single correct answer.

---

## Annotation System

Annotations follow a two-axis scheme. Understanding both axes is necessary to use them effectively as a tutor.

### Axis 1 — Structure

Describes *what* the activity or workflow does, without explaining the business rule.

> "Sub-decision 3 of 7 — Pattern: Expression — Input: in_VatId — Output: out_VatClassification"

Use these annotations to orient learners who are spatially lost on the canvas.

### Axis 2 — Logic

Explains *why* the activity does what it does: the rule, the expression, the sentinel value, or the pattern rationale.

> "Expression: If(in_VatId.StartsWith("DOM-"), "NATIONAL", "INTERNATIONAL")"

Use these annotations to direct attention to the decision-encoding activity and explain the business reasoning behind it.

If a learner cannot identify which activity encodes the decision unaided, that is a signal they have not yet understood the workflow's logic.

### Annotation tags

Logic-axis annotations use bracketed prefixes to identify the role of the content:

| Tag | Meaning |
|-----|---------|
| `[RULE]` | States a business condition or decision rule |
| `[SENTINEL]` | Explains a special-case value (empty string as "not yet assessed", placeholder outputs) |
| `[OPEN]` | Marks an unresolved design ambiguity |
| `[SIDE EFFECT]` | Describes a non-decisional consequence (notification, log, fire-and-continue) |

Tags are grep-able and give learners a vocabulary to describe what they see before they can explain why.

---

## Open Questions as Discussion Points

Several test rows carry `Expected = "?"` and some annotation texts contain "Open question #1". These mark **unresolved design ambiguities** deliberately preserved in the codebase.

Example:

> "Open question #1: is this OR or AND? Currently implemented as OR."

Teaching use:
- Ask learners to find all open questions across the scenarios.
- Ask which test rows are marked `?` and why they cannot be asserted.
- Ask what information they would need from the business to resolve each one.
- Ask how the code would change once resolved.

This teaches the discipline of distinguishing *what is known* from *what is assumed*.

---

## Test Data and Test Cases

### E2E.xlsx

`project/Data/TestData/E2E.xlsx` contains one sheet per scenario. Column names match argument names exactly. Boolean inputs are stored as strings (`"True"` / `"False"`) because Excel is the source.

Teach learners to read the test matrix as the *executable specification* of the decision logic. Every row is a claim: "given these inputs, the correct output is this."

### TestCase\_\*.xaml

Test cases follow **Given / When / Then**:

- **Given** — instantiate a `QueueItem` with the test row values; cast strings to their correct types
- **When** — invoke the decision workflow
- **Then** — assert the output against `Expected`; rows with `Expected = "?"` are skipped

The test case is responsible for type casting. The decision workflow receives already-typed inputs. This separation is intentional and worth pointing out: the workflow's contract is typed; the test harness adapts the string-based data source.

### Workflow\_\*.xaml

The `Workflow_` wrapper exercises the full path including `InitAllSettings`. It does not assert; it just runs. Use it when demonstrating execution in Robot or when the assertion would fail due to unresolved open questions.

---

## Pattern Documents

`docs/patterns/` contains one file per implementation pattern:

| Pattern | Level | Core mechanism |
|---------|-------|----------------|
| If / Else | 101 | Nested `If` activities, one predicate per node |
| Expression | 101 | Single compound VB.NET expression in `If.Condition` |
| Decision Table | 101 – 301 | Inline `List(Of String())` with wildcard cells, LINQ `.Any()` |
| Rule-Based | 201 – 301 | Named Boolean variables per rule, combined with `OrElse` |
| State Machine | 301 | WF4 `StateMachine` with explicit state graph |
| Bitmask Lookup | 201 | Booleans × powers-of-2 → integer index → lookup table |
| Pipeline | 201 | Sequential sub-decisions, intermediate results as local variables |

Each document covers: mechanics, characteristics, when to use it, and what to watch out for. Assign the relevant pattern document before showing that implementation in Studio.

---

## Common Tutor Mistakes to Avoid

**Showing IfElse first because it looks familiar.**
Start with the design document and the test matrix instead. Let the pattern choice emerge from the design constraints, not the other way around.

**Treating one implementation as "the answer".**
All implementations pass the same test matrix. The lesson is about trade-offs, not correctness.

**Skipping the open questions.**
The `?` rows and the unresolved annotations are not gaps — they are intentional teaching material about the gap between specification and implementation.

**Explaining annotations rather than asking learners to read them.**
Annotations are written to be self-sufficient. Ask learners to read them aloud and explain what they mean. Intervene only when the annotation itself is insufficient.

---

## Recommended Session Structure

### Single-pattern session (≈ 90 min)

| Time | Activity |
|------|----------|
| 15 min | Read `brief.md` and `design.md` together |
| 10 min | Walk the test matrix in `E2E.xlsx`; identify `?` rows |
| 30 min | Open the implementation in Studio; find the needle; read annotations top-down |
| 20 min | Run `TestCase_*.xaml`; observe pass/skip behavior |
| 15 min | Discussion: what would change if rule X were modified? |

### Comparison session (≈ 90 min)

| Time | Activity |
|------|----------|
| 10 min | Recap the decision logic from `design.md` |
| 30 min | Open two implementations side by side; locate both needles |
| 20 min | Guided comparison using the Characteristics table from the pattern docs |
| 20 min | Learner presents: which implementation would they use in production and why? |
| 10 min | Debrief |

---

## Adding a New Scenario

If you extend this repository with a new scenario:

1. Fill `brief.md`, `design.md`, `outcome.md`, `roadmap.md` before writing any code.
2. Pseudonymize all domain names before storing anything in the repository.
3. Add at least one unresolved open question in `roadmap.md` and mark the corresponding test rows `Expected = "?"`.
4. Identify exactly one decisive activity per workflow and annotate it with the Logic-axis annotation.
5. Use the two-axis annotation scheme: structure annotations on the Sequence, logic annotations on the decisive activities.
