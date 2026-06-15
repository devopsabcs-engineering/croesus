<!-- markdownlint-disable-file -->
# Implementation Details: Croesus App Registration Analysis Deliverables

## Context Reference

Sources: .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md; .copilot-tracking/research/subagents/2026-06-15/app-registration-extraction.md; .copilot-tracking/research/subagents/2026-06-15/sso-ca-oauth-evidence.md

## Implementation Phase 1: Core Analysis Findings Deliverable

<!-- parallelizable: false -->

### Step 1.1: Assemble the verified app-registration matrix and per-file verification section

Create the deliverable report and populate the verification section. Copy the verified comparison table and per-file facts from the research document verbatim (do not re-derive). Include: format (Graph application object), tenant per publisherDomain, redirect URIs, credentials (none), permissions (Graph User.Read delegated), audience (AzureADMyOrg), implicit-grant settings, SP-lock.

Files:
* assets/app-registration-analysis-findings.md - New customer-facing analysis report (create); section "1. App Registration Verification".

Discrepancy references:
* Addresses DR-01 (owners/admin-consent absent — note as a stated limitation, do not block).

Success criteria:
* All three .txt files are characterized with the verified comparison table reproduced.
* Each file's format, tenant, redirect URIs, credentials, permissions, audience, and implicit-grant state are stated.

Context references:
* .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md (Lines covering "Verified comparison of the three exports") - source table.
* .copilot-tracking/research/subagents/2026-06-15/app-registration-extraction.md (Lines covering "Per-file extraction") - per-file facts.

Dependencies:
* Research documents available.

### Step 1.2: Reconcile the matrix and naming against the intent doc's six dimensions

Add a section that walks the intent doc's six verification dimensions and the dev-dev / prod-dev(=dev-prod) / prod-prod matrix, noting the naming reconciliation (doc's prod-dev == file dev-prod) and the confirmed cross-environment mismatch (UAT/dev-named registration in the prod tenant). Flag dev hygiene gaps (implicit ID token, SiteMinder redirect).

Files:
* assets/app-registration-analysis-findings.md - Add section "2. Matrix and Naming Reconciliation" and "3. Configuration Hygiene Observations".

Discrepancy references:
* Addresses DD-01 (advisory PDF "OBO" label vs the export evidence — qualify it here).

Success criteria:
* The six dimensions from the intent doc are each addressed.
* Naming reconciliation and the cross-environment mismatch are documented.
* Dev hygiene gaps are listed as non-causal but worth cleaning.

Context references:
* assets/app-registration-analysis.md (Lines 17-150) - matrix + six dimensions.
* .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md (Lines covering "Naming hypothesis" and "Configuration / security observations").

Dependencies:
* Step 1.1 completion.

### Step 1.3: Write the determination and the Option A vs Option B recommendation

Add the determination (three separated facts: vendor-driven second event; CA block correct-by-design; cannot be a standards OBO given no secret/cert/exposed-API). State the bottom line and the Option B-first → scoped Option A recommendation. Include the mermaid sequence diagram and the considered-alternatives list. Clearly mark the RMS-blocked evidence as the remaining certainty gap.

Files:
* assets/app-registration-analysis-findings.md - Add sections "4. Determination", "5. Recommendation (Option A vs Option B)", "6. Evidence Gaps and Alternatives".

Discrepancy references:
* Addresses DR-02 (RMS-locked authoritative flow — recommendation is conditioned on Croesus confirmation).

Success criteria:
* The determination states expected-vs-misconfiguration with the three separated facts.
* Option B-first then scoped Option A (B2B trust preferred; CA exception alternative) is recommended with rationale.
* The sequence diagram and rejected alternatives are included.
* The RMS-blocked gap is stated as the certainty limit.

Context references:
* .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md (Lines covering "Determination", "Preferred Approach", mermaid diagram, "Considered Alternatives").
* .copilot-tracking/research/subagents/2026-06-15/sso-ca-oauth-evidence.md (Key Discovery 1 — OBO impossibility).

Dependencies:
* Step 1.2 completion.

## Implementation Phase 2: Croesus Escalation Packet

<!-- parallelizable: true -->

### Step 2.1: Draft the vendor escalation packet

Create a standalone escalation packet listing the exact questions to put to Croesus and the evidence to request: exact OAuth/OIDC flow (true OBO vs server-side token replay), whether their backend is a confidential client and against which app/scope, the audience of the reused token, and published AWS egress IP ranges. Include a short framing paragraph (CA is correct-by-design; we need their flow definition to choose the proportionate internal accommodation).

Files:
* assets/croesus-escalation-packet.md - New escalation packet (create).

Discrepancy references:
* Addresses DR-02 (authoritative flow locked) by converting it into concrete vendor asks.

Success criteria:
* The packet lists the flow, credential, audience, and AWS IP-range questions.
* The framing makes clear the CA policy is not being called broken.

Context references:
* .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md (Lines covering "Preferred Approach" escalation bullets and "Follow-on Questions").
* .copilot-tracking/research/subagents/2026-06-15/sso-ca-oauth-evidence.md (Option A vs Option B reasoning).

Dependencies:
* Research documents available (independent of Phase 1).

## Implementation Phase 3: Gap-Closure Verification Steps

<!-- parallelizable: true -->

### Step 3.1: Author the verification command set

Create a verification guide with the Azure CLI commands to close the gaps the exports cannot answer: confirm delegated User.Read + admin-consent grants, pull app owners, resolve the dev/prod tenant IDs behind the two publisher domains, and the Entra sign-in log query (capture AADSTS code, CA policy name, Client App, Resource/audience, Source IP, Device compliance) for both events. Mark these as operator-run; do not execute them as part of authoring.

Files:
* assets/app-registration-verification.md - New verification guide (create) with fenced command blocks.

Discrepancy references:
* Addresses DR-01 (owners/admin-consent absent) and the sign-in-log evidence gap.

Success criteria:
* Commands for owners, admin-consent, tenant-ID resolution, and sign-in logs are present.
* Each command block notes what gap it closes and that it is operator-run.

Context references:
* .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md (Lines covering "Implementation Details" verification queries and "Potential Next Research").

Dependencies:
* Research documents available (independent of Phases 1 and 2).

## Implementation Phase N: Validation

<!-- parallelizable: false -->

### Step N.1: Run full validation of all deliverable markdown

Lint and review all created markdown deliverables; verify internal references and external links resolve.

Validation commands:
* Markdown lint over assets/app-registration-analysis-findings.md, assets/croesus-escalation-packet.md, assets/app-registration-verification.md - markdown structure.

### Step N.2: Fix minor validation issues

Correct lint warnings, heading levels, table formatting, and link issues inline.

### Step N.3: Report blocking issues

If a finding requires the RMS-unlocked documents or live tenant access, document it and provide next steps rather than fabricating evidence.

## Dependencies

* Read access to assets/ and the three research documents.
* Markdown lint tooling.

## Success Criteria

* Three deliverables exist (findings report, escalation packet, verification guide) that fully address the user request and trace to research.
