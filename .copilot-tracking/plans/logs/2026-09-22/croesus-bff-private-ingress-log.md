<!-- markdownlint-disable-file -->
# Planning Log: Croesus BFF Private Ingress and Security Evidence

## Discrepancy Log

Gaps and differences identified between research findings and the implementation plan.

### Unaddressed Research Items

* DR-01: The effective Azure Policy assignment and definition identifiers remain unverified
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 and Remaining Decision Gates
  * Reason: The plan cannot resolve this by design; it is a read-only Azure query that belongs in Phase 1 execution. Until it runs, the design targets a constraint the user stated rather than one the plan proved.
  * Impact: high
* DR-02: Whether the modern app currently has an expired or invalid client credential is not determined
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Stage 2, login restoration
  * Reason: The application-layer 403 masks any authentication failure. No login attempt can be made until private reachability exists, so this cannot be planned around, only sequenced after Phase 2.
  * Impact: medium
* DR-03: The research names Stage 2 login restoration as a distinct deliverable, but the plan has no dedicated phase for it
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Stage 0 to 6 execution table
  * Reason: Login restoration has no predictable code change. It is verification plus a conditional credential fix, so it is folded into Step 5.1 challenge assertions and Step 8.3 blocking-issue reporting.
  * Impact: medium
* DR-04: Stage 5 controlled delegated-user evidence collection is represented only as workflow structure, not as an executable phase
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Stage 5
  * Reason: It requires a human on an approved device and a separate approval. The plan constrains how the result is recorded rather than scheduling the session.
  * Impact: medium
* DR-05: The .NET Framework target choice for the legacy app is not decided in the plan
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 3
  * Reason: The choice between 4.8.1 and 4.8 depends on the underlying Windows version, which Step 1.3 inspects. Deciding it now would repeat the withdrawn unconditional recommendation.
  * Impact: low
* DR-06: Migration of poc/legacy-net452 to a supported target is out of scope
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 3
  * Reason: The immediate goal is restoring reachability and producing evidence. Migration is separate work captured as WI-02.
  * Impact: low
* DR-07: The legacy identity bridge and the direct-ingress bypass control required by H5 have no plan step
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - H5 correction, Scenario 3 legacy authentication ownership, Scenario 1 prevent untrusted private clients bypassing the BFF
  * Reason: Step 1.3 gates only the runtime target. No step proves one authenticated and one forbidden legacy route, defines anti-spoofing or logout semantics for the bridge, or blocks a private client reaching the legacy site directly instead of through the BFF. The plan takes the research fallback of a standalone BFF against an owned API without recording legacy integration as blocked rather than complete, and no step removes the withdrawn promise that the legacy app need not change.
  * Impact: high
* DR-08: No hosting surface or private endpoint is planned for poc/bff-yarp-net10 although Step 4.1 depends on a BFF HTTPS hostname
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 Selected Approach, a separately hosted third BFF adds another endpoint; Scenario 5 Stage 3
  * Reason: Step 2.2 instantiates endpoints for the modern and conditional legacy apps only. Step 4.1 requires signin-oidc and signout-callback-oidc redirect URIs on the normal HTTPS hostname, and Step 7.1 documents the resulting configuration, but no step provisions the site, its endpoint, or the parameter that supplies the hostname.
  * Impact: high
* DR-09: Wrong-audience and insufficient-scope negative tests are not implemented by any phase although Step 6.2 lists audience validation as deterministic CI coverage
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 Required Security Behavior item 3, Scenario 4 audience validation proof row
  * Reason: Step 3.5 records an audience verdict on the evidence surface and Step 6.2 asserts CI covers audience validation, but no step in Phase 3 or Phase 4 creates the owned-API tests that accept the intended audience and reject a wrong audience and an insufficient scope. The CI gate would report on a check nothing builds.
  * Impact: high
* DR-10: Nonce and state mismatch and callback replay negative tests are absent
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 4 nonce and state protection proof row, Scenario 2 Required Security Behavior item 9
  * Reason: Step 3.4 covers expiry, revocation, concurrency, cache outage, and interaction-required, but no step tests a mismatched state, a mismatched nonce, or a replayed callback, and no step separates session-cookie SameSite from the OIDC nonce and correlation cookies or exercises the cross-site response mode.
  * Impact: high
* DR-11: Outbound dependency inventory, approved egress, and the optional separate VNet integration subnet are not planned
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 Network and Deployment Contract, Implementation Surfaces separate outbound integration
  * Reason: Step 3.2 requires a distributed cache and Step 4.1 requires a Key Vault certificate, both of which may be private dependencies, yet Step 1.2 inventories only inbound topology and no step adds VNet integration in a separate delegated subnet or records the required outbound DNS, Entra discovery, token, downstream API, telemetry, and artifact egress.
  * Impact: medium
* DR-12: Evidence log access restriction, retention, and reader controls are not covered
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 4 Correlation and Privacy
  * Reason: Step 3.5 constrains the response body, logs, and telemetry content, but no step restricts log access to an operator or collector identity, bounds retention, or names the permitted readers of the collected evidence.
  * Impact: low
* DR-13: The Step 1.1 command set cannot produce the definition body, effect parameters, or exemptions that its own success criteria require
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 capture assignment, definition, scope, effect, parameters, exemptions
  * Reason: The three listed read-only commands return assignment metadata, non-compliant states, and activity-log entries. They do not retrieve the policy definition body or any exemption, so the recorded criterion asking whether the policy also constrains private endpoints, private DNS, subnets, public IPs, or egress cannot be satisfied as written.
  * Impact: medium
* DR-14: No step creates the BFF test project that Phase 3, Phase 6, and Phase 8 validation all depend on
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 Required Security Behavior items 1 to 9, Scenario 4 proof table, H10 required missing evidence blocks acceptance
  * Reason: Step 3.1 creates only poc/bff-yarp-net10/Croesus.BffYarp.csproj. Steps 3.2 through 3.5 assert tests as success criteria, and the new Step 3.6 adds poc/bff-yarp-net10/Tests/AudienceAndScopeTests.cs and poc/bff-yarp-net10/Tests/ProtocolNegativeTests.cs, but no step's Files list creates a test project. Step 3.8 and Step 8.1 both invoke dotnet test against "the new BFF test project" that nothing defines, and Step 6.2 gates required CI evidence on those same tests. The test sources also fall inside the web project's default compilation glob, so absent an explicit project and exclusion they would compile into the shipped application. This reproduces the DR-09 failure mode at a different layer: a gate reporting on a check nothing builds.
  * Impact: high
* DR-15: The Step 1.1 narrative miscounts its own command set
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 capture assignment, definition, scope, effect, parameters, exemptions
  * Reason: The DR-13 remediation added three commands, bringing the block to six, but the sentence beneath it still reads "All five commands are read-only". The read-only property holds for all six commands; only the stated count is wrong.
  * Impact: low
* DR-16: The DR-14 remediation changed the criteria and the checklist title but never added the test project to any Files list, so the artefact the criteria gate on is still created by nothing
  * Source: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 Required Security Behavior items 1 to 9, Scenario 4 proof table, H10 required missing evidence blocks acceptance
  * Reason: Details Step 3.1 (lines 312 to 349) lists exactly three files: Croesus.BffYarp.csproj, Program.cs, and appsettings.json. Neither poc/bff-yarp-net10/Tests/Croesus.BffYarp.Tests.csproj nor any solution file appears in that list, anywhere else in the details file, or anywhere in the plan. Only the two new success criteria were added, asserting that dotnet test discovers the test project and that the web project excludes the Tests directory from its compilation glob, and the plan checklist title was changed to "Scaffold the project and test project" while the details heading still reads "Scaffold the project". Step 3.6 declares its two test sources belong to "the test project created in Step 3.1", Step 3.8 and Step 8.1 both invoke dotnet test against it, and Step 6.2 gates required CI evidence on those tests. Success criteria describe an outcome; Files lists create artefacts. Moving the claim from one to the other leaves the original DR-14 failure mode intact: a gate reporting on a check nothing builds.
  * Impact: high

### Resolutions Applied After Validation

The following entries were raised by plan validation and have been closed by plan edits. They are retained for traceability rather than deleted.

* DR-07: Resolved. Step 1.4 now decides legacy identity bridge scope, the bridge mechanism, anti-spoofing, logout semantics, and the bypass-prevention requirement. Step 3.7 either implements the bridge with one authenticated and one forbidden route, or records legacy integration as blocked with its unblocking dependency. A success criterion in the plan now binds the outcome.
* DR-08: Resolved. Step 2.4 adds an optional reference BFF site and private endpoint behind a deployBffSite parameter defaulted to false, and supplies the hostname parameter Step 4.1 consumes. Step 4.1 is executable in both parameter states.
* DR-09: Resolved. Step 3.6 builds the wrong-audience and insufficient-scope rejection tests that Step 6.2 gates on.
* DR-10: Resolved. Step 3.6 adds state mismatch, nonce mismatch, and callback replay tests, and asserts the OIDC nonce and correlation cookies separately from the session cookie.
* DR-11: Partially resolved. Step 1.2 now records the outbound dependency inventory, approved egress, and whether a separate delegated VNet integration subnet is required. Implementing that subnet remains WI-08.
* DR-12: Resolved. Step 6.2 now requires evidence artefact access to be restricted to the named operator or collector identity with a stated retention bound and named readers.
* DR-13: Resolved. Step 1.1 now includes az policy definition show, az policy set-definition show, and az policy exemption list. All remain read-only.
* DD-12: Resolved. The absent .github/copilot-instructions.md reference is removed from Standards References; assets/croesus-3way-session-findings.md replaces it as an existing convention source.
* Parallel marker contradiction: Resolved. The plan now states execution waves explicitly. Phase 1 alone, Phases 2 to 5 as wave 1, Phases 6 and 7 as wave 2, Phase 8 last.
* Shared changes file collision: Resolved. Each phase appends only under its own uniquely titled section of the changes file and never rewrites another phase's section.
* Anchor inaccuracy: Resolved. Details Step 3.1 now cites poc/modern-net10/Program.cs lines 97 to 118 for the forwarded header configuration, separately from lines 17 to 57.
* Forwarded-header trust: Resolved. Step 3.1 now requires re-deriving KnownProxies and KnownNetworks against the private topology rather than inheriting the existing guard.

### Resolutions Applied After Second Validation

* DR-14: Resolved. Step 3.1 now creates poc/bff-yarp-net10/Tests/Croesus.BffYarp.Tests.csproj and a solution file covering both projects, and its success criteria require dotnet test to discover the project and the web project to exclude the Tests directory from its compilation glob. Step 3.6 states that its test sources belong to that project.
* DR-15: Resolved. Step 1.1 now reads six commands rather than five.
* DD-13: Resolved. The details file marks Phase 6 parallelizable true with a wave 2 note, matching the plan.
* DD-14: Resolved. Step 1.2 now records the BFF hostname value, and Step 2.4 and Step 4.1 each read it from Phase 1. Phase 2 and Phase 4 have no data dependency on each other, so the wave 1 grouping holds.
* DD-15: Resolved. Step 7.1 now depends on Phases 2 through 5, matching its phase preamble.

### Resolutions Applied After Third Validation

* DR-16: Resolved. The Step 3.1 Files list now creates poc/bff-yarp-net10/Tests/Croesus.BffYarp.Tests.csproj with a project reference and pinned test package versions, plus poc/bff-yarp-net10/Croesus.BffYarp.slnx covering both projects, and the web project entry carries the explicit Tests directory glob exclusion. The details heading now matches the plan checklist title. The earlier fix changed only success criteria; artefacts belong in a Files list.
* DD-16: Resolved. Step 2.4 now sets the hostname in main.bicepparam from the value recorded in Step 1.2, and a required-behaviour bullet binds the provisioned hostname to that recorded value so the Phase 2 and Phase 4 decoupling is enforced rather than asserted.
* Residual anchor: the Step 3.1 carry-forward list now cites lines 97 to 118 for UseForwardedHeaders instead of attributing it to lines 17 to 57.

### Plan Deviations from Research

* DD-01: Public ingress remediation reversed
  * Research recommends: publicNetworkAccess stays Disabled with private endpoints added
  * Plan implements: the same, and additionally pins Disabled in Bicep so a redeploy cannot reintroduce public ingress
  * Rationale: The withdrawn plan's az webapp update public-enable command would fail against the policy and would misrepresent the target state if left in source
* DD-02: Token custody mechanism
  * Research recommends: library-owned server-side token caching plus a server-side session ticket store
  * Plan implements: the same, with an explicit sentinel-value test asserting no token appears in Set-Cookie
  * Rationale: A configuration flag alone is not observable evidence; the test makes the property verifiable in CI
* DD-03: Proxy boundary depth
  * Research recommends: protect mutations, constrain destinations, strip incoming credentials, test leakage
  * Plan implements: the same requirements as three separate failing-by-default tests rather than as review checklist items
  * Rationale: The earlier forwarding sketch passed review precisely because nothing tested it
* DD-04: On-Behalf-Of evidence
  * Research recommends: record sanitized grant-operation evidence and corroborate rather than infer
  * Plan implements: removal of audience-count and token-identifier comparisons from both the evidence surface and the KQL file, with no OBO claim unless that hop is exercised
  * Rationale: Two distinct audiences are the expected result of any two-resource flow and prove nothing about the grant type
* DD-05: Graph token inspection
  * Research recommends: treat Microsoft Graph tokens as opaque
  * Plan implements: an explicit prohibition on decoding any Graph token claim in the evidence projection
  * Rationale: Graph tokens are not guaranteed to be parseable by clients, so any claim read from one is unreliable evidence
* DD-06: Optional claims placement
  * Research recommends: configure access token claims on the resource registration
  * Plan implements: optional access token claims on the API registration only, with a check that none exist on the BFF client
  * Rationale: Optional claims on a client registration do not affect the access token a resource receives
* DD-07: Group claims
  * Research recommends: retain group claims where authorization needs them
  * Plan implements: withdrawal of the earlier groupMembershipClaims None recommendation
  * Rationale: Removing group claims to simplify evidence would silently change authorization behaviour
* DD-08: Correlation strategy
  * Research recommends: map per-operation identifiers and verify tenant service principal object identifiers
  * Plan implements: parameterized queries keyed on a run identifier and observed operation identifiers, with no assumed universal correlation join
  * Rationale: A single correlation identifier does not reliably span sign-in, token acquisition, and downstream calls
* DD-09: Verification command hygiene
  * Research recommends: inspect headers in memory and emit only allowlisted verdicts
  * Plan implements: the same, plus a distinguishable failure message per failure class
  * Rationale: A single generic failure cannot separate DNS, routing, authorization, and challenge-shape problems, which is how the original 403 was misdiagnosed
* DD-10: Deployment runner
  * Research recommends: use a private-connected deployment runner
  * Plan implements: hosted validation retained, deployment and probes moved to the connected runner, with an explicit prohibition on enabling basic publishing credentials as a workaround
  * Rationale: The convenient workaround is the insecure one, so it is named and blocked rather than left implicit
* DD-11: Evidence gating
  * Research recommends: separate CI from controlled user tests; missing required evidence blocks acceptance
  * Plan implements: an evidence job with explicit pass, fail, or not-executed verdicts, and a bounded log ingestion deadline that reports unavailable rather than pass
  * Rationale: The repository already contains an optional-skip test convention that must not be inherited by required gates
* DD-12: Standards reference to an absent file
  * Research recommends: the previously cited .github/copilot-instructions.md was absent during review and is not represented as loaded
  * Plan implements: .github/copilot-instructions.md listed under Context Summary Standards References as repository conventions
  * Rationale: Unjustified divergence. The file is not present in the workspace, so the plan represents a convention source that an implementer cannot open. Either the reference is removed or the conventions it stands for are named from a file that exists.
* DD-13: Phase 6 parallelization marker disagrees between the plan and the details file
  * Research recommends: no research position; this is a plan-internal consistency requirement created by the new execution-wave statement
  * Plan implements: the plan marks Phase 6 parallelizable true and places it in wave 2 alongside Phase 7, while the details file marks the same phase parallelizable false and states it edits the shared workflow file and depends on the outputs of Phases 2 through 5
  * Rationale: Unjustified divergence. The wave statement added to close the earlier parallel-marker contradiction was applied to the plan only, so the two files now instruct an implementer differently about whether Phase 6 may run concurrently with Phase 7. One marker must change to match the other.
* DD-14: Step 4.1 consumes an artefact produced by Step 2.4 although both phases are declared concurrent in wave 1
  * Research recommends: Scenario 1 treats a separately hosted BFF as additional endpoint work, and Scenario 2 requires the registration to carry real redirect URIs on a normal HTTPS hostname
  * Plan implements: Step 2.4 in Phase 2 creates the deployBffSite parameter and the hostname value, and Step 4.1 in Phase 4 states the redirect hostname comes from the parameter established in Step 2.4, yet Step 4.1 lists only Implementation Phase 1 completion as its dependency and the wave statement runs Phases 2 and 4 concurrently
  * Rationale: Unjustified divergence. The DR-08 remediation introduced a cross-phase data dependency inside wave 1 without updating the Step 4.1 dependency list or the wave grouping, so Phase 4 may begin before the parameter it reads exists. Either Step 4.1 depends on Step 2.4, or Phase 4 moves out of wave 1, or the hostname source is declared independently of Phase 2.
* DD-15: Step 7.1 dependency list is narrower than its own phase preamble and its content requirements
  * Research recommends: documentation must state the private access prerequisites, the browser and runner requirements, and the legacy readiness gate, all of which are produced by Phases 1, 2, and 5
  * Plan implements: the Phase 7 preamble states the phase runs after Phases 2 through 5, while Step 7.1 lists only Implementation Phase 3 and Phase 4 completion as its dependencies
  * Rationale: Minor divergence. Read literally, the step-level dependency permits Phase 7 to begin before the phases that produce the private topology and verification facts it documents.
* DD-16: The Step 2.4 file annotation still routes the BFF hostname from Phase 2 to Phase 4, contradicting the single Phase 1 source the DD-14 resolution established
  * Research recommends: no research position; this is a plan-internal consistency requirement created by the DD-14 resolution
  * Plan implements: Step 1.2 records the BFF hostname, Step 4.1 states it reads that value and "does not depend on Phase 2", and the wave statement declares no data dependency inside wave 1, yet the Step 2.4 Files list still specifies that infra/poc/main.bicepparam adds "a bffHostName output or parameter consumed by the registration scripts", which is the Phase 4 script
  * Rationale: Minor divergence. The details file now names two sources for one value and gives no rule for which wins. Related, and the reason this is recorded rather than dismissed as an editing remnant: no success criterion in Step 2.4 requires the hostname the deployBffSite true branch actually provisions to equal the hostname recorded in Step 1.2, so the decoupling is asserted rather than enforced. The exposure is bounded because deployBffSite defaults to false and WI-09 already carries reconciliation of the registration redirect URIs against the provisioned hostname, which is why this is minor rather than major. Either the Files annotation is corrected to name Step 1.2 as the source, or Step 2.4 gains a criterion binding the provisioned hostname to the recorded one.

## Implementation Paths Considered

### Selected: Per-app private endpoints with a connected deployment runner

* Approach: Keep publicNetworkAccess Disabled, add one private endpoint per approved app on the sites subresource, integrate the privatelink.azurewebsites.net zone for both application and scm hostnames, and move package deployment to a runner with a route into the VNet
* Rationale: It satisfies the policy constraint without an exemption request, works on the existing Basic B1 plan, and preserves the normal HTTPS hostnames so no OIDC redirect URI changes
* Evidence: .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - Basic B1 supports private endpoints, per-app endpoint requirement, DNS and SCM record requirements

### IP-01: Re-enable public network access with IP access restrictions

* Approach: Set publicNetworkAccess to Enabled and constrain access with App Service IP rules
* Trade-offs: Fastest path to a working demo and requires no network ownership. It contradicts the stated policy, would be denied at the management plane, and IP rules are not evaluated for private endpoint traffic so the two models do not compose as a fallback
* Rejection rationale: The user stated the policy prohibits public access, and this is the recommendation already withdrawn during the rubber-duck review

### IP-02: Request a policy exemption for the POC resource group

* Approach: Obtain a scoped exemption so the existing public configuration can be restored unchanged
* Trade-offs: No infrastructure work and no runner change. It requires governance approval on an unknown timeline, produces a demo environment that does not represent the customer's real constraint, and weakens the security story the POC is meant to tell
* Rejection rationale: The engagement goal is a credible security reference, and demonstrating the pattern under the real constraint is worth more than restoring the old shape

### IP-03: Redeploy the POC onto Azure Container Apps or a private App Service Environment

* Approach: Move both apps onto a platform with private ingress as its default posture
* Trade-offs: Cleaner long-term isolation. It changes the hosting comparison the POC exists to make, invalidates the classic .NET Framework legacy app path entirely, and costs substantially more than a Basic B1 plan
* Rejection rationale: The legacy versus modern comparison is the deliverable, and an App Service Environment is not justified when Basic B1 already supports private endpoints

### IP-05: Host the reference BFF unconditionally as a third App Service site

* Approach: Always provision a third site with its own private endpoint and DNS records rather than gating it behind a parameter
* Trade-offs: Removes the hostname ambiguity entirely and makes the registration unambiguously real. It adds a third site to a Basic B1 plan before the hosting decision from Step 1.2 is known, and commits network ownership work that may not be authorized
* Rejection rationale: The parameterized form in Step 2.4 gives the same executable registration path while letting Step 1.2 decide whether hosting is in scope

### IP-04: Build the reference BFF by retrofitting poc/modern-net10 in place

* Approach: Add YARP, server-side token caching, and the proxy boundary directly to the existing modern app
* Trade-offs: Less duplicated configuration. It would entangle the before-and-after comparison, make the modern app's current behaviour unreproducible, and prevent parallel work because Phase 3 would then share files with the ingress and evidence phases
* Rejection rationale: The BFF is additive by design; keeping it in a new directory preserves the comparison and keeps four phases parallelizable

## Suggested Follow-On Work

Items identified during planning that fall outside current scope.

* WI-01: Obtain and document the governance decision on private endpoint and private DNS permissibility under the effective policy (high)
  * Source: DR-01, Step 1.1
  * Dependency: Phase 1 policy evidence capture
* WI-02: Migrate poc/legacy-net452 to a supported .NET Framework target chosen from the host Windows version (medium)
  * Source: DR-05, DR-06, Step 1.3
  * Dependency: Step 1.3 legacy readiness verdict
* WI-03: Schedule and run the controlled delegated-user evidence session on an approved browser and device (high)
  * Source: DR-04, Step 6.2
  * Dependency: Phase 2 and Phase 6 completion, plus customer approval for the session
* WI-04: Resolve the modern app client credential if login fails once private reachability exists (medium)
  * Source: DR-02, DR-03
  * Dependency: Phase 2 completion and a first successful challenge
* WI-05: Refresh the Token Protection preview wording across all customer-facing assets, not only docs/evidence-narrative.md (low)
  * Source: Step 7.1
  * Dependency: none
* WI-06: Decide the long-term credential model for the BFF registration, including whether a managed identity federated credential replaces the Key Vault certificate (low)
  * Source: Step 4.1
  * Dependency: Phase 4 completion
* WI-07: Define the frontend framework and hosting model for any production successor to the reference BFF (low)
  * Source: Step 3.1, deliberately excluded from scope
  * Dependency: acceptance of the reference pattern
* WI-08: Add VNet integration in a separate delegated subnet for outbound access to the distributed cache, Key Vault, and any private downstream dependency (medium)
  * Source: DR-11, Step 1.2 outbound dependency inventory
  * Dependency: Step 1.2 inventory and the Step 3.2 cache backing-store decision
* WI-09: Host the reference BFF by setting deployBffSite to true once the hosting decision is recorded, and update the registration redirect URIs to the provisioned hostname (medium)
  * Source: DR-08, Step 2.4
  * Dependency: Step 1.2 hosting verdict and Phase 2 completion
