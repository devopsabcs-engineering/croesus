---
applyTo: '.copilot-tracking/changes/2026-06-30/croesus-bad-path-replay-demo-changes.md'
---
<!-- markdownlint-disable-file -->
# Implementation Plan: Croesus Bad-Path Replay Demo

## Overview

Add a safe, client-only bad-path replay demonstration that reuses the existing API-audienced token against Microsoft Graph, shows the expected 401 audience rejection, and documents Tier 2 backend replay as a deferred gated lab.

## Objectives

### User Requirements

* Reproduce the Croesus unbound token-replay anti-pattern in a way that is instructive next to the working OBO demo — Source: user request, README.md (Lines 8-17), .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md
* Follow the task-plan prompt and create implementation planning artifacts from the available research — Source: c:\Users\emknafo\.vscode\extensions\ise-hve-essentials.hve-core-3.2.2\.github\prompts\hve-core\task-plan.prompt.md

### Derived Objectives

* Implement Tier 1 before Tier 2 — Derived from: research and rubber-duck findings that Tier 1 provides safe teaching value with no tenant changes while Tier 2 introduces a deliberate token-forwarding vulnerability
* Avoid claiming the mock emits literal Token Protection 1008 — Derived from: docs/evidence-narrative.md boundary summarized in .copilot-tracking/research/subagents/2026-06-30/1008-reasoning-rubber-duck.md
* Keep raw tokens out of UI state, logs, telemetry, errors, and documentation — Derived from: bad-path implementation safety controls in .copilot-tracking/research/subagents/2026-06-30/bad-path-implementation-rubber-duck.md

## Context Summary

### Project Files

* spa/src/api.ts - Existing MSAL API-token fetch wrapper; add safe JWT claim decoding and `callGraphWithApiTokenWrongWay()`
* spa/src/App.tsx - Existing good-path button, state, and `ContrastPanel`; add bad-path button/state/rendering
* spa/src/components/EvidencePanel.tsx - Existing card/row evidence styling to mirror for replay attempt panel
* docs/obo-demo-guide.md - Existing negative-test section; update to mention interactive Tier 1 negative control
* README.md - Existing wrong-versus-right demo explanation; update to mention safe interactive negative control without overclaiming 1008

### References

* .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md - Primary research and selected staged approach
* .copilot-tracking/research/subagents/2026-06-30/1008-reasoning-rubber-duck.md - Claim boundaries for real 1008 vs mock replay-shape evidence
* .copilot-tracking/research/subagents/2026-06-30/bad-path-implementation-rubber-duck.md - Implementation pitfalls, safety controls, and recommended Tier 1 first sequence
* .copilot-tracking/research/subagents/2026-06-30/good-path-code-map.md - Current SPA/API insertion-point map

### Standards References

* c:\Users\emknafo\.vscode\extensions\ise-hve-essentials.hve-core-3.2.2\.github\instructions\hve-core\markdown.instructions.md — Markdown style rules for README/docs updates
* c:\Users\emknafo\.vscode\extensions\ise-hve-essentials.hve-core-3.2.2\.github\instructions\hve-core\writing-style.instructions.md — Writing style for documentation updates

## Implementation Checklist

### [x] Implementation Phase 1: Add Client-Side Replay Attempt

<!-- parallelizable: false -->

* [x] Step 1.1: Add safe JWT payload decoding in `spa/src/api.ts`
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 16-38)
* [x] Step 1.2: Add `callGraphWithApiTokenWrongWay()` in `spa/src/api.ts`
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 40-78)
* [x] Step 1.3: Add replay attempt result UI component
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 80-106)
* [x] Step 1.4: Wire bad-path button and state in `spa/src/App.tsx`
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 108-137)
* [x] Step 1.5: Validate SPA changes
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 139-153)

### [x] Implementation Phase 2: Update Documentation For Tier 1

<!-- parallelizable: true -->

* [x] Step 2.1: Update `docs/obo-demo-guide.md` negative-control section
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 157-181)
* [x] Step 2.2: Update README wrong-versus-right prose
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 183-207)
* [x] Step 2.3: Validate Markdown wording
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 209-224)

### [x] Implementation Phase 3: Add Focused Tests If Practical

<!-- parallelizable: false -->

* [x] Step 3.1: Add JWT decoder unit coverage only if SPA test infrastructure already exists
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 228-252)

### [ ] Implementation Phase 4: Final Validation

<!-- parallelizable: false -->

* [x] Step 4.1: Run full focused validation
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 256-271)
* [ ] Step 4.2: Exercise deployed or local browser flow
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 273-287)
* [x] Step 4.3: Record implementation evidence
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 289-303)

### [ ] Deferred Phase: Tier 2 Replay Lab

<!-- parallelizable: false -->

* [ ] Create separate plan before implementing backend replay endpoint, SPA Graph-token acquisition, consent changes, CORS POST, token redaction tests, and replay-lab deployment gates
  * Details: .copilot-tracking/details/2026-06-30/croesus-bad-path-replay-demo-details.md (Lines 305-323)

## Planning Log

See `.copilot-tracking/plans/logs/2026-06-30/croesus-bad-path-replay-demo-log.md` for discrepancy tracking, implementation paths considered, and suggested follow-on work.

## Dependencies

* Existing SPA build and Vite/TypeScript setup from `spa/package.json`
* Existing MSAL token acquisition via `spa/src/getApiToken.ts`
* Browser access to `https://graph.microsoft.com/v1.0/me`
* Existing OBO demo app registration and API scope configuration
* No new tenant permissions, API routes, CORS changes, or Graph consent for Tier 1

## Success Criteria

* The SPA shows a bad-path action that attempts to use token A against Microsoft Graph and renders the expected 401 response — Traces to: user requirement for an instructive replay contrast
* The existing OBO good path still renders leg 1 API audience and leg 2 Graph audience evidence — Traces to: current verified demo behavior
* The UI and docs describe Tier 1 as a safe negative-control replay rejection, not a literal Token Protection 1008 reproduction — Traces to: .copilot-tracking/research/subagents/2026-06-30/1008-reasoning-rubber-duck.md
* Raw tokens are never displayed, logged, stored in long-lived state, or written to tracking artifacts — Traces to: .copilot-tracking/research/subagents/2026-06-30/bad-path-implementation-rubber-duck.md
* Tier 2 backend replay remains deferred behind a separate plan and explicit replay-lab gates — Traces to: .copilot-tracking/plans/logs/2026-06-30/croesus-bad-path-replay-demo-log.md
