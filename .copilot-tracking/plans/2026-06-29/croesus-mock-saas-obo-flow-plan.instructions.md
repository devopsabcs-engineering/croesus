---
applyTo: '.copilot-tracking/changes/2026-06-29/croesus-mock-saas-obo-flow-changes.md'
---
<!-- markdownlint-disable-file -->
# Implementation Plan: Mock Croesus SaaS App with Standards-Compliant MSAL OBO Flow

## Overview

Build a runnable mock "Croesus / GPD Central" SaaS app — MSAL.js SPA (public client) -> ASP.NET Core middle-tier API (confidential client) -> Microsoft Graph via standards On-Behalf-Of — with provisioning scripts, Bicep, a GitHub Actions OIDC pipeline, and audience-binding evidence that proves the token-replay issue is avoidable.

## Objectives

### User Requirements

* Mock SaaS app modeling Croesus/GPD Central using MSAL.js doing the proper post-login OAuth flow — Source: .copilot-tracking/research/2026-06-29/croesus-mock-saas-obo-flow-research.md (Task Implementation Requests).
* Confidential middle-tier API performing a standards-compliant OBO exchange to Microsoft Graph — Source: research Task Implementation Requests.
* Demonstrate the absence of the token-replay issue via runtime evidence and logs — Source: research Task Implementation Requests.
* CI/CD pipeline (GitHub Actions) deploying to Azure and surfacing sign-in + API logs as evidence — Source: research Task Implementation Requests.
* Framing: "we are the Croesus vendor"; Desjardins (outsider) creates the registrations and performs SaaS SSO with the correct flow — Source: research Task Implementation Requests.

### Derived Objectives

* Re-anchor the "no replay" proof on OBO audience-binding rather than Token Protection code 1008 — Derived from: research CRITICAL CORRECTION (1008 is native-app/EXO-SPO-Teams only and will not fire for a browser SPA + custom API + Graph).
* Use ASP.NET Core + Microsoft.Identity.Web for the API — Derived from: research "correct-by-construction" recommendation (no place to accidentally implement a replay).
* Single-tenant registrations with a certificate-in-Key-Vault credential — Derived from: research app-registration design (Microsoft-recommended shape for customer-builds-vendor-app; cert over secret).
* Idempotent provisioning/teardown/verification scripts — Derived from: research note that `az ad app create` is not idempotent (CI re-runs would duplicate registrations).

## Context Summary

### Project Files

* assets/app-registration-analysis-findings.md - The broken token-replay baseline (SPA-only public clients, no credential/exposed scope) the demo contrasts against.
* assets/croesus-escalation-packet.md - Vendor questions Q1-Q6 the demo answers with concrete evidence.
* README.md - Existing analysis; extended with the OBO demo section in Phase 7.

### References

* .copilot-tracking/research/2026-06-29/croesus-mock-saas-obo-flow-research.md - Primary research; SELECTED scenarios 1-3 and the audience-binding correction.
* .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md - OBO protocol, MSAL config, ASP.NET Core fluent chain, code snippets.
* .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md - Two-registration design, provisioning script, verification table.
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md - App Service hosting, GitHub Actions OIDC, Key Vault cert, evidence/logging + KQL.

### Standards References

* .github/copilot-instructions.md - Repository conventions (none currently present beyond analysis docs; layout is net-new).

## Implementation Checklist

### [ ] Implementation Phase 1: Repository scaffolding and shared configuration

<!-- parallelizable: false -->

* [ ] Step 1.1: Create top-level directory structure and root tooling files
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 38-60)
* [ ] Step 1.2: Author the demo configuration contract document
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 61-77)

### [ ] Implementation Phase 2: SPA front end (React + MSAL.js public client)

<!-- parallelizable: true -->

* [ ] Step 2.1: Scaffold the Vite React TypeScript SPA project
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 82-103)
* [ ] Step 2.2: Implement MSAL auth config and token acquisition for the API scope
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 104-121)
* [ ] Step 2.3: Build the demo UI showing sign-in, API call, and audience-binding evidence
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 122-142)
* [ ] Step 2.4: Validate SPA build
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 143-155)

### [ ] Implementation Phase 3: Middle-tier API (ASP.NET Core + Microsoft.Identity.Web OBO) with evidence logging

<!-- parallelizable: true -->

* [ ] Step 3.1: Scaffold the ASP.NET Core Web API project
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 160-180)
* [ ] Step 3.2: Implement the /api/me OBO controller with audience/scope enforcement
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 181-198)
* [ ] Step 3.3: Add App Insights structured claim logging (evidence layer 3a)
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 199-216)
* [ ] Step 3.4: Add the negative-control endpoints/tests (evidence layer)
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 217-234)
* [ ] Step 3.5: Validate API build and tests
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 235-248)

### [ ] Implementation Phase 4: App registration provisioning, teardown, and verification scripts

<!-- parallelizable: true -->

* [ ] Step 4.1: Author the idempotent provisioning script (two single-tenant registrations)
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 253-272)
* [ ] Step 4.2: Author the teardown and verification scripts
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 273-290)
* [ ] Step 4.3: Author smoke-test and negative-test scripts for CI
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 291-313)

### [ ] Implementation Phase 5: Infrastructure as Code (Bicep)

<!-- parallelizable: true -->

* [ ] Step 5.1: Author Bicep for App Service, Key Vault, Managed Identity, App Insights, and diagnostics
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 318-339)
* [ ] Step 5.2: Validate Bicep
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 340-352)

### [ ] Implementation Phase 6: CI/CD pipeline and post-deploy evidence job

<!-- parallelizable: false -->

* [ ] Step 6.1: Author the GitHub Actions deploy workflow (OIDC, no deploy secret)
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 357-374)
* [ ] Step 6.2: Add the post-deploy evidence job
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 375-393)

### [ ] Implementation Phase 7: Documentation and evidence narrative

<!-- parallelizable: true -->

* [ ] Step 7.1: Write the demo README and setup guide
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 398-418)

### [ ] Implementation Phase 8: Validation

<!-- parallelizable: false -->

* [ ] Step 8.1: Run full project validation
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 423-436)
* [ ] Step 8.2: Fix minor validation issues
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 437-443)
* [ ] Step 8.3: Report blocking issues
  * Details: .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md (Lines 444-452)

## Planning Log

See .copilot-tracking/plans/logs/2026-06-29/croesus-mock-saas-obo-flow-log.md for discrepancy tracking, implementation paths considered, and suggested follow-on work.

## Dependencies

* Node.js 20 + npm (SPA build).
* .NET 8 SDK (API build/test).
* Azure CLI with the `bicep` extension.
* Azure subscription + demo Entra tenant (live deploy/evidence only).
* GitHub repository `devopsabcs-engineering/croesus` with OIDC federated credential + `vars.*` (live deploy only).

## Success Criteria

* Runnable SPA (public client) + ASP.NET Core API (confidential client) demonstrate a standards OBO exchange where the SPA never holds a Graph token — Traces to: research Scenario 1 (SELECTED); user requirement (proper MSAL OBO flow).
* Two correctly-shaped single-tenant registrations exist via idempotent scripts (exposed scope, Key Vault certificate, pre-authorization, Graph User.Read consent) and a verifier asserts OBO-capability vs the broken baseline — Traces to: app-registration-design-research verification table; user requirement (correct registrations).
* Bicep provisions App Service x2 + Key Vault + Managed Identity + App Insights + Log Analytics + sign-in diagnostics with no secret material in IaC — Traces to: azure-hosting-cicd-evidence-research hosting recommendation.
* GitHub Actions OIDC pipeline deploys both apps and a gated post-deploy job surfaces two-leg audience-binding evidence with portal deep links — Traces to: research Scenario 2 (SELECTED); user requirement (CI/CD surfacing evidence).
* Documentation frames the wrong-vs-right contrast and anchors the proof on OBO audience-binding, with Token Protection 1008 demoted to an optional advanced exhibit — Traces to: research CRITICAL CORRECTION; user requirement (prove the issue is avoidable).
