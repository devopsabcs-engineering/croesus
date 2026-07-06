---
applyTo: '.copilot-tracking/changes/2026-07-06/tier2-ca-1008-replay-changes.md'
---
<!-- markdownlint-disable-file -->
# Implementation Plan: Tier 2 Conditional Access 1008 Token-Replay Reproduction & Reversible Demo

## Overview

Add a gated, fully reversible Tier 2 to the Croesus OBO demo that reproduces the vendor's server-side token-replay shape (2a), reproduces the customer's real Token Protection "unbound / 1008" telemetry on a supported resource via a report-only Conditional Access policy (2b), presents wrong-vs-right side-by-side with the five-item OBO gap, and restores the demo tenant to its exact original state on teardown.

## Objectives

### User Requirements

* Implement Tier 2 tests reproducing the server-side token replay (SPA acquires a real Graph token, forwards it to an API endpoint, API replays it server-side). — Source: user request 2026-07-06
* Prove reproduction end-to-end for both customer (Desjardins) and vendor (Croesus). — Source: user request 2026-07-06
* Show WRONG flows (currently implemented/suspected) side-by-side with CORRECT flows (mock Croesus OBO). — Source: user request 2026-07-06
* Make explicit what is MISSING to move from wrong to recommended/best-practice. — Source: user request 2026-07-06
* Reproduce (or observe) the Conditional Access Token Protection 1008 "unbound" signal. — Source: user request 2026-07-06
* Provide a fully REVERSIBLE undo of the Conditional Access and other tenant changes to restore the original Contoso/Entra tenant state after the demo. — Source: user request 2026-07-06

### Derived Objectives

* Split Tier 2 into 2a (replay shape) and 2b (report-only Token Protection CA policy) because the platform does not emit 1008 for a browser-SPA → custom-API → Graph call. — Derived from: research Key Discovery 2 (concept-token-protection support matrix).
* Gate every new capability OFF by default (`Demo:EnableReplay`, `VITE_ENABLE_REPLAY_DEMO`) with the replay endpoint ABSENT when disabled. — Derived from: research Key Discovery 5 + safety (deliberate weakness).
* Reuse the `OboClaimLogger` redaction guard and claims-only pattern; never log/return raw tokens. — Derived from: research File Analysis (OboClaimLogger.cs) + repo convention.
* Cite Entra ID P1 (not P2) for Token Protection and use the beta Graph endpoint with `sessionControls.secureSignInSession`. — Derived from: research Key Discovery 1 (README correction).
* Document the 1008 boundary honestly (2a = shape, 2b = real telemetry on EXO/SPO/Teams). — Derived from: research Scope + evidence-narrative.

## Context Summary

### Project Files

* api/Program.cs - OBO/auth wiring (18-24), CORS GET-only (29-43), middleware order (52-56), test hook (60); add gated endpoint mapping + CORS POST.
* api/Controllers/MeController.cs - OBO good path + `AudienceMatchesThisApi` reject-the-token pattern; model for the new controller's authorize/scope shape.
* api/Telemetry/OboClaimLogger.cs - `JwtShaped` regex (22-25), `TrackEvent("OboExchange")` (63), `Redact()` (85-93); reuse for `ReplayAttempt` event.
* api/Tests/NegativeControlTests.cs - xUnit + `WebApplicationFactory<Program>` host with swapped JWT trust anchor; model for replay-endpoint tests.
* api/appsettings.json - config keys; add `Demo:EnableReplay=false`.
* spa/src/authConfig.ts - API-scope-only `apiRequest`/`loginRequest` (26-33); add `graphRequest`.
* spa/src/getApiToken.ts - silent→popup token A; model for `getGraphToken.ts`.
* spa/src/api.ts - `callApiMe` (65-83), `decodeJwtClaims` (96-133), `callGraphWithApiTokenWrongWay` Tier 1 (159-215); add `callApiReplay`.
* spa/src/App.tsx - buttons rendered in `AuthenticatedTemplate` (183-192, Tier 1 always-on); add `VITE_ENABLE_REPLAY_DEMO`-gated Tier 2 section.
* spa/src/components/ReplayAttemptPanel.tsx - Tier 1 result panel; extend for the server-side replay result.
* infra/modules/appservice.bicep - API app settings (116-152); add `Demo__EnableReplay='false'`.
* scripts/provision-app-registrations.sh - Graph User.Read granted to API only (243-249); add SPA Graph consent.
* scripts/teardown-app-registrations.sh - display-name idempotent teardown (28-69); extend to revoke SPA Graph grant + reset gates.
* scripts/setup-deploy-identity.sh - OIDC FIC subjects (81-104); add `replay-lab` environment credential.
* scripts/evidence-kql.kusto - Query 3 already extracts 1008 (47-58); correct display names + add correlation query.
* .github/workflows/deploy-croesus.yml - `environment: production` manual gate + `if: vars.*` gating; model for replay-lab.
* docs/configuration-contract.md - config-contract-first rule; add four new rows.
* docs/obo-demo-guide.md, docs/evidence-narrative.md, README.md - Tier 2 narrative + 1008 boundary.

### References

* .copilot-tracking/research/2026-07-06/tier2-ca-1008-replay-reproduction-research.md - authoritative research (selected approach, key discoveries, examples).
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md - exact file/line insertion points.
* .copilot-tracking/research/subagents/2026-07-06/token-protection-1008-mechanics.md - 1008 mechanics + KQL + support matrix.
* .copilot-tracking/research/subagents/2026-07-06/reversible-ca-provisioning-and-obo-gap.md - Graph CA schema, teardown pattern, OBO gap.

### Standards References

* docs/configuration-contract.md — every new config value gets a contract row before wiring; secrets only in Key Vault / GitHub secrets.
* README.md "Wrong versus right" + "App registration comparison" — the wrong-vs-right framing and five-item gap.

## Implementation Checklist

### [x] Implementation Phase 1: API Tier 2a replay endpoint, gate, CORS, telemetry, tests

<!-- parallelizable: true -->

* [x] Step 1.1: Add `Demo:EnableReplay=false` to api/appsettings.json
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 12-27)
* [x] Step 1.2: Create api/Controllers/ReplayController.cs (POST /api/replay, fixed-target, claims-only ReplayAttempt)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 29-63)
* [x] Step 1.3: Gate endpoint registration + widen CORS to POST in api/Program.cs
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 65-88)
* [x] Step 1.4: Add ReplayAttempt telemetry helper to api/Telemetry/OboClaimLogger.cs
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 90-108)
* [x] Step 1.5: Create api/Tests/ReplayEndpointTests.cs (authorize + redaction + gate-off-absent)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 110-134)
* [x] Step 1.6: Validate phase changes (dotnet build + test api/Tests)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 136-146)

### [x] Implementation Phase 2: SPA Graph token + gated replay flow

<!-- parallelizable: true -->

* [x] Step 2.1: Add `graphRequest` to spa/src/authConfig.ts (VITE_GRAPH_SCOPE)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 150-166)
* [x] Step 2.2: Create spa/src/getGraphToken.ts (silent→popup Graph token)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 168-182)
* [x] Step 2.3: Add `callApiReplay` to spa/src/api.ts (POST /api/replay with Graph token)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 184-204)
* [x] Step 2.4: Add VITE_ENABLE_REPLAY_DEMO-gated Tier 2 section + wrong-vs-right in spa/src/App.tsx and ReplayAttemptPanel.tsx
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 206-230)
* [x] Step 2.5: Validate phase changes (tsc + vite build)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 232-241)

### [x] Implementation Phase 3: Infrastructure + configuration contract

<!-- parallelizable: true -->

* [x] Step 3.1: Add `Demo__EnableReplay='false'` API app setting to infra/modules/appservice.bicep
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 245-262)
* [x] Step 3.2: Add four new rows to docs/configuration-contract.md
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 264-282)
* [x] Step 3.3: Validate phase changes (bicep build)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 284-292)

### [x] Implementation Phase 4: Reversible provisioning & teardown scripts

<!-- parallelizable: true -->

* [x] Step 4.1: Extend scripts/provision-app-registrations.sh to grant SPA Graph User.Read + admin consent (record grant id)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 296-318)
* [x] Step 4.2: Create scripts/provision-ca-policy.sh (beta report-only token-protection policy; write state file)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 320-344)
* [x] Step 4.3: Create scripts/teardown-ca-policy.sh (delete by recorded id; prefix sweep fallback)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 346-366)
* [x] Step 4.4: Extend scripts/teardown-app-registrations.sh to revoke SPA Graph grant(s), reset the live Demo__EnableReplay app setting, and remove the replay-lab federated credential
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 368-388)
* [x] Step 4.5: Create scripts/verify-clean.sh (assert no demo CA policy, no SPA Graph grant, no registrations, live gate off, no replay-lab FIC)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 390-408)
* [x] Step 4.6: Validate phase changes (bash -n syntax check on all scripts)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 410-418)

### [x] Implementation Phase 5: CI/CD replay-lab environment gating

<!-- parallelizable: false -->

* [x] Step 5.1: Add `replay-lab` federated-credential subject to scripts/setup-deploy-identity.sh
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 422-438)
* [x] Step 5.2: Add gated Tier 2 provision + evidence steps to .github/workflows/deploy-croesus.yml
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 440-464)
* [x] Step 5.3: Validate workflow YAML (actionlint or yaml parse)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 466-474)

### [x] Implementation Phase 6: Evidence & documentation

<!-- parallelizable: true -->

* [x] Step 6.1: Update scripts/evidence-kql.kusto (correct display names; add 1008 + two-leg correlation queries)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 478-498)
* [x] Step 6.2: Update docs/obo-demo-guide.md + docs/evidence-narrative.md (Tier 2a/2b walkthrough + 1008 boundary)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 500-520)
* [x] Step 6.3: Update README.md Tier 2 sections (P1 correction; 2a/2b; five-item gap; reversible teardown)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 522-540)

### [x] Implementation Phase 7: Validation

<!-- parallelizable: false -->

* [x] Step 7.1: Run full project validation (dotnet build+test, spa tsc+build, bicep build, script syntax, markdown lint)
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 544-560)
* [x] Step 7.2: Fix minor validation issues
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 562-570)
* [x] Step 7.3: Report blocking issues + next steps
  * Details: .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Lines 572-580)

## Planning Log

See .copilot-tracking/plans/logs/2026-07-06/tier2-ca-1008-replay-log.md for discrepancy tracking, implementation paths considered, and suggested follow-on work.

## Dependencies

* .NET 8 SDK; Microsoft.Identity.Web 4.11.0; xUnit + Microsoft.AspNetCore.Mvc.Testing 8.0.10.
* Node 22 + Vite 6; @azure/msal-browser ^3.28.1 / @azure/msal-react ^2.2.0.
* Azure CLI with Graph access (`az rest`); jq for the state-file pattern.
* Tenant roles/permissions: Conditional Access Administrator (or Security Administrator), `Policy.ReadWrite.ConditionalAccess`, `DelegatedPermissionGrant.ReadWrite.All`.
* A test user object id + a break-glass exclude account id for the CA policy scope.
* Entra ID P1 (Token Protection) — provisioned in the demo tenant.

## Success Criteria

* Gated `POST /api/replay` exists, is `[Authorize]`d + `[RequiredScope("access_as_user")]`, replays a forwarded Graph token to a FIXED target, logs claims-only, and is ABSENT when `Demo:EnableReplay=false`. — Traces to: user request (server-side replay) + research Selected Approach.
* SPA behind `VITE_ENABLE_REPLAY_DEMO` acquires a real Graph token, drives the replay endpoint, and renders wrong-vs-right beside OBO evidence. — Traces to: user request (side-by-side wrong/right).
* Report-only Token Protection CA policy provisions (beta `secureSignInSession`), scoped to a test user + supported resource; its Unbound/1008 line is surfaced by KQL. — Traces to: user request (reproduce 1008) + Key Discovery 2.
* A single teardown restores the tenant exactly: CA policy deleted, SPA Graph grant(s) revoked, live `Demo__EnableReplay` reset (or removed), `replay-lab` federated credential removed, gates reset, registrations + cert removed; `verify-clean.sh` asserts empty on all five checks. Already-issued Graph tokens remain valid until natural expiry (documented, short-lived). — Traces to: user request (reversible undo).
* Docs present the five-item OBO gap and the honest 1008 boundary (2a shape vs 2b telemetry). — Traces to: user request (what's missing) + research Scope.
* All builds/tests/linters pass; no raw tokens are ever logged or returned. — Traces to: repo conventions + Success Criteria.
