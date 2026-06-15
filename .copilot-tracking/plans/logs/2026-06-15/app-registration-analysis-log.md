<!-- markdownlint-disable-file -->
# Planning Log: Croesus App Registration Analysis Deliverables

## Discrepancy Log

Gaps and differences identified between research findings and the implementation plan.

### Unaddressed Research Items

* DR-01: App owners and admin-consent (delegated grant) state are not present in the application-object exports.
  * Source: .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md (Key Discoveries — export limitations)
  * Reason: Out of scope for the static .txt verification; converted into Phase 3 verification commands and noted as a stated limitation in the findings report.
  * Impact: low
* DR-02: The two authoritative evidence documents (croesus_entra_oauth_integration_report PDF and PROD.docx) are Azure RMS/MIP-encrypted and unreadable.
  * Source: .copilot-tracking/research/subagents/2026-06-15/sso-ca-oauth-evidence.md (RMS-blocked docs)
  * Reason: Cannot decrypt; the recommendation is conditioned on Croesus confirming the flow (Phase 2 escalation packet) and on operator sign-in-log capture (Phase 3).
  * Impact: low (DOWNGRADED from medium) — a third customer doc, assets/Screenshots of app registrations.docx, IS readable and supplies the prod reference behaviour and raw sign-in logs those two encrypted docs were expected to hold (see .copilot-tracking/research/subagents/2026-06-15/screenshot-evidence.md, images 11-16). The OAuth integration report PDF would still add the vendor's intended flow definition.

### Plan Deviations from Research

* DD-01: The readable advisory PDF labels the second sign-in an "OBO" flow, but the exports prove no registration can perform a standards-compliant Entra OBO (no secret/cert/exposed API).
  * Research recommends: Treat the second event as vendor-driven token reuse, not a customer-toggleable OBO.
  * Plan implements: The findings report qualifies the PDF's "OBO" terminology and states the export-based evidence as authoritative for what the registrations can/cannot do.
  * Rationale: Avoid propagating an inaccurate flow label into the customer determination.

## Implementation Paths Considered

### Selected: Option B-first, then scoped Option A

* Approach: Escalate to Croesus to confirm the exact flow and AWS egress IPs, then apply the proportionate internal accommodation (preferred: B2B cross-tenant "Trust compliant devices"; alternative: scoped CA trusted-location exception + MFA).
* Rationale: Authoritative flow evidence is RMS-locked; committing to an internal fix before confirming the vendor flow risks weakening Conditional Access for the wrong reason.
* Evidence: .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md (Preferred Approach / Determination sections).

### IP-01: Option A only (internal fix without escalation)

* Approach: Immediately implement a CA trusted-location or B2B trust change for the Croesus AWS IPs.
* Trade-offs: Fastest unblock; but risks loosening CA against an unconfirmed flow and without published AWS IP ranges.
* Rejection rationale: Proceeds without the flow confirmation that determines the proportionate control.

### IP-02: Option B only (escalate, no internal preparation)

* Approach: Hand the question to Croesus and wait.
* Trade-offs: Lowest customer effort; but leaves the customer without a determination, hygiene findings, or verification steps.
* Rejection rationale: The user explicitly asked for the analysis to be performed now.

### IP-03: PDF Option 3 — single-tenant redesign

* Approach: Consolidate to a single Entra tenant to remove the cross-tenant device-compliance gap.
* Trade-offs: Removes the root cause permanently; but is a large architectural change.
* Rejection rationale: Last-resort per the advisory PDF; disproportionate to the immediate question.

## Suggested Follow-On Work

Items identified during planning that fall outside current scope.

* WI-01: Unlock and review the two RMS-encrypted documents (OAuth integration report PDF, PROD.docx). — Obtain decryption rights to confirm the authoritative flow. (high)
  * Source: DR-02
  * Dependency: Customer/label-owner access to the MIP-protected files.
* WI-02: Capture raw Entra sign-in logs for both events (interactive + AWS-IP event). — Provides verbatim AADSTS codes, CA policy name, audience, device-compliance state. (high)
  * Source: research evidence gaps
  * Dependency: Entra admin access to sign-in logs.
  * Status: PARTIALLY SATISFIED — the screenshots doc (images 13-16) supplies the prod-prod SignInLog export: both rows share SessionId 007bc799-6350-25bf-fbe9-ebbcb7093b63 and deviceId 5b4b24f4-4540-46a6-a4cf-4518e81f06c8; the interactive row is Token Protection "bound" (code 0) from Desjardins IP 142.195.80.133, the non-interactive row is "unbound" (code 1008) from AWS IP 3.97.32.113, both ResultType 0 (success). Still missing: verbatim AADSTS failure code + CA policy name for the non-prod (DEV-tenant) denial.
* WI-03: Obtain Croesus's exact OAuth flow definition and published AWS egress IP ranges. — Drives Option A vs Option B selection. (high)
  * Source: Phase 2 escalation packet
  * Dependency: Vendor response.
  * Status: PARTIALLY SATISFIED — confirmed Amazon egress IPs 3.97.32.113 (sign-in log) and 3.99.119.124 (meeting-chat note). Still need Croesus's full published egress range + intended-flow definition.
* WI-04: Pull app owners and admin-consent grants for the three appIds. — Closes DR-01. (medium)
  * Source: DR-01
  * Dependency: Entra directory read access.
* WI-05: Resolve the dev and prod tenant IDs behind the two publisher domains. — Confirms cross-tenant topology in the findings. (low)
  * Source: research naming/tenant reconciliation
  * Dependency: Directory read access.

## Implementation Notes

* All three deliverables created under assets/ (findings report, escalation packet, verification guide) and the changes log written. All plan steps and phases marked complete.
* DD-02 (lint): Section 3.2 of the findings report was initially numbered 4-8 to read as a continuation of the 3.1 table; markdownlint MD029 requires ordered lists to restart at 1. Renumbered 3.2 to 1-5 and updated the two cross-references in the verification guide (#8 -> #5). No semantic change.
* Added a new actionable security finding V1 (Token Protection "unbound"/replay risk) and an explicit Step 4 remediation (enforce a token-binding CA control) — derived directly from the screenshot sign-in logs; surfaced as a customer recommendation.
