<!-- markdownlint-disable-file -->
# Planning Log: Croesus Bad-Path Replay Demo

## Discrepancy Log

Current validation found no unresolved discrepancies between the research findings and the Tier 1 implementation plan.

### Unaddressed Research Items

None. The research items that remain unresolved, including the Token Protection support matrix, SPA Graph `User.Read` consent state, and non-prod Conditional Access details, are explicitly outside the Tier 1 plan and are tracked as follow-on work below rather than current implementation gaps.

### Plan Deviations from Research

None. The plan follows the refined research recommendation to implement Tier 1 first, defer Tier 2 to a separately approved replay-lab plan, and avoid claiming that the mock emits literal Token Protection 1008.

## Implementation Paths Considered

### Selected: Tier 1 Safe Negative-Control UX

* Approach: Add a SPA-only button that uses the existing API-audienced token A against Microsoft Graph `/me`, captures Graph's 401 response, and renders that as an expected audience-bound replay rejection.
* Rationale: This is the smallest useful implementation, requires no tenant permissions, does not add a vulnerable endpoint, and directly explains why audience binding matters.
* Evidence: .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md (Lines 222-245)

### IP-01: Tier 2 Backend Graph Token Replay Lab

* Approach: SPA acquires a Graph token, sends it to an API-authenticated replay endpoint, and the API re-presents it to Graph from App Service.
* Trade-offs: Most faithful to real Croesus AWS server replay shape, but intentionally creates a token-forwarding vulnerability, requires SPA Graph consent, CORS POST, gating, redaction tests, and a lab-only deployment path.
* Rejection rationale: Deferred until Tier 1 is complete and a separate replay-lab plan approves the required app-registration and security changes.

### IP-02: SPA Direct Graph Token Demo Only

* Approach: SPA requests Graph `User.Read` and calls Graph directly, showing the browser now holds a replayable Graph token.
* Trade-offs: Lighter than backend replay and no API endpoint needed, but requires SPA Graph consent and does not reproduce the server-side replay aspect of the real app.
* Rejection rationale: Kept as a possible part of Tier 2, not needed for the initial safe contrast.

## Suggested Follow-On Work

Items identified during planning that fall outside current scope.

* WI-01: Plan Tier 2 replay lab — Create a separate plan for the gated backend replay endpoint, consent changes, token redaction tests, and lab deployment environment (high)
  * Source: .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md (Lines 460-494)
  * Dependency: Tier 1 implementation and user approval for intentionally vulnerable lab mode
* WI-02: Confirm Token Protection support matrix — Verify first-party Entra documentation for when 0/1008 is emitted and whether Microsoft Graph resource access can ever show it (medium)
  * Source: .copilot-tracking/research/subagents/2026-06-30/1008-reasoning-rubber-duck.md (Lines 278-288)
  * Dependency: Access to current Microsoft Entra Token Protection documentation
* WI-03: Capture non-prod CA failure details — Run the verification guide queries to capture exact AADSTS code and CA policy name for the blocked real-app replay (medium)
  * Source: .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md (Lines 54-57)
  * Dependency: Access to customer tenant sign-in logs
