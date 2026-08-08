---
description: "Deploy both classic BFF proofs to one Windows B1 App Service plan"
applyTo: '.copilot-tracking/changes/2026-08-07/classic-net-bff-appservice-deployment-changes.md'
---
<!-- markdownlint-disable-file -->

# Implementation Plan: Classic BFF App Service Deployment

## Overview

Add a repeatable, low-cost Azure demonstration deployment for the .NET Framework 4.5.2 compatibility proof and the .NET 10 reference BFF. Use one Windows B1 App Service plan, two web apps, one confidential Microsoft Entra web registration, GitHub Actions workload identity federation, and no committed credentials.

## Objectives

### User Requirements

* Deploy both PoCs through GitHub Actions and Bicep
* Use no App Service plan tier higher than B1
* Create the Microsoft Entra app registrations required for the demonstration
* Produce a demonstrable environment for Desjardins and Croesus guidance

### Derived Objectives

* Keep this deployment separate from the existing Linux OBO demonstration infrastructure
* Share one Windows B1 plan across both web apps
* Preserve the legacy `net452` compile target while documenting that App Service hosts it on the installed .NET Framework 4.8 runtime
* Publish the .NET 10 app self-contained for `win-x64`
* Use one confidential `web` registration with both callback URIs
* Use GitHub OIDC for Azure and Microsoft Graph access without a stored deployment credential
* Permit a short-lived client secret only in an explicit `Poc` environment
* Validate infrastructure and packages without performing live Azure or Entra mutations

## Standards References

* `c:/Users/emknafo/.vscode/extensions/ise-hve-essentials.hve-core-3.2.2/.github/instructions/hve-core/markdown.instructions.md`
* `c:/Users/emknafo/.vscode/extensions/ise-hve-essentials.hve-core-3.2.2/.github/instructions/hve-core/writing-style.instructions.md`
* `c:/Users/emknafo/.vscode/extensions/ise-hve-essentials.hve-core-3.2.2/.github/instructions/hve-core/prompt-builder.instructions.md`
* Azure deployment guidance captured on 2026-08-07: use Bicep, least privilege, managed deployment identity, secure parameters, HTTPS, and what-if before deployment

## Implementation Checklist

### [x] Implementation Phase 1: Prepare Both Applications for App Service

<!-- parallelizable: false -->

* [x] Step 1.1: Permit absolute HTTPS deployment callbacks in the legacy host while retaining strict URI validation
* [x] Step 1.2: Permit a client secret only in the explicit modern `Poc` environment while preserving Production credential requirements
* [x] Step 1.3: Add focused regression tests and publish settings for Windows App Service

### [x] Implementation Phase 2: Add the B1 Bicep Stack

<!-- depends-on: phase 1 -->

* [x] Step 2.1: Add one Windows B1 App Service plan and two HTTPS-only web apps under `infra/poc/`
* [x] Step 2.2: Configure non-secret identity settings and secure client-secret injection for both apps
* [x] Step 2.3: Expose stable app names, host names, callback URIs, and registration inputs as outputs

### [x] Implementation Phase 3: Add Entra Deployment Automation

<!-- depends-on: phase 2 -->

* [x] Step 3.1: Extend provisioning for deterministic deployed callback URIs and non-interactive GitHub execution
* [x] Step 3.2: Rotate one named short-lived demo credential without accumulating credentials
* [x] Step 3.3: Add ownership-scoped teardown and static security regression coverage

### [x] Implementation Phase 4: Add GitHub Actions Workflows

<!-- depends-on: phases 1, 2, 3 -->

* [x] Step 4.1: Add a manually dispatched validation and deployment workflow using GitHub OIDC
* [x] Step 4.2: Build, test, package, run Bicep what-if, provision Entra, deploy infrastructure, and deploy both applications
* [x] Step 4.3: Add a guarded manual teardown workflow and actionable run summaries

### [x] Implementation Phase 5: Document and Validate the Deployment

<!-- depends-on: phases 1, 2, 3, 4 -->

* [x] Step 5.1: Document bootstrap permissions, configuration, deployment, demo flow, caveats, rotation, and teardown
* [x] Step 5.2: Build and test both applications and validate all PowerShell scripts
* [x] Step 5.3: Compile Bicep, validate workflow diagnostics, inspect publish artifacts, and scan for credential or RBAC regressions

### [x] Implementation Phase 6: Fail Honestly on What-If Prerequisite Errors

<!-- depends-on: phase 5 -->

* [x] Step 6.1: Check the Azure CLI exit code before parsing the resource-group existence result
* [x] Step 6.2: Add a static regression check that prevents the false-green ordering from returning
* [x] Step 6.3: Validate, commit, push, and rerun the validation workflow

### [x] Implementation Phase 7: Complete Interactive Authentication

<!-- depends-on: phase 6 -->

* [x] Step 7.1: Allow bounded OIDC callback query strings through IIS for both applications
* [x] Step 7.2: Preserve a direct 401 response for the anonymous legacy session API
* [x] Step 7.3: Add focused regressions, deploy both packages, and verify callback routing and signed-out behavior

### [ ] Implementation Phase 8: Diagnose Authorization-Code Completion

<!-- depends-on: phase 7 -->

* [x] Step 8.1: Raise the bounded classic ASP.NET query-string limit for the legacy callback
* [x] Step 8.2: Record secret-safe modern remote-failure categories and protocol error codes
* [ ] Step 8.3: Validate, redeploy, and complete interactive callback verification

## Success Criteria

* The Bicep deployment contains exactly one Windows B1 App Service plan and two web apps
* Neither web app uses an App Service tier above B1
* The legacy artifact remains compiled for .NET Framework 4.5.2 and is described accurately as running on App Service's installed .NET Framework 4.8 runtime
* The modern artifact is self-contained for `win-x64` and does not depend on an App Service .NET 10 shared runtime
* Both applications use one confidential Entra `web` registration and keep tokens out of browser code
* GitHub Actions uses OIDC and contains no long-lived Azure deployment secret
* The demo credential is short-lived, masked, replaced deterministically, and never committed or persisted as an artifact
* Multi-tenant mode remains explicit and requires an allowed-tenant list
* Validation can run without changing Azure or Entra resources
* OIDC code-flow callbacks longer than the IIS default query-string limit reach application middleware
* The anonymous legacy session API returns 401 without redirecting fetch requests to Microsoft Entra
