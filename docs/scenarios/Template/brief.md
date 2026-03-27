---
title: "Template — Brief"
slug: template-brief
---

<!-- TEMPLATE INSTRUCTIONS
Purpose: Entry point for tutor and learner. Written BEFORE any implementation begins.
Audience: Tutor writing the scenario + learner reading it for the first time.
Tone: Direct. Tell the learner what they will do and why it matters.
When copying: replace all example content below with scenario-specific content.
Learning Objectives must be actionable — use verbs: implement, compare, explain, identify.
Keep Context to one paragraph: one real-world situation where this pattern appears.
-->

## Learning Objectives

After completing this scenario, the learner will be able to:

- Implement a named decision pattern using at least two different UiPath Studio approaches
- Compare the readability and maintainability of each implementation
- Identify which implementation approach fits a given volatility requirement
- Write a UiPath test case that verifies decision output against a test matrix

## Target Audience

UiPath developer with basic Sequence and If activity knowledge.
No prior experience with Flowcharts or decision tables required.

## Prerequisites

- UiPath Studio installed and licensed (Windows target framework)
- Familiarity with In/Out arguments in invoked workflows
- Understanding of Boolean expressions in VB.NET

## Context

In process automation, a case management system must route work items based on
a combination of category flags and assessment results. The routing logic is
stable for months at a time but changes when regulations are updated — making
maintainability a key concern alongside initial correctness.

This scenario presents a single binary output decision (Positive / Negative)
driven by a hierarchy of conditions. It is a realistic representative of the
class of compliance-gating decisions common in back-office automation.
