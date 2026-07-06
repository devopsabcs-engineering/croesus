<!-- markdownlint-disable-file -->
# Planning Log: Tier 2 Conditional Access 1008 Token-Replay Reproduction & Reversible Demo

## Discrepancy Log

Gaps and differences identified between research findings and the implementation plan.

### Unaddressed Research Items

* DR-01: Config-contract-first rule — every new config value must be a contract row before wiring.
  * Source: .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 600-618)
  * Reason: Addressed in Phase 3 Step 3.2; Phase 1/2 steps that add config reference this dependency.
  * Impact: low
* DR-02: Existing teardown does not revoke Graph grants or reset gates.
  * Status: RESOLVED (this iteration). SPA Graph grant revocation was already addressed in Phase 4 Step 4.4; the previously-open gate-reset residual is now concrete — Step 4.4 resets the live `Demo__EnableReplay` app setting and Step 4.5 verify-clean asserts it (see DR-06).
  * Source: .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 485-510)
  * Reason: Grant revocation (Step 4.4) + live-gate reset (Step 4.4, verify-clean check 4) now cover both halves of the finding.
  * Impact: resolved (was high)
* DR-03: README states Token Protection needs P2; authoritative docs say P1.
  * Source: .copilot-tracking/research/2026-07-06/tier2-ca-1008-replay-reproduction-research.md (Key Discovery 1)
  * Reason: Corrected in Phase 6 Step 6.3.
  * Impact: low (both licenses present)
* DR-04: Enforced (blocking) beat coverage depends on the WI-02 preview support matrix.
  * Source: .copilot-tracking/research/2026-07-06/tier2-ca-1008-replay-reproduction-research.md (Potential Next Research)
  * Reason: Plan uses report-only by default; enforcement beat is an optional commented PATCH (Step 4.2) and reported in Step 7.3, not required for success.
  * Impact: medium
* DR-05: Which resource the customer's real 1008 line targeted (EXO/SPO/Teams vs Graph) is unconfirmed.
  * Source: .copilot-tracking/research/subagents/2026-07-06/token-protection-1008-mechanics.md (Section 7)
  * Reason: Out of implementation scope; surfaced as follow-on WI-03 and in Step 7.3.
  * Impact: medium (affects whether 2b reproduces the exact line or requires vendor escalation)
* DR-06: The live `Demo__EnableReplay=true` app setting (set by the replay-lab job in Phase 5 Step 5.2 via `az webapp config appsettings set`, because deploy is code-only) is not verifiably reset by teardown or asserted by `verify-clean.sh`.
  * Status: RESOLVED (this iteration). Step 4.4 now resets the live gate (`az webapp config appsettings set ... Demo__EnableReplay=false`, guarded on app existence, or deletes the setting); Step 4.5 verify-clean adds check (4) asserting the live setting is `false` or absent (skipped gracefully if the app is gone); Success Criterion 4 enumerates "live `Demo__EnableReplay` reset (or removed)" among the five verify-clean checks. The deployed baseline is Bicep `Demo__EnableReplay='false'` (Step 3.1), so "false OR absent" correctly restores the baseline.
  * Source: user requirement (fully reversible undo → restore exact original state); .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Step 4.4 Lines 368-388; Step 4.5 Lines 390-408); plan Success Criteria.
  * Reason: Teardown (Step 4.4) resets the live setting and verify-clean (Step 4.5 check 4) asserts it; the plan-level success criterion no longer over-claims.
  * Impact: resolved (was high)
* DR-07: The `replay-lab` federated-credential subject (Phase 5 Step 5.1) and the `replay-lab` GitHub environment (Phase 5 Step 5.2) are app-registration / config changes that no teardown step reverses and `verify-clean.sh` does not assert absent.
  * Status: RESOLVED for the high-impact app-registration change (this iteration). Step 4.4 now removes the `replay-lab` FIC (`az ad app federated-credential delete`, resolved by the `replay-lab` subject/name, guarded); Step 4.5 verify-clean adds check (5) asserting no `replay-lab` federated credential on the deploy identity; Success Criterion 4 enumerates "`replay-lab` federated credential removed." This closes the material tenant-side residual (a live token-issuance credential on the deploy identity).
  * Residual (low): the `replay-lab` GitHub *environment* (a repo-side config object, no secrets, no Entra effect) is still not removed by teardown. Recommend either a one-line manual-cleanup note in docs or an explicit "intentionally retained" statement to keep the exact-original-state claim honest.
  * Source: user requirement (reverse "all other tenant / app-registration / config changes"); .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Step 4.4 Lines 368-388; Step 4.5 Lines 390-408).
  * Reason: FIC removal (Step 4.4) + verify-clean check 5 (Step 4.5) reverse the deploy-identity credential; only the benign GitHub environment artifact remains.
  * Impact: low (was high — high-impact FIC now reversed; residual is an empty repo-side environment)
* DR-08: SPA Graph consent revocation leaves already-issued access tokens valid until natural expiry; the "exact original state" caveat is not documented.
  * Status: RESOLVED (this iteration). Step 6.2 now has docs/evidence-narrative.md note that revoking the SPA Graph grant leaves already-issued tokens valid until natural (short) expiry (DR-08); Success Criterion 4 explicitly caveats "Already-issued Graph tokens remain valid until natural expiry (documented, short-lived)." Backed by research (Existing tokens remain valid until expiry; only new tokens lose the scope).
  * Source: .copilot-tracking/research/subagents/2026-07-06/reversible-ca-provisioning-and-obo-gap.md (Section 2.3); .copilot-tracking/details/2026-07-06/tier2-ca-1008-replay-details.md (Step 6.2 Lines 500-520); plan Success Criteria.
  * Reason: The residual-token-validity window is now documented in both the guide and the success criterion; the reversibility narrative is honest.
  * Impact: resolved (was low)

### Plan Deviations from Research

* DD-01: Endpoint gate is absent-when-off (route not mapped) rather than present-but-refusing.
  * Research recommends: gate the endpoint; notes absent-when-off is safer.
  * Plan implements: conditional controller feature-provider so the route is not mapped when `Demo:EnableReplay=false` (Phase 1 Step 1.3).
  * Rationale: minimizes attack surface for a deliberately weak endpoint.
* DD-02: Distinct `ReplayAttempt` telemetry event instead of reusing `OboExchange`.
  * Research recommends: distinct event name for clean evidence queries.
  * Plan implements: new `LogReplayAttempt` (Phase 1 Step 1.4).
  * Rationale: keeps `OboExchange` evidence unambiguous.
* DD-03: SPA requests a Graph scope (breaks the "SPA requests only API_SCOPE" invariant).
  * Research recommends: the Graph-scoped SPA path is required for Variant C.
  * Plan implements: a gated `graphRequest` (Phase 2 Step 2.1), used only behind `VITE_ENABLE_REPLAY_DEMO`; the API path stays API-scope-only.
  * Rationale: intentional and gated; documented in the config contract.
* DD-04: Token protection provisioned via the beta Graph endpoint (`secureSignInSession`).
  * Research recommends: beta endpoint (field is beta-only).
  * Plan implements: `az rest ... /beta/identity/conditionalAccess/policies` (Phase 4 Step 4.2).
  * Rationale: v1.0 and grant controls do not carry the field.

## Implementation Paths Considered

### Selected: Two-part gated Tier 2 (2a replay shape + 2b report-only Token Protection CA policy), fully reversible

* Approach: keep Tier 1 (audience 401) as the deterministic anchor; add a gated server-side replay endpoint (2a) reproducing the vendor shape; add a scoped report-only Token Protection CA policy (2b) to surface the real 1008 telemetry; reverse all tenant changes via a state file + prefix sweep + verify-clean.
* Rationale: satisfies every user request while being honest about where 1008 is emitted, and keeps every change gated OFF and reversible.
* Evidence: .copilot-tracking/research/2026-07-06/tier2-ca-1008-replay-reproduction-research.md (Selected Approach; Key Discoveries 1-5).

### IP-01: Force 1008 on the SPA→API→Graph leg

* Approach: attempt to make the replay endpoint's Graph call emit 1008 directly.
* Trade-offs: would be the most literal single-flow reproduction if it worked.
* Rejection rationale: impossible/overclaiming — Token Protection does not apply to browser clients or to Microsoft Graph as a resource; the binding evaluation never runs for that leg.

### IP-02: API self-acquires the Graph token (no SPA forwarding)

* Approach: the API mints/holds the Graph token itself and replays it.
* Trade-offs: fewer SPA changes.
* Rejection rationale: does not reproduce the customer's shape (the vendor forwards the USER's token from the front channel); README specifies SPA-forwarded.

### IP-03: Fold CA/teardown into existing app-registration scripts

* Approach: no new script files; extend provision/teardown-app-registrations.sh only.
* Trade-offs: fewer files.
* Rejection rationale: separate provision-ca-policy.sh / teardown-ca-policy.sh / verify-clean.sh keep the reversibility surface auditable and independently runnable; a state file deletes the exact created object, avoiding name collisions.

## Suggested Follow-On Work

* WI-01: Enforcement (blocking) beat — flip the CA policy report-only→enabled against a native client hitting EXO/SPO/Teams to demonstrate the actual block, then revert. (medium)
  * Source: research Potential Next Research + Step 4.2 commented PATCH.
  * Dependency: WI-03 (confirm the customer's target resource) + a registered native/device test client.
* WI-02: Reconfirm the Token Protection preview support matrix at implementation time. (low)
  * Source: research Potential Next Research.
  * Dependency: none.
* WI-03: Confirm which resource the customer's real 1008 line targeted; if Graph, open a vendor/support escalation rather than reproduce. (medium)
  * Source: token-protection-1008-mechanics.md Section 7.
  * Dependency: access to the customer's raw sign-in logs.
* WI-04: Investigate a Conditional Access `templateId` for token protection to simplify create + strengthen reversibility. (low)
  * Source: reversible-ca-provisioning-and-obo-gap.md Section 6.
  * Dependency: none.
* WI-05: Confirm `az rest` against `/beta` CA policies is not blocked by tenant Graph-beta governance; else add a Graph PowerShell fallback. (low)
  * Source: reversible-ca-provisioning-and-obo-gap.md Section 6.
  * Dependency: none.
