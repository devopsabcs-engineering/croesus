---
applyTo: '.copilot-tracking/changes/2026-06-15/app-registration-analysis-changes.md'
---
<!-- markdownlint-disable-file -->
# Implementation Plan: Croesus App Registration Analysis Deliverables

## Overview

Produce the customer-facing analysis that answers whether the Croesus SaaS second non-interactive sign-in is expected OAuth behaviour or a misconfiguration, with an Option A (internal fix) vs Option B (escalate to vendor) recommendation, plus a Croesus escalation packet and gap-closure verification steps.

## Objectives

### User Requirements

* Perform the analysis described in assets/app-registration-analysis.md against the three app-registration .txt files. — Source: user request "do the analysis suggested here ... verify 3 .txt files for the app registrations"
* Verify the three .txt files (dev-dev, dev-prod, prod-prod). — Source: user request "verify 3 .txt files for the app registrations"
* Use the other assets/ documents for additional context where needed. — Source: user request "if needed for additional context can look at other documents in assets folder"

### Derived Objectives

* Deliver a written determination on the second-sign-in behaviour with an Option A vs Option B recommendation. — Derived from: the analysis-intent doc frames a decision the customer must make (research lines covering the decision section).
* Produce a Croesus escalation packet listing the exact questions and evidence to request. — Derived from: research concludes Option B (escalate) must precede an internal fix because authoritative flow evidence is RMS-locked.
* Provide a gap-closure verification step set (owners, admin-consent, tenant IDs, sign-in logs). — Derived from: research lists follow-on items that the application-object exports cannot answer.

## Context Summary

### Project Files

* .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md - Primary research: verified comparison matrix, decisive OBO finding, determination, selected approach, alternatives.
* .copilot-tracking/research/subagents/2026-06-15/app-registration-extraction.md - Per-file app-registration extraction (formats, tenants, redirect URIs, credentials, naming hypothesis).
* .copilot-tracking/research/subagents/2026-06-15/sso-ca-oauth-evidence.md - CA advisory PDF findings, RMS-blocked docs, OBO-impossibility evidence, Option A/B reasoning.
* .copilot-tracking/research/subagents/2026-06-15/screenshot-evidence.md - Per-image catalogue of the readable screenshots docx; confirms manifests live in-portal and supplies the prod-prod raw sign-in logs (Token Protection bound/unbound) + AWS egress IPs.
* assets/app-registration-analysis.md - Customer intent/scoping doc (matrix, six verification dimensions, the decision).
* assets/dev-dev.txt - App registration export (appId 713d6ede..., DEV tenant).
* assets/dev-prod.txt - App registration export (appId e3e358ea..., PROD tenant, UAT/dev-named).
* assets/prod-prod.txt - App registration export (appId 92dd40a3..., PROD tenant, clean reference).

### References

* assets/non_prod_sso_conditional_access_report_20260529_180104.pdf - Readable CA advisory; source of the three remediation options.
* assets/croesus_entra_oauth_integration_report_20260529_185237.pdf - RMS-encrypted; authoritative OAuth flow (blocked).
* assets/PROD.docx - RMS-encrypted; prod reference behaviour (blocked).
* assets/Screenshots of app registrations.docx - READABLE; 16 portal/sign-in-log screenshots that complement the manifests and supply the prod reference behaviour + raw sign-in logs the RMS-locked docs were expected to hold.
* [Microsoft Entra OBO flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow) - Confirms OBO requires confidential client + exposed API scope.
* [Cross-tenant access settings](https://learn.microsoft.com/entra/external-id/cross-tenant-access-settings-b2b-collaboration) - Trust compliant devices (PDF Option 2).

### Standards References

* c:\Users\emknafo\.vscode\extensions\ise-hve-essentials.hve-core-3.2.2\.github\instructions\hve-core\markdown.instructions.md — Markdown authoring conventions for .md deliverables.
* c:\Users\emknafo\.vscode\extensions\ise-hve-essentials.hve-core-3.2.2\.github\instructions\hve-core\writing-style.instructions.md — Voice/tone/language conventions.

## Implementation Checklist

### [ ] Implementation Phase 1: Core Analysis Findings Deliverable

<!-- parallelizable: false -->

* [ ] Step 1.1: Assemble the verified app-registration matrix and per-file verification section
  * Details: .copilot-tracking/details/2026-06-15/app-registration-analysis-details.md (Lines 12-31)
* [ ] Step 1.2: Reconcile the dev-dev / prod-dev(=dev-prod) / prod-prod matrix and naming against the intent doc's six dimensions
  * Details: .copilot-tracking/details/2026-06-15/app-registration-analysis-details.md (Lines 33-53)
* [ ] Step 1.3: Write the determination (three separated facts) and the Option A vs Option B recommendation with the sequence diagram
  * Details: .copilot-tracking/details/2026-06-15/app-registration-analysis-details.md (Lines 55-76)

### [ ] Implementation Phase 2: Croesus Escalation Packet

<!-- parallelizable: true -->

* [ ] Step 2.1: Draft the vendor escalation packet (exact questions + evidence to request from Croesus)
  * Details: .copilot-tracking/details/2026-06-15/app-registration-analysis-details.md (Lines 82-101)

### [ ] Implementation Phase 3: Gap-Closure Verification Steps

<!-- parallelizable: true -->

* [ ] Step 3.1: Author the verification command set (owners, admin-consent, tenant IDs, sign-in logs) with run guidance
  * Details: .copilot-tracking/details/2026-06-15/app-registration-analysis-details.md (Lines 107-125)

### [ ] Implementation Phase N: Validation

<!-- parallelizable: false -->

* [ ] Step N.1: Run full validation of all deliverable markdown
  * Markdown lint on all created files; verify links resolve and no broken internal references
* [ ] Step N.2: Fix minor validation issues
  * Correct lint warnings and formatting inline
* [ ] Step N.3: Report blocking issues
  * Document any issue needing more research (e.g., RMS-unlocked docs) and provide next steps

## Planning Log

See .copilot-tracking/plans/logs/2026-06-15/app-registration-analysis-log.md for discrepancy tracking, implementation paths considered, and suggested follow-on work.

## Dependencies

* Read access to assets/ and the three research documents.
* Azure CLI (for the verification steps in Phase 3 — author-only; execution is left to the customer/operator).
* Markdown lint tooling for the validation phase.

## Success Criteria

* The deliverable verifies all three .txt files and reconciles the analysis-doc matrix. — Traces to: user requirement "verify 3 .txt files" and the intent doc's matrix.
* The deliverable states a defensible determination (expected vs misconfiguration) with evidence. — Traces to: research determination section.
* An Option A vs Option B recommendation is provided with rationale. — Traces to: research selected approach.
* A Croesus escalation packet and gap-closure verification steps exist. — Traces to: research follow-on items and Option B-first conclusion.
