---
title: "Template — Brief"
slug: template-brief
---

<!-- TEMPLATE INSTRUCTIONS
Purpose: Entry point for tutor and learner. Written BEFORE any implementation begins.
Audience: Tutor writing the scenario + learner reading it for the first time.
Tone: Direct. Tell the learner what they will do and why it matters.
When copying: replace all example content below with scenario-specific content.

This template is intentionally generic:
- It may describe a binary gate, a multi-outcome routing decision, or a
  multi-action determination.
- It may fit level 101, 201, or 301.
- It should describe the business decision first, not the implementation style.

Keep this file short. It is the front door, not the full specification.
-->

## Scenario Snapshot

- **Scenario ID:** `ExampleScenario`
- **Archetype:** `eligibility | routing | approval_matrix | required_actions | escalation`
- **Target level(s):** `101 | 201 | 301`
- **Primary output shape:** `binary | multi_outcome | multi_action`

## Learning Objectives

After completing this scenario, the learner will be able to:

- Explain the business decision in technology-agnostic terms
- Implement the scenario using at least two different approaches
- Compare which approach best fits the scenario's expected rate of change
- Read the test matrix as the executable specification of the scenario

## Target Audience

UiPath developer with basic Sequence and If activity knowledge.
Prior exposure to the repo's 101 material is recommended for 201 and 301 scenarios.

## Prerequisites

- UiPath Studio installed and licensed (Windows target framework)
- Familiarity with In/Out arguments in invoked workflows
- Understanding of Boolean expressions in VB.NET

## Context

Describe one realistic automation situation where this decision appears.
Keep it to one paragraph. Focus on the operational problem, not the chosen
implementation pattern.

Typical examples:

- A case is eligible or ineligible for further processing
- A request is routed to exactly one queue or owner
- A case requires several follow-up actions, not just one
- An approval path depends on amount, role, geography, or risk
- A timeout or failure triggers escalation, retry, or manual review

## Why This Scenario Matters

State why this scenario deserves a place in the repo:

- common in business process automation
- easy to test with a matrix
- suitable for multiple implementation styles
- exposes one important trade-off learners should discuss
