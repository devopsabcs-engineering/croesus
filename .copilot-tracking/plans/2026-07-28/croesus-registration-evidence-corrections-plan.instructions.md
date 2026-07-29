---
applyTo: '.copilot-tracking/changes/2026-07-28/croesus-registration-evidence-corrections-changes.md'
---
<!-- markdownlint-disable-file -->
# Implementation Plan: Croesus Registration Evidence and Test Corrections

## Overview

Correct the repository's documentation, tests, and evidence tooling so they state the evidence-qualified revised position — authorization code with PKCE is the leading hypothesis, the Desjardins registrations are `spa`-platform public clients (correct only for browser redemption), and `1008` is a device-binding status rather than proof of token replay — and add mock-app coverage that can prove or falsify the SPA-versus-web redemption behavior.

## Objectives

### User Requirements

* Correct repository assumptions where the revised research requires it — Source: research selected position and Repository Test and Documentation Backlog in .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md
* Ensure the mock app can support the revised theories with tests — Source: user request "ensure we can support our theories with tests in our mock app"
* Explain the `1008` replay-token message correctly in customer-facing material — Source: user request "don't forget we need to explain the 1008 replay token message evidence"
* Verify and reflect whether the Desjardins app registrations are correct for the intended flow — Source: user request "verify that the desjardins app registrations are correct or not for the intended flow"

### Derived Objectives

* Remove categorical replay and OBO-required claims from README and docs — Derived from: research corrected-assumptions table and the confirmed `AADSTS9002327` / native-app-only Token Protection findings
* Add static registration-shape assertions as regression guards — Derived from: research mock-app test backlog items 11-12 and the need to encode "public SPA client, OBO structurally impossible" in CI
* Provide optional, gated live redemption tests that demonstrate `spa` versus `web` token-endpoint behavior — Derived from: research mock-app test backlog item 13, marked opt-in because it needs real Entra apps

## Context Summary

### Project Files

* README.md - Carries the categorical "token replay (Token Protection 1008)" bottom-line and V1 findings plus OBO-required remediation framing that must become evidence-qualified
* docs/evidence-narrative.md - Asserts a certificate thumbprint in logs and an API-token-rejected-by-Graph claim proven only structurally; the Token Protection section is already well hedged and should be preserved
* docs/obo-demo-guide.md - Contains downstream `jti`/`iat`, decoded-audience, and certificate-thumbprint claims to reconcile with what the code actually emits
* docs/configuration-contract.md - Row 34 maps the API delegated scope to `AzureAd:Scopes`; the active enforcement path is `[RequiredScope("access_as_user")]`
* api/Controllers/MeController.cs - OBO reference route; does not decode token B and logs credential source/name only (not a thumbprint)
* api/Controllers/ReplayController.cs - Gated bearer-relay negative control with a status interpretation helper
* api/Tests/NegativeControlTests.cs - Audience-rejection tests; the string-comparison test name overstates what it proves
* api/Tests/ReplayEndpointTests.cs - Replay route coverage that needs status-neutral parameterization
* assets/dev-dev.txt, assets/dev-prod.txt, assets/prod-prod.txt - `spa`-platform public-client registrations to be asserted by new static tests
* spa/src/api.ts, spa/src/components/ReplayAttemptPanel.tsx - Replay-lab UI gated behind `VITE_ENABLE_REPLAY_DEMO`
* .github/workflows/deploy-croesus.yml - Enables the API replay route but does not rebuild the SPA with `VITE_ENABLE_REPLAY_DEMO=true`; evidence filtering may not match immutable app IDs
* scripts/evidence-kql.kusto - Best-effort correlation queries to filter by immutable app ID and label as best-effort

### References

* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md - Authoritative revised position, registration-correctness verdict, `1008` explanation, and 14-item backlog
* .copilot-tracking/research/subagents/2026-07-28/spa-platform-token-redemption-and-registration-correctness.md - `AADSTS9002327`, `AADSTS700025`, `AADSTS700084`, and native-app-only Token Protection citations plus seven falsifiable mock-app assertions
* .copilot-tracking/research/subagents/2026-07-06/token-protection-1008-mechanics.md - Authoritative `1008` mechanics and support matrix

### Standards References

* .github/instructions/hve-core/markdown.instructions.md - Markdown authoring rules for all edited docs
* .github/instructions/hve-core/writing-style.instructions.md - Voice and tone rules for markdown content

## Implementation Checklist

### [x] Implementation Phase 1: Documentation Corrections

<!-- parallelizable: true -->

* [x] Step 1.1: Make README replay, `1008`, and OBO-required claims evidence-qualified
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 12-33)
* [x] Step 1.2: Correct evidence-narrative and obo-demo-guide overclaims
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 35-58)
* [x] Step 1.3: Correct the configuration-contract enforcement-path row
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 60-73)
* [x] Step 1.4: Add the registration-correctness and `1008` explanation to customer-facing docs
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 75-92)

### [x] Implementation Phase 2: .NET Test Suite Corrections and New Coverage

<!-- parallelizable: true -->

* [x] Step 2.1: Rename the string-comparison negative-control test to state only what it proves
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 96-112)
* [x] Step 2.2: Add labeled audience acceptance and rejection middleware tests
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 114-130)
* [x] Step 2.3: Parameterize the replay endpoint tests for 200, 401, 403, and transport failure
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 132-149)
* [x] Step 2.4: Add a MeController success-path test with mocked token acquisition and Graph
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 151-168)
* [x] Step 2.5: Add static registration-shape and correctness assertions over the exports
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 170-190)
* [x] Step 2.6: Validate the .NET test project
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 192-204)

### [x] Implementation Phase 3: SPA Replay-Lab, Workflow, and Evidence Corrections

<!-- parallelizable: true -->

* [x] Step 3.1: Rebuild the replay-lab SPA with `VITE_ENABLE_REPLAY_DEMO=true`
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 208-224)
* [x] Step 3.2: Filter evidence by immutable app ID and label correlation joins best-effort
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 226-243)
* [x] Step 3.3: Validate the SPA build
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 245-255)

### [x] Implementation Phase 4: Optional Gated Live Redemption Tests

<!-- parallelizable: false -->

* [x] Step 4.1: Scaffold an opt-in integration harness for real Entra redemption
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 259-276)
* [x] Step 4.2: Assert `spa`-node no-Origin redemption fails with `AADSTS9002327` and Origin succeeds
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 278-294)
* [x] Step 4.3: Assert `web`-node confidential redemption succeeds and public-client-with-credential fails with `AADSTS700025`
  * Details: .copilot-tracking/details/2026-07-28/croesus-registration-evidence-corrections-details.md (Lines 296-312)

### [x] Implementation Phase 5: Validation

<!-- parallelizable: false -->

* [x] Step 5.1: Run full project validation
  * Execute markdownlint / mega-linter on changed docs, `dotnet test` on the API test project, and `npm run build` on the SPA
* [x] Step 5.2: Fix minor validation issues
  * Iterate on lint errors, build warnings, and test failures when corrections are straightforward
* [x] Step 5.3: Report blocking issues
  * Document issues requiring additional research or a live tenant, and provide next steps rather than large-scale inline fixes

## Planning Log

See .copilot-tracking/plans/logs/2026-07-28/croesus-registration-evidence-corrections-log.md for discrepancy tracking, implementation paths considered, and suggested follow-on work.

## Dependencies

* .NET 8 SDK (`dotnet test`) — not currently on PATH in this shell; the implementer must resolve the SDK path
* Node.js and npm for the SPA build (`npm run build`)
* markdownlint / mega-linter for documentation validation
* Optional Phase 4 only: a nonproduction Microsoft Entra tenant with a `spa`-platform app, a `web`-platform app with a certificate, and the ability to complete an interactive authorization-code grant

## Success Criteria

* README and docs no longer assert categorical token replay, mandatory OBO, downstream token-B claims the code does not emit, or a logged certificate thumbprint — Traces to: research corrected-assumptions table and backlog items 1-4
* Customer-facing docs state the registration-correctness verdict (`spa` public client, correct only for browser redemption) and the correct `1008` meaning — Traces to: user requests on registration correctness and `1008`, research Registration Correctness and Explaining the 1008 sections
* The renamed and new .NET tests pass and assert only what they prove, including static registration-shape checks over the three exports — Traces to: backlog items 5-8, 11-12
* The replay-lab SPA is built with the demo flag and evidence is filtered by immutable app ID with best-effort labels — Traces to: backlog items 9-10
* Optional gated live tests exist and are documented as opt-in, demonstrating `spa` versus `web` redemption behavior — Traces to: backlog item 13
