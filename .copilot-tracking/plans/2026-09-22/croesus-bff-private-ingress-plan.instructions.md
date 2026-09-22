---
applyTo: '.copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md'
---
<!-- markdownlint-disable-file -->
# Implementation Plan: Croesus BFF Private Ingress and Security Evidence

## Overview

Restore reachability to the Croesus POC App Services through per-app private endpoints while public ingress stays disabled, then add a hardened reference BFF and honest authentication evidence that does not overstate what the logs prove.

## Objectives

### User Requirements

* Keep public network access disabled because Azure Policy prohibits it, and use private endpoints instead — Source: conversation 2026-09-22, "we are getting this because of azure policy disallowing public access --- perhaps we should use private endpoints?"
* Eliminate the critical, high, and major defects found in the earlier plan before committing to implementation — Source: conversation 2026-09-22, rubber-duck request
* Produce implementation planning artefacts from the revised research — Source: conversation 2026-09-22, task-plan request

### Derived Objectives

* Verify the enforcing policy assignment before designing around it — Derived from: an application-layer HTTP 403 does not identify who set publicNetworkAccess to Disabled, so the constraint must be confirmed rather than assumed
* Treat browser reachability, DNS resolution, and SCM deployment as three separate paths — Derived from: a private endpoint that serves the browser still leaves a hosted CI runner unable to deploy
* Replace browser-held token custody with server-side caching in the reference BFF — Derived from: SaveTokens places tokens in the encrypted ticket the browser holds, which contradicts the BFF premise
* Gate the legacy comparison app on a supported-runtime verdict — Derived from: private networking is not a runtime-support exemption, and silently dropping the legacy app would reduce the stated two-app scope
* Separate deterministic CI checks from delegated-user evidence — Derived from: workload identity authentication and report-only policy results cannot substitute for user security proof
* Decide the legacy identity bridge scope before Phase 3 assumes it — Derived from: a BFF cannot be said to front the legacy app without an explicit bridge and blocked direct ingress, and private networking creates neither

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

Execution waves: Phase 1 runs alone. Phases 2, 3, 4, and 5 form wave 1 and may run concurrently, with no data dependency between them because Step 2.4 and Step 4.1 both read the BFF hostname recorded in Step 1.2. Phases 6 and 7 form wave 2 and may run concurrently with each other once wave 1 completes. Phase 8 runs last. Parallel phases append only under their own uniquely titled section of the changes file and never rewrite another phase's section.

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

### [ ] Implementation Phase 2: Private Ingress Infrastructure

BLOCKED by Phase 1. The deployed App Service plan is F1 Free, which supports no private endpoint, and no private network path exists in the subscription. The Azure Policy premise that motivated this phase was falsified. Awaiting the ID-01 architecture decision.

<!-- parallelizable: true -->

* [ ] Step 2.1: Pin disabled public ingress in Bicep
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 175-206)
* [ ] Step 2.2: Add a private endpoint module and per-app endpoints
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 207-236)
* [ ] Step 2.3: Wire private DNS records for application and SCM hostnames
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 237-260)
* [ ] Step 2.4: Add the optional reference BFF site and endpoint
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 261-293)
* [ ] Step 2.5: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 294-306)

### [x] Implementation Phase 3: Reference BFF Application

<!-- parallelizable: true -->

* [x] Step 3.1: Scaffold the project and test project from the modern app conventions
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 313-352)
* [x] Step 3.2: Implement server-side token and session custody
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 353-388)
* [x] Step 3.3: Implement the constrained proxy boundary
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 389-426)
* [x] Step 3.4: Implement the session and token lifecycle
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 427-458)
* [x] Step 3.5: Implement the sanitized evidence surface
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 459-501)
* [x] Step 3.6: Implement negative authorization and protocol tests
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 502-541)
* [x] Step 3.7: Implement the legacy bridge contract or record it blocked
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 542-581)
* [x] Step 3.8: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 582-592)

### [x] Implementation Phase 4: Registration and Evidence Queries

<!-- parallelizable: true -->

* [x] Step 4.1: Add the confidential BFF registration block
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 599-637)
* [x] Step 4.2: Set the owned API token version and optional claims
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 638-670)
* [x] Step 4.3: Replace speculative KQL with operation-mapped queries
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 671-709)
* [x] Step 4.4: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 710-720)

### [ ] Implementation Phase 5: Provisioning and Verification Scripts

<!-- parallelizable: true -->

* [ ] Step 5.1: Add private path preflight assertions
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 727-767)
* [ ] Step 5.2: Add a public negative check
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 768-792)
* [ ] Step 5.3: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 793-803)

### [ ] Implementation Phase 6: Pipeline Wiring

<!-- parallelizable: true -->

* [ ] Step 6.1: Split hosted validation from private deployment
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 810-843)
* [ ] Step 6.2: Separate deterministic checks from delegated user evidence
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 844-878)
* [ ] Step 6.3: Define rollback behaviour
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 879-903)

### [ ] Implementation Phase 7: Documentation Correction

<!-- parallelizable: true -->

* [ ] Step 7.1: Correct the private access prerequisites and proof limits
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 910-937)
* [ ] Step 7.2: Validate phase changes
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 938-947)

### [ ] Implementation Phase 8: Validation

<!-- parallelizable: false -->

* [ ] Step 8.1: Run full project validation
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 952-961)
* [ ] Step 8.2: Fix minor validation issues
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 962-965)
* [ ] Step 8.3: Report blocking issues
  * Details: .copilot-tracking/details/2026-09-22/croesus-bff-private-ingress-details.md (Lines 966-976)

## Planning Log

See .copilot-tracking/plans/logs/2026-09-22/croesus-bff-private-ingress-log.md for discrepancy tracking, implementation paths considered, and suggested follow-on work.

## Dependencies

* Azure CLI with reader access for Phase 1 and scoped contributor access for later deployment
* Bicep CLI for template compilation
* .NET 10 SDK for the reference BFF application
* PowerShell 7 for the verification scripts
* An approved private network path for both the browser and the deployment runner
* Entra ID P1 or better in the demo tenant for any Conditional Access evaluation

## Success Criteria

* Public application and SCM ingress remain disabled throughout, and no repository command sets publicNetworkAccess to Enabled — Traces to: user requirement, Azure Policy prohibits public access
* Each approved app is reachable over its own private endpoint using normal HTTPS hostnames from the actual browser and the deployment runner — Traces to: research Scenario 1 Network and Deployment Contract
* The enforcing policy assignment is recorded, or its absence is recorded explicitly as unverified — Traces to: research H1 and the policy-versus-ingress distinction
* The legacy app either passes the runtime support gate or its exclusion is recorded with remaining work, rather than being silently dropped — Traces to: research Scope and Success Criteria
* No OAuth token appears in any browser cookie, proven by a sentinel assertion — Traces to: research H3 and Scenario 2 token custody
* State-changing proxied operations enforce antiforgery validation, and forwarded requests carry only the server-acquired credential — Traces to: research H4 and Scenario 2 items 1 to 3
* Evidence artefacts report pass, fail, or not-executed per criterion, and no On-Behalf-Of claim appears unless that hop was exercised and instrumented — Traces to: research H7, H10, and Scenario 5
* Wrong-audience, insufficient-scope, state mismatch, nonce mismatch, and callback replay are each proven to be rejected — Traces to: research Scenario 2 items 3 and 9, and the Scenario 4 proof table
* The legacy identity bridge is either implemented with one authenticated and one forbidden route, or explicitly recorded as blocked with its unblocking dependency — Traces to: research H5 and Scenario 3
