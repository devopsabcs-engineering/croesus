<!-- markdownlint-disable-file -->
# Planning Log: Croesus BFF Private Ingress and Security Evidence

## Discrepancy Log

### Findings From Implementation Phase 1

* DD-17: The plan's founding premise is falsified by tenant evidence (critical)
  * Plan specifies: public network access stays disabled because Azure Policy prohibits enabling it
  * Implementation found: no policy assignment at subscription scope or tenant root management group scope enforces publicNetworkAccess on Microsoft.Web/sites. The enforced deny initiative has no Microsoft.Web member. The enforced modify initiative covers AI Foundry Hub, Storage, Key Vault, Cosmos DB, and Azure SQL, with no App Service equivalent. Exemption list is empty.
  * Rationale: recorded rather than worked around, because designing against an imagined constraint is the failure mode the research rewrite was meant to eliminate
  * Consequence: private ingress is an elective architecture, not a compliance obligation. Re-enabling public ingress is not policy-blocked.
  * Resolves DR-01 by falsification rather than by confirmation
* DD-18: Implementation Phase 2 is blocked on an unanticipated prerequisite (critical)
  * Plan specifies: add per-app private endpoints to the existing App Service plan, on the research finding that Basic B1 supports them
  * Implementation found: the deployed plan is F1 Free, which supports neither private endpoints nor VNet integration. The B1 research finding is correct; the deployed plan is simply not B1.
  * Secondary finding: infra/poc/main.bicep declares B1, so the deployed resources diverge from the template in this repository
  * Consequence: every Step 2.2 through Step 2.4 endpoint action is unreachable until the tier and the template divergence are resolved
  * Resolution (2026-09-22): the user disclosed that an external process degrades the plan to F1 on roughly a 24 hour cycle. This converts the finding from a one-time blocker into a permanent constraint, because a private endpoint cannot survive a tier that repeatedly drops below B1. Resolved by ID-01 Option C, which adopts public ingress and makes the tier an explicit, self-diagnosing template parameter.
* DD-19: No private network path exists in the subscription (high)
  * Plan specifies: Step 1.2 selects a browser path and a deployment runner path from existing approved infrastructure
  * Implementation found: no privatelink.azurewebsites.net zone, no VPN or ExpressRoute gateway, no Bastion, no peering. croesus-vnet in rg-croesus is shaped for this purpose but sits in Canada Central while the POC apps are in Canada East, and nothing connects to it.
  * Consequence: both the browser path and the runner path are recorded as undetermined rather than selected
  * Resolution (2026-09-22): no longer blocking under ID-01 Option C. Public ingress needs neither path, and the hosted CI runner is sufficient. The finding is retained because the production successor will have to solve it.
* DD-20: Step 1.3 returned blocked and Step 1.4 returned out of scope (medium)
  * Both are legitimate recorded branches rather than deviations, captured here so the downstream effect is traceable
* DD-21: Phase 1 recorded the wrong hostname as the reference BFF origin (high)
  * Plan specifies: Step 1.2 records a hostname that Step 4.1 later registers redirect URIs against, so the two phases stay independent
  * Implementation recorded: the existing croesus-bff-a3v24wppuvd34-modern hostname, which is the modern comparison app, not a new site
  * Consequence: Phase 4 inherited the error and defaulted BFF_BASE_URI to that host. The modern app already claims /signin-oidc through modernCallbackUri at infra/poc/main.bicep line 45, so two registration objects would have claimed one identical redirect URI.
  * Correction: the reference BFF hostname is croesus-bff-a3v24wppuvd34-bff. Fixed in scripts/provision-app-registrations.sh and in the Phase 1 record in the changes file. Step 2.4 now states the constraint explicitly so it cannot recur.
  * Root cause: a recording error in one phase propagated silently to a later phase through the changes file, because the later phase treated the recorded value as verified input.

* DD-22: Two phases silently disabled a security test suite outside their file scope (high)
  * Plan specifies: phases are scoped to disjoint file sets so they can run in parallel without interfering
  * Implementation found: `scripts/test-classic-net-bff-deployment-entra-static.ps1` parses `infra/poc/main.bicep` by literal string anchors and asserts literal values from `poc/legacy-net452/web.config`. Phase 2 renamed the description used as a parse anchor; Phase 9 changed the framework literals. Neither file was in the other phase's scope, and neither change was a defect in itself.
  * Consequence: the suite threw at its first anchor from the moment Phase 2 landed, which disabled every credential-safety assertion after that line while still failing loudly enough to be mistaken for an unrelated infra problem. CI step `.github/workflows/classic-net-bff-poc.yml` line 190 would have failed for the wrong reason.
  * Correction: the orchestrator repaired all three. The modern-resource block now terminates at the new `bffApp` resource rather than at the outputs, which is also more robust than the description anchor it replaced. Legacy assertions now expect 4.8. Suite verified at exit 0.
  * Root cause: disjoint *write* scopes are not disjoint *blast radii*. A test that couples to another file by literal string makes any edit to that file a potential test break, and file-scope isolation cannot detect it. Caught only because Phase 5 captured a baseline before its own edits and Phase 9 evaluated the assertions in isolation rather than assuming.
  * Step 3.7 executes its blocked branch; no artefact may claim the BFF fronts the legacy app
  * The two-app scope is reduced by a recorded gate outcome, not silently

## Discrepancy Log (Planning Phase)

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

### Raised During Implementation

* WI-10: Re-ratify the ingress architecture before Phase 2 resumes (critical)
  * Source: Phase 1, Step 1.1
  * Private ingress is elective, not compelled. The engagement owner should confirm intent now that the compliance rationale is gone.
  * Dependency: none; this is the ID-01 decision
* WI-11: Resolve the Bicep and deployment divergence on the plan SKU (high)
  * Source: Phase 1, Step 1.2
  * infra/poc/main.bicep declares B1 but the deployed plan is F1. The deployed resources may not have come from this template, which undermines the template as a source of truth.
  * Dependency: none
* WI-12: Name the region decision for any private endpoint (high)
  * Source: Phase 1, Step 1.2
  * croesus-vnet is in Canada Central; the POC apps are in Canada East. Either a cross-region endpoint or a new Canada East VNet must be chosen.
  * Dependency: WI-10
* WI-13: Add control-removal pairs for the two unpaired evidence assertions (low)
  * Source: Phase 3, Step 3.5
  * `Evidence_SetsCacheControlNoStore` and `Evidence_RequiresAnAuthenticatedSession` assert observed behaviour without a paired removal test, so their non-vacuity is unproven.
  * Dependency: none
* WI-14: Extend teardown-app-registrations.sh to the new BFF registration (medium)
  * Source: Phase 4, Step 4.1
  * Teardown does not consume the new `bffAppId` state key and does not delete the BFF certificate, so repeated provision and teardown cycles will leak both.
  * Dependency: none
* WI-15: Add configuration-contract rows for the new BFF keys (low)
  * Source: Phase 4, Step 4.1
  * docs/configuration-contract.md has no row for `croesus-bff-cert` or `BFF_CLIENT_ID`. Phase 7 Step 7.1 already owns this file and can absorb it.
  * Dependency: Phase 7
* WI-16: Automate or schedule recovery from the F1 tier degradation cycle (medium)
  * Source: ID-01, Q1 answer
  * An external process returns the App Service plan to F1 on roughly a 24 hour cycle, which takes the demo down until someone redeploys. Step 2.3 makes the condition diagnosable and Step 5.1 makes a 403 attributable, but neither prevents the outage. A scheduled scale-up check, or identification and exemption of the degrading process, would.
  * Dependency: Phase 2 and Phase 5 completion
* WI-17: Build the private ingress successor in an environment that can sustain it (medium)
  * Source: ID-01, Option C
  * The private design survives as an opt-in module under Step 2.2 but is never exercised. It needs a plan that stays at B1 or better and a same-region VNet with a linked privatelink.azurewebsites.net zone, neither of which the POC subscription has. Supersedes WI-01 and WI-12 as the umbrella item.
  * Dependency: a successor environment
* WI-18: Corroborate the unconnected-client assertion rather than trusting the caller (low)
  * Source: Phase 5, Step 5.2
  * `verify-ingress.ps1` accepts `-UnconnectedClient` as an operator assertion. A private-address heuristic was considered and rejected because an RFC 1918 source address is not proof of being off the VNet, and a false negative would be worse than the current honest `not-executed`. A real corroboration needs an out-of-band signal such as a known-external egress check.
  * Dependency: WI-17, since the assertion is inert while ingress is public
* WI-19: Decouple the static suite from literal anchors in files it does not own (medium)
  * Source: DD-22
  * The suite parses `main.bicep` by string search and asserts `web.config` values by literal. Both couplings made unrelated, correct edits in other phases break it. Parsing the compiled ARM JSON, or reading the expected framework from the project file rather than hardcoding it, would remove the class rather than the instance.
  * Dependency: none
* WI-20: Refresh the 4.5.2 references in docs and the workflow evidence summary (medium)
  * Source: Phase 9
  * `docs/classic-net-bff-poc.md` carries roughly a dozen references and `.github/workflows/classic-net-bff-poc.yml` line 589 describes a compile-target-versus-runtime split that no longer exists. Descriptive only, so nothing breaks, but the evidence narrative now contradicts the artifact. The comparison table needs rework rather than find-and-replace.
  * Dependency: Phase 7, which owns documentation accuracy
* WI-21: Give `verify-ingress.ps1` an opt-in strict switch (low)
  * Source: Phase 6
  * The workflow reconstructs the not-executed count by string-parsing the script's `summary pass=N fail=N not-executed=N` line. That works, but it couples CI to an output format the script does not treat as a contract. A `-Strict` switch exiting non-zero when any check was declined would replace the parse with an exit code.
  * Dependency: none
* WI-22: Remove the escalation narrative from the `package.json` description field (medium)
  * Source: Phase 7
  * The npm `description` field contains a multi-paragraph markdown blockquote asserting the Step 1 fork is still open and listing Q7 and Q8 as outstanding. It is stale relative to the three-way session findings, and prose of that shape in a package manifest field is a defect independent of its accuracy. `README.md` carries the same stale claim.
  * Dependency: none
* WI-23: Record in the repository that the two-app comparison scope survived (medium)
  * Source: Phase 7
  * The Phase 1 gate contemplated formally reducing to a single app. ID-02 authorized the retarget instead, so the reduction never happened. That resolution exists only in this tracking log, and a reader of `poc/` or the root README alone cannot tell which outcome occurred.
  * Dependency: none
* WI-24: Implement the bounded log-ingestion deadline (medium)
  * Source: Phase 6, Step 6.2
  * The evidence job reports `sign-in-log-correlation` as `not-executed`, which is honest but weaker than the bounded poll the step describes. A step running the KQL in `scripts/evidence-kql.kusto` against a deadline, reporting `unavailable` on timeout, would close it.
  * Dependency: a delegated-user evidence session, WI-03

### Raised During Planning

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

## User Decisions

Decisions recorded from Implementation Decision prompts.

* ID-01: Ingress architecture for the POC — Option C selected
  * Context: Phase 1 found no Azure Policy enforcing disabled public access on Microsoft.Web/sites at any reachable scope, which falsified the premise the plan was built on. It also found the App Service plan at F1, which supports no private endpoint, and no private network path anywhere in the subscription.
  * Decision: adopt public ingress for the POC demo, and keep private ingress as the documented production-successor design rather than building it.
  * User rationale: the recommendation was accepted as given. The user additionally disclosed that an external process degrades the plan to F1 on roughly a 24 hour cycle, which independently rules out private endpoints — an endpoint cannot survive a tier repeatedly dropping below B1.
  * Authorization granted: scale the plan to B1, with the standing expectation that it will degrade again and require a redeploy.
  * Consequence: Phase 2 is rescoped from building private endpoints to parameterizing ingress; DD-18 and DD-19 are resolved; WI-16 and WI-17 are raised.

Decisions I made under delegated authority, where the user answered "you may if you judge this best".

* ID-02: Legacy comparison app runtime target — retarget poc/legacy-net452 to .NET Framework 4.8
  * Question asked: whether to retarget the legacy app to a supported .NET Framework version.
  * Decision: yes, to 4.8 rather than 4.8.1.
  * Reasoning: net452 reached end of support in April 2022, so Step 1.3 returns blocked while it stands, and dropping the legacy app would silently reduce the two-app comparison scope the user required be preserved. 4.8 is in-box on App Service Windows and is backward compatible with net452, making this a low-risk TargetFrameworkVersion change. 4.8.1 was rejected because it cannot be installed on Windows Server 2016 or 2019 and is only in-box on Server 2025, which would make the target dependent on a host version the POC does not control.
  * Consequence: resolves the Step 1.3 blocked verdict and closes WI-02.

* ID-03: CI runner model — do not introduce a self-hosted or connected runner
  * Question asked: whether to set up a self-hosted runner.
  * Decision: no. Phase 6 Step 6.1 keeps the deploy job on windows-latest.
  * Reasoning: the runner split existed only because a hosted runner has no route to a private endpoint. Under ID-01 the apps are publicly reachable, so that constraint is gone, and adding a self-hosted runner would carry cost, patching, and a pull-request exposure surface for no benefit. Step 6.1 records the condition under which this stops being true, so a future move to private ingress knows to revisit it.
  * Consequence: supersedes DD-10.

* ID-04: 403 handling — diagnose rather than prescribe a blind redeploy
  * Context: the user asked to be reminded to redeploy on a 403.
  * Decision: Step 5.1 resolves a 403 to one of tier degradation, ingress drift, or a stopped site, each with a named remedy, instead of always recommending a redeploy.
  * Reasoning: three distinct causes present to the operator as the same symptom. The observed 403 carrying a "Web App - Unavailable" title is specifically the disabled-public-access signature, which a redeploy would not fix. Prescribing one remedy for all three would send the operator down the wrong path in two cases out of three.

## Deployment Findings: 2026-09-23

### Implementation Deviations

* DD-23: `main.bicep` omitted `netFrameworkVersion` on both .NET 10 sites
  * Plan specifies: the template configures each site's runtime
  * Implementation differs: only the legacy site had the property set; the two .NET 10 sites inherited the ARM default of `v4.0` and returned HTTP 500.32
  * Rationale: not a deviation from intent but a gap in it. An omitted property cannot be caught by a static check that reads what the template states, which is why nine phases of validation missed it. Found within minutes of the first real deployment.

### Unaddressed Research Items

* DR-03: the reference BFF has no configuration contract for its own required settings
  * Source: observed live on 2026-09-23 from the ANCM stdout capture
  * Reason: the application validates `DownstreamApi:Scopes` at startup and fails closed, but no artifact defines what that scope should be, the registration exposes no API, and no proxy destinations exist
  * Impact: high for any attempt to host the BFF; none for the two-app comparison the POC actually demonstrates
  * Status: resolved 2026-09-23. The contract is now defined in three places that agree: `scripts/provision-owned-api-registration.ps1` converges the registration that exposes the scope, `infra/poc/main.bicep` supplies the scope, the permitted origin and the proxy destination as app settings, and `poc/owned-api-net10` is the destination those settings point at. The BFF starts.

## Suggested Follow-On Work

* WI-25: Define the BFF configuration contract — expose an Application ID URI and delegated scope on the registration, decide the proxy destination set, and add the corresponding app settings to `main.bicep` (high)
  * Source: 2026-09-23 deployment
  * Dependency: a decision on what the BFF is meant to front, which Step 1.4 returned as out-of-scope and Step 3.7 left blocked
  * Status: implemented 2026-09-23. The dependency was resolved by ID-07. A second registration, `croesus-bff-a3v24wppuvd34-api`, exposes `api://<api-client-id>/access_as_user` and pre-authorizes the web registration so no consent prompt stands between the BFF and its downstream. A new site, `croesus-bff-a3v24wppuvd34-api`, runs `poc/owned-api-net10`, which validates bearer tokens only. The BFF received `DownstreamApi__Scopes__0`, `ProxyPolicy__AllowedDestinationOrigins__0` and `ReverseProxy__Clusters__owned-api__Destinations__primary__Address`. Verified live: the App Service event log reports `Application 'C:\home\site\wwwroot\' started successfully`, `/` returns HTTP 200, and an unauthenticated `/api/profile` returns the bounded `interaction_required` JSON rather than an identity provider redirect.
* WI-26: Pin `RuntimeIdentifier` to `win-x64` in the publish path for both .NET 10 projects, or set it in CI, so an ARM64 developer machine cannot produce an unloadable package (medium)
  * Source: 2026-09-23 deployment
  * Dependency: none
* WI-27: Add a post-deployment application-liveness check to `verify-ingress.ps1` or the CI evidence job — every ingress check passed against a site returning HTTP 500, because the script asserts reachability and challenge shape, not that the application started (medium)
  * Source: 2026-09-23 deployment
  * Dependency: none
* WI-28: Make the verifier's `ChallengePath` default fit the app under test — the legacy app serves an anonymous page at `/` and challenges at `/signin`, so the default produced a false `challenge-not-a-redirect` failure (low)
  * Source: 2026-09-23 deployment
  * Dependency: none

## User Decisions

* ID-05: Whether to host the reference BFF — Option "Yes, host the BFF too" selected
  * Rationale: user elected to expand beyond the plan default of `deployBffSite = false`. The site provisions correctly; the application cannot start for reasons recorded as WI-25.
* ID-06: How to supply the expired client secret — Option "Rotate the secret, then deploy" selected
  * Rationale: the deployed credential had been expired for six weeks. Rotated with `--append` so the prior credential was not invalidated, and the value was never displayed or routed through the assistant.

## Evidence Findings: 2026-09-23

### Unaddressed Research Items

* DR-04: token protection status is absent from the Graph v1.0 sign-in log schema
  * Source: observed live on 2026-09-23 against this tenant
  * Reason: `auditLogs/signIns` on `v1.0` returns `tokenProtectionStatusDetails` as null. The same rows on `beta` return `signInSessionStatus` and `signInSessionStatusCode`. Any evidence capture that reads binding status from v1.0 silently records nothing and would be misread as "no binding problem observed".
  * Impact: high for evidence capture; the API version is load-bearing and is not stated in `scripts/evidence-kql.kusto` or the workflow

### Implementation Deviations

* DD-24: the Unbound diagnostic was treated as a Croesus-specific observation pending explanation
  * Plan specifies: preserve the unresolved customer finding until grant and policy evidence is obtained
  * Implementation differs: the finding is now partly explained by measurement. Over 200 recent sign-in rows in this tenant, `unbound` appears 24 times, including one `1008`, on applications unrelated to this POC, with no BFF present. `bound/0` occurs 149 times and every one of those rows carries `incomingTokenType=primaryRefreshToken`.
  * Rationale: binding status tracks whether the client presented a primary refresh token. That is a property of the client device and its broker, not of server-side token custody, so a BFF cannot convert an unbound sign-in into a bound one. This strengthens rather than weakens the research position in Scenario 4: Unbound is a routine binding diagnostic, observable in ordinary traffic, and is not evidence of replay.

## Suggested Follow-On Work

* WI-29: State the required Graph API version for token protection status in `scripts/evidence-kql.kusto` and in any sign-in log capture step, and add a guard that treats a null `tokenProtectionStatusDetails` as not-executed rather than as an absence of findings (high)
  * Source: 2026-09-23 evidence review
  * Dependency: none
  * Status: implemented 2026-09-23. Query 4 in `scripts/evidence-kql.kusto` now buckets every row instead of filtering to populated ones, so "signal out of scope", "nothing collected" and "binding observed" stay distinguishable, and its header states the v1.0 versus beta Graph behaviour. The Tier 2 evidence step in `.github/workflows/deploy-croesus.yml` gained an `emit_query_result` helper: piping `az` straight into `tee` meant a successful query returning zero rows produced a blank table indistinguishable from a passing exhibit, and a failed query took the same path. All three outcomes now print an explicit verdict, with the two non-row outcomes labelled not-executed.
* WI-30: Add the tenant baseline measurement to `docs/evidence-narrative.md` so the customer conversation can cite an observed unbound rate in traffic unrelated to the escalation rather than reasoning about it (medium)
  * Source: 2026-09-23 evidence review
  * Dependency: agreement on how much tenant-shaped data may be shared
  * Status: implemented 2026-09-23 as the "A measured base rate for Unbound" section. The dependency was resolved by publishing proportions and the binding-to-token-type correlation only, with no application names, user principals or row identifiers. The section states that a base rate is context rather than a verdict and that the original finding stays open.
* WI-31: Reproduce the tenant baseline as a repeatable capture rather than a transcribed sample (low)
  * Source: 2026-09-23 WI-30 implementation
  * Dependency: none. Query 5 exists; the percentages in `docs/evidence-narrative.md` are currently hand-carried from a single Graph beta sample and will drift from the workspace result.

## Owned API and BFF Downstream: 2026-09-23

### User Decisions

* ID-07: What the reference BFF proxies to — a separate API registration and a purpose-built minimal API
  * Rationale: user selected Option A and asked for the fuller, more realistic variant. Two sub-decisions followed from that. First, the scope lives on its own registration rather than on the shared web registration, because the audience boundary this escalation is about only exists when the resource and the client are different applications; a single registration acting as both would remove the thing being demonstrated. Second, the downstream is a new `poc/owned-api-net10` rather than the existing `api/Croesus.Api`, which requires Microsoft Graph on-behalf-of, a Key Vault certificate credential, Application Insights and CORS configuration, and whose primary endpoint fails without the Graph leg. Neither of those belongs in a proof about token custody.

### Implementation Deviations

* DD-25: Graph rejects a single PATCH that adds a delegated scope and pre-authorizes a client for it
  * Plan specifies: converge `api.oauth2PermissionScopes` and `api.preAuthorizedApplications` together
  * Implementation differs: `provision-owned-api-registration.ps1` writes them in two sequential PATCH requests
  * Rationale: observed live. Graph resolves `delegatedPermissionIds` against the scopes already stored on the application, so the combined request fails with `InvalidValue` naming a permission id present in the same body. The script now writes the scope, then the pre-authorization.

* DD-26: `requestedAccessTokenVersion` is set to 2 rather than left at the directory default
  * Plan specifies: nothing; the token version was not considered
  * Implementation differs: the API registration requests v2 access tokens, and the provisioning script fails preflight if it is anything else
  * Rationale: Microsoft.Identity.Web builds a v2.0 authority from `Instance` and `TenantId`. A v1 access token carries the `sts.windows.net` issuer, which fails issuer validation against that metadata. Left unset, the first proxied call would fail authentication for a reason that does not name its cause.

### Suggested Follow-On Work

* WI-32: Perform an interactive delegated sign-in through the BFF and capture the proxied call (high)
  * Source: 2026-09-23 owned API deployment
  * Dependency: a human sign-in. Everything up to the redirect is verified; the token acquisition, the audience on the token the API receives, and the claim that no cookie crosses the boundary are all asserted by tests and by design, not yet by a live delegated call. `GET /api/profile` through the BFF returns the audience, the calling application id and a `receivedCookie` observation, so one sign-in produces the evidence.
* WI-33: Persist the Data Protection key ring for the BFF (medium)
  * Source: 2026-09-23 App Service event log, `No XML encryptor configured`
  * Dependency: none. `DataProtection:KeyRingPath` is empty, so keys live in memory and every restart invalidates existing session cookies. Tolerable for a single-instance demonstration, but a scale-out or a restart mid-demonstration signs everyone out.
* WI-34: Publish the owned API from CI rather than from a developer machine (medium)
  * Source: 2026-09-23 owned API deployment
  * Dependency: none. `.github/workflows/classic-net-bff-poc.yml` builds and publishes only the legacy and modern apps. Neither the BFF nor the owned API is in the publish step, so both currently reach App Service by hand.

