# Contributing

This repository is primarily a teaching resource. The main contributors are tutors
who extend or maintain it. Technical contributors (adding new scenarios or patterns)
should read this file first.

---

## Who this file is for

**Tutors** — instructors and mentors who use this repository in sessions and want to
extend or correct it.

**Technical contributors** — developers who want to add a new scenario, a new pattern
implementation, or a new pattern document.

---

## Source-of-truth files

Before changing anything, locate the relevant source of truth:

| What you want to change | Source of truth |
|------------------------|-----------------|
| What the repo teaches, which scenarios are active, roadmap | `docs/manifest.yml` |
| Controlled vocabulary (level IDs, archetype IDs, pattern IDs) | `docs/taxonomy.yml` |
| The business problem a scenario solves | `docs/scenarios/<Scenario>/brief.md` |
| The decision rules in neutral language | `docs/scenarios/<Scenario>/design.md` |
| What a correct implementation must produce | `docs/scenarios/<Scenario>/outcome.md` |
| Expected test outputs and open questions | `project/Data/TestData/E2E.xlsx` |
| A pattern implementation (the XAML) | `project/<Scenario>/<Pattern>.xaml` |
| Test variation data | `project/.variations/E2E_<Scenario>.json` |
| Coding and annotation conventions | `docs/conventions.md` |
| Teaching conventions, annotation tag system | `docs/tutor_guide.md` |
| Pattern mechanics (one file per pattern) | `docs/patterns/<Pattern>.md` |

`design.md` is the contract. Implementations are evaluated against it, not against
each other. Change `design.md` only if the business rules themselves change.

`E2E.xlsx` is the executable specification. Every row is a claim. Rows where
`Expected = "?"` mark deliberate open questions — do not resolve them without also
updating `design.md` and `roadmap.md`.

---

## Adding a new scenario

1. Confirm the archetype exists in `docs/taxonomy.yml`. If not, add it there first.
2. Assign a level from `docs/taxonomy.yml` levels.
3. Write `brief.md`, `design.md`, `outcome.md`, `roadmap.md` before opening Studio.
4. Pseudonymize all domain names — no real client, product, or system names in the
   repository.
5. Add at least one unresolved open question in `roadmap.md` and mark the
   corresponding test rows `Expected = "?"` in `E2E.xlsx`.
6. Register the scenario in `docs/manifest.yml` under `scenarios`.
7. Implement in `project/<ScenarioId>/`. Follow `docs/conventions.md`.
8. Add test variation data to `project/.variations/E2E_<ScenarioId>.json` and
   register it in `project/.variations/config.json`.
9. Register entry points in `project/project.json`.
10. Add `run-<scenario>` recipes to `justfile`.

## Adding a new pattern implementation to an existing scenario

1. Check that the pattern ID exists in `docs/taxonomy.yml`. Add it if missing.
2. Confirm a pattern document exists in `docs/patterns/`. Write it if missing
   (`has_pattern_doc: false` in `taxonomy.yml` is the signal).
3. Implement in `project/<ScenarioId>/<PatternId>.xaml`. Follow `docs/conventions.md`.
4. Add a `TestCase_<Scenario>_<Pattern>.xaml` and a `Workflow_<Scenario>_<Pattern>.xaml`
   under `project/Tests/<ScenarioId>/`.
5. Add test rows to `E2E.xlsx` and to the variation JSON.
6. Add the pattern to `implementations` in `docs/manifest.yml` for that scenario.

## Adding a new pattern document

1. Write `docs/patterns/<PatternId>.md`. Cover: mechanics, characteristics, when to
   use it, what to watch out for.
2. Set `has_pattern_doc: true` and `status: active` in `docs/taxonomy.yml` for that
   pattern.

---

## Annotation conventions (summary)

Full rules are in `docs/conventions.md`. The essentials:

- Every workflow has **exactly one decisive activity** — the one that, if changed,
  would change outputs. Annotate it with an Axis 2 (Logic) annotation.
- Logic annotations start with an annotation tag: `[RULE]`, `[SENTINEL]`, `[OPEN]`,
  or `[SIDE EFFECT]`. See `docs/tutor_guide.md` for the tag definitions.
- Sequence-level annotations are always docked and follow the Axis 1 (Structure)
  format: pattern name, inputs, outputs, side effects.
- Do not add tags to Structure annotations.

---

## Build and test

Pack the project and run a workflow:

```
just pack                              # validate XAML only
just run-eligibility IfElse            # pack + run EligibilityDecision/IfElse
just run-routing                       # pack + run RoutingPipeline
just run "Tests\<Scenario>\<File>.xaml"  # arbitrary entry point
```

Packages land in `out/` (gitignored). Version is auto-incremented.
Close UiPath Studio before packing, or the pack step will fail with a lock error.

---

## What not to do

- Do not edit `project/project.json` manually except to add entry points or
  `fileInfoCollection` entries. Studio manages the rest.
- Do not resolve open questions (`Expected = "?"` rows) without a corresponding
  update to `design.md`.
- Do not commit `.nupkg` files — they are gitignored under `out/`.
- Do not use real domain names, client names, or system names anywhere in the
  repository.
