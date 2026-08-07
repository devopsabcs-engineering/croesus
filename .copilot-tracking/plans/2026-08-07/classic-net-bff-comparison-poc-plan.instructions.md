---
description: "Implement a .NET Framework 4.5.2 BFF proof beside a supported .NET 10 reference"
applyTo: '.copilot-tracking/changes/2026-08-07/classic-net-bff-comparison-poc-changes.md'
---
<!-- markdownlint-disable-file -->

# Implementation Plan: Classic .NET BFF Comparison PoC

## Overview

Build a contained comparison PoC that proves confidential authorization-code redemption with PKCE on classic ASP.NET .NET Framework 4.5.2 and shows the supported destination on .NET 10 with Microsoft.Identity.Web/MSAL.NET. Add repeatable Entra registration automation for single-tenant and explicitly enabled multi-tenant modes, without Azure subscription RBAC or committed credentials.

## Objectives

### User Requirements

* Start with recommended option A: a 4.5.2 legacy PoC plus a .NET 10 reference BFF
* Highlight the requirement to move to .NET Framework 4.8 or .NET 10 for a supported production implementation
* Include a possible multi-tenant app registration and MSAL.NET-based reference implementation

### Derived Objectives

* Keep the browser token-free and use protected server-side session state
* Use one confidential `web` registration with separate local callback URIs so the two implementations are comparable
* Keep multi-tenant mode opt-in and require an explicit tenant allowlist
* Use a short-lived secret only for the Dev proof; document certificate or managed-identity-backed credential handling as the destination
* Do not introduce an obsolete MSAL.NET package into the 4.5.2 project

## Context Summary

* Central reportedly runs .NET Framework 4.5.2 and has a BFF boundary that still requires Q15 confirmation
* Current MSAL.NET does not support .NET Framework 4.5.2; Microsoft.Identity.Web is a modern ASP.NET Core library
* The workstation has .NET SDK 10 and .NET Framework 4.5.2 and 4.8 reference assemblies
* Existing repository code under `api/` remains the OBO evidence application and must not be repurposed as the Central compatibility PoC
* Azure subscription Contributor or other resource RBAC is not required for Entra app-registration provisioning

## Standards References

* `c:/Users/emknafo/.vscode/extensions/ise-hve-essentials.hve-core-3.2.2/.github/instructions/hve-core/markdown.instructions.md`
* `c:/Users/emknafo/.vscode/extensions/ise-hve-essentials.hve-core-3.2.2/.github/instructions/hve-core/writing-style.instructions.md`
* Azure best-practice result captured during planning: no hardcoded credentials, least privilege, short credential lifetime, and no unnecessary subscription RBAC

## Implementation Checklist

### [x] Implementation Phase 1: Build the Legacy 4.5.2 BFF

<!-- parallelizable: true -->

* [x] Step 1.1: Add a buildable classic ASP.NET/OWIN project targeting .NET Framework 4.5.2
* [x] Step 1.2: Implement authorization code with PKCE and confidential server-side redemption
* [x] Step 1.3: Add focused tests for PKCE, state protection, token redaction, and tenant allowlisting

### [x] Implementation Phase 2: Build the .NET 10 Reference BFF

<!-- parallelizable: true -->

* [x] Step 2.1: Add a .NET 10 ASP.NET Core BFF using Microsoft.Identity.Web/MSAL.NET
* [x] Step 2.2: Keep OAuth tokens server-side and expose only authenticated session-backed endpoints
* [x] Step 2.3: Add focused tests for authentication configuration and tenant allowlisting

### [x] Implementation Phase 3: Add Entra Provisioning and Operator Guidance

<!-- depends-on: phases 1, 2 -->

* [x] Step 3.1: Add idempotent app-registration provisioning for single-tenant and opt-in multi-tenant modes
* [x] Step 3.2: Add cleanup automation and secret-handling safeguards
* [x] Step 3.3: Add the comparison guide, caveat matrix, runbook, and root README link

### [x] Implementation Phase 4: Validate the Comparison PoC

<!-- depends-on: phases 1, 2, 3 -->

* [x] Step 4.1: Build and test the .NET Framework 4.5.2 implementation
* [x] Step 4.2: Build and test the .NET 10 implementation
* [x] Step 4.3: Validate scripts, documentation, credential hygiene, and final repository diagnostics

## Success Criteria

* Both PoC projects build locally without changing the existing `api/` application
* The 4.5.2 project uses no MSAL.NET or Microsoft.Identity.Web dependency
* The .NET 10 project uses supported Microsoft.Identity.Web/MSAL.NET packages
* Both implementations use confidential `web` callbacks and keep OAuth tokens out of browser JavaScript
* Multi-tenant mode is disabled by default and rejects tenants outside a configured allowlist
* Provisioning creates no Azure subscription role assignment and commits no credential
* Documentation clearly separates protocol feasibility from production supportability
