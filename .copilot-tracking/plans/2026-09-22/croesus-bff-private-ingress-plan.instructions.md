---
applyTo: '.copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md'
---
<!-- markdownlint-disable-file -->
# Implementation Plan: Croesus BFF Ingress Restoration and Security Evidence

## Overview

Restore reachability to the Croesus POC App Services over public ingress, add a hardened reference BFF, and produce honest authentication evidence that does not overstate what the logs prove. Private ingress is documented as the production-successor design rather than built, because Phase 1 falsified the premise that policy compelled it and the hosting tier cannot sustain it.

## Objectives

### User Requirements

* Adopt public ingress for the POC demo and document private ingress as the production-successor design — Source: conversation 2026-09-22, ID-01 decision, Option C accepted
* Scale the App Service plan to B1, accepting that an external process may degrade it back to F1 on roughly a 24 hour cycle — Source: conversation 2026-09-22, "scaling to B1 is authorized but know that azure policy may degrade to F1 every 24 hours so will need to be reminded to redeploy if 403"
* Eliminate the critical, high, and major defects found in the earlier plan before committing to implementation — Source: conversation 2026-09-22, rubber-duck request
* Produce implementation planning artefacts from the revised research — Source: conversation 2026-09-22, task-plan request

### Superseded Requirement

* Keep public network access disabled because Azure Policy prohibits it, and use private endpoints instead — Source: conversation 2026-09-22, "we are getting this because of azure policy disallowing public access --- perhaps we should use private endpoints?". Withdrawn after Phase 1 Step 1.1 found no policy assignment at any reachable scope enforcing publicNetworkAccess on Microsoft.Web/sites. Recorded as DD-17.

### Derived Objectives

* Verify the enforcing policy assignment before designing around it — Derived from: an application-layer HTTP 403 does not identify who set publicNetworkAccess to Disabled, so the constraint must be confirmed rather than assumed. This objective was met and it overturned the plan's premise.
* Diagnose a 403 by distinguishing its causes rather than prescribing a blind redeploy — Derived from: tier degradation, disabled public ingress, and a stopped site each produce a failure the operator sees as the same symptom
* Replace browser-held token custody with server-side caching in the reference BFF — Derived from: SaveTokens places tokens in the encrypted ticket the browser holds, which contradicts the BFF premise
* Gate the legacy comparison app on a supported-runtime verdict — Derived from: ingress choice is not a runtime-support exemption, and silently dropping the legacy app would reduce the stated two-app scope
* Separate deterministic CI checks from delegated-user evidence — Derived from: workload identity authentication and report-only policy results cannot substitute for user security proof
* Keep the private ingress design recoverable as documentation — Derived from: the production successor still needs it, and the research that produced it remains valid even though the POC will not build it

## Context Summary

### Project Files

* infra/poc/main.bicep - App Service plan and both site definitions that must pin disabled public ingress and gain private endpoints
* infra/poc/main.bicepparam - parameter surface for the existing VNet, subnet, and private DNS zone identifiers
* scripts/provision-classic-net-bff-deployment.ps1 - deployment script requiring private path preflight assertions
* scripts/provision-app-registrations.sh - idempotent Entra registration provisioning with a documented scope-before-preauthorization ordering constraint
* scripts/evidence-kql.kusto - evidence queries that currently infer an OAuth grant from resource counts
* .github/workflows/classic-net-bff-poc.yml - validate and deploy jobs; the deploy job runs on windows-latest at line 275 and cannot reach a private endpoint
* poc/modern-net10/Program.cs - hardened cookie, forwarded header, and issuer validation conventions to carry into the reference BFF
* poc/legacy-net452/ - legacy comparison app subject to the runtime support gate
* api/Tests/ReplayEndpointTests.cs - existing non-disclosure assertion convention to extend
* docs/evidence-narrative.md - proof narrative containing stale Token Protection wording

### References

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - revised primary research and the source of the H1 to H10 corrections
* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - private endpoint, DNS, and runner requirements with official sources
* .copilot-tracking/research/subagents/2026-09-22/bff-auth-evidence-review.md - evidence claims that the logs do not support
* .copilot-tracking/research/subagents/2026-09-22/bff-architecture-security-review.md - token custody, proxy, and lifecycle design findings
* https://learn.microsoft.com/en-us/azure/app-service/overview-private-endpoint - per-app endpoints and the sites subresource
* https://learn.microsoft.com/en-us/azure/private-link/private-endpoint-dns-integration - private DNS zone group behaviour

### Standards References

* assets/croesus-escalation-packet.md - engagement guardrails: no customer tenant changes, no Conditional Access edits, no IP allowlisting
* assets/croesus-3way-session-findings.md - agreed scope boundaries for the POC
* docs/configuration-contract.md - configuration and credential handling conventions

## Implementation Checklist

Execution waves: Phase 1 runs alone. Phases 2, 3, 4, 5, and 9 form wave 1 and may run concurrently, with no data dependency between them because Step 2.4 and Step 4.1 both read the BFF hostname recorded in Step 1.2. Phases 6 and 7 form wave 2 and may run concurrently with each other once wave 1 completes. Phase 8 runs last, after Phase 9 despite the lower number. Parallel phases append only under their own uniquely titled section of the changes file and never rewrite another phase's section.

### [x] Implementation Phase 1: Prerequisite Verification and Gate Capture

<!-- parallelizable: false -->

* [x] Step 1.1: Capture effective Azure Policy evidence
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 24-65)
* [x] Step 1.2: Confirm reusable network, DNS, browser, and runner topology
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 66-104)
* [x] Step 1.3: Gate the legacy comparison app on supported-target review
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 106-134)
* [x] Step 1.4: Decide legacy identity bridge scope and bypass prevention
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 135-168)

### [x] Implementation Phase 2: Ingress Restoration and Successor Design

Rescoped by the ID-01 decision. Private endpoints are no longer built. Two Phase 1 findings drove this: no policy enforces disabled public ingress, and the plan is degraded to F1 on roughly a 24 hour cycle, which would break a private endpoint each time because F1 cannot host one.

<!-- parallelizable: true -->

* [x] Step 2.1: Parameterize ingress and restore public access for the POC
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 177-209)
* [x] Step 2.2: Retain the private endpoint path as an opt-in successor module
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 210-246)
* [x] Step 2.3: Make the plan tier explicit and self-diagnosing
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 247-277)
* [x] Step 2.4: Add the reference BFF site on its own hostname
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 278-314)
* [x] Step 2.5: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 315-328)

### [x] Implementation Phase 3: Reference BFF Application

<!-- parallelizable: true -->

* [x] Step 3.1: Scaffold the project and test project from the modern app conventions
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 335-374)
* [x] Step 3.2: Implement server-side token and session custody
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 375-410)
* [x] Step 3.3: Implement the constrained proxy boundary
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 411-448)
* [x] Step 3.4: Implement the session and token lifecycle
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 449-480)
* [x] Step 3.5: Implement the sanitized evidence surface
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 481-523)
* [x] Step 3.6: Implement negative authorization and protocol tests
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 524-563)
* [x] Step 3.7: Implement the legacy bridge contract or record it blocked
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 564-603)
* [x] Step 3.8: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 604-614)

### [x] Implementation Phase 4: Registration and Evidence Queries

<!-- parallelizable: true -->

* [x] Step 4.1: Add the confidential BFF registration block
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 621-659)
* [x] Step 4.2: Set the owned API token version and optional claims
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 660-692)
* [x] Step 4.3: Replace speculative KQL with operation-mapped queries
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 693-731)
* [x] Step 4.4: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 732-742)

### [x] Implementation Phase 5: Provisioning and Verification Scripts

<!-- parallelizable: true -->

* [x] Step 5.1: Add ingress preflight assertions and 403 diagnosis
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 749-801)
* [x] Step 5.2: Add the posture negative check
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 802-828)
* [x] Step 5.3: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 829-839)

### [x] Implementation Phase 6: Pipeline Wiring

<!-- parallelizable: true -->

* [x] Step 6.1: Wire the reference BFF and ingress verification into CI
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 846-881)
* [x] Step 6.2: Separate deterministic checks from delegated user evidence
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 882-916)
* [x] Step 6.3: Define rollback behaviour
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 917-941)

### [x] Implementation Phase 7: Documentation Correction

<!-- parallelizable: true -->

* [x] Step 7.1: Document the ingress decision, its successor, and the proof limits
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 948-991)
* [x] Step 7.2: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 992-1001)

### [x] Implementation Phase 8: Validation

<!-- parallelizable: false -->

* [x] Step 8.1: Run full project validation
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 1006-1015)
* [x] Step 8.2: Fix minor validation issues
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 1016-1019)
* [x] Step 8.3: Report blocking issues
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 1020-1030)

### [x] Implementation Phase 9: Legacy Runtime Retarget

Added after Phase 1 returned a blocked legacy readiness verdict. ID-02 authorized a retarget to .NET Framework 4.8 rather than dropping the legacy comparison app. Runs in wave 1 and must complete before Phase 8.

<!-- parallelizable: true -->

* [x] Step 9.1: Retarget the legacy comparison app to .NET Framework 4.8
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 1037-1072)
* [x] Step 9.2: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 1073-1084)

## Planning Log

See .copilot-tracking/plans/logs/2026-09-22/croesus-bff-private-ingress-log.md for discrepancy tracking, implementation paths considered, and suggested follow-on work.

## Dependencies

* Azure CLI with reader access for Phase 1 and scoped contributor access for later deployment
* Bicep CLI for template compilation
* .NET 10 SDK for the reference BFF application
* PowerShell 7 for the verification scripts
* An App Service plan at B1 or better for the demo, with the understanding that an external process degrades it to F1 on roughly a 24 hour cycle
* Entra ID P1 or better in the demo tenant for any Conditional Access evaluation

## Success Criteria

* Public ingress is restored for the POC as an explicit, parameterized decision rather than undocumented drift, and the superseded policy premise is recorded — Traces to: ID-01 decision and research H1
* A 403 against either app is diagnosable to one of tier degradation, disabled public ingress, or a stopped site, without guesswork — Traces to: user requirement on the 24 hour F1 degradation cycle
* The private endpoint design survives as an opt-in module and documented successor rather than being deleted — Traces to: research Scenario 1 Network and Deployment Contract
* The enforcing policy assignment is recorded, or its absence is recorded explicitly — Traces to: research H1. Met in Phase 1: absence recorded.
* The legacy app either passes the runtime support gate or its exclusion is recorded with remaining work, rather than being silently dropped — Traces to: research Scope and Success Criteria
* No OAuth token appears in any browser cookie, proven by a sentinel assertion — Traces to: research H3 and Scenario 2 token custody
* State-changing proxied operations enforce antiforgery validation, and forwarded requests carry only the server-acquired credential — Traces to: research H4 and Scenario 2 items 1 to 3
* Evidence artefacts report pass, fail, or not-executed per criterion, and no On-Behalf-Of claim appears unless that hop was exercised and instrumented — Traces to: research H7, H10, and Scenario 5
* Wrong-audience, insufficient-scope, state mismatch, nonce mismatch, and callback replay are each proven to be rejected — Traces to: research Scenario 2 items 3 and 9, and the Scenario 4 proof table
* The legacy identity bridge is either implemented with one authenticated and one forbidden route, or explicitly recorded as blocked with its unblocking dependency — Traces to: research H5 and Scenario 3
