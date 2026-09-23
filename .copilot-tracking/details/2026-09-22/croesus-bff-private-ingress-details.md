<!-- markdownlint-disable-file -->
# Implementation Details: Croesus BFF Private Ingress and Security Evidence

## Context Reference

Sources:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - revised primary research, supersedes the public-access remediation
* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - private endpoint, DNS, runner, and browser reachability requirements
* .copilot-tracking/research/subagents/2026-09-22/bff-auth-evidence-review.md - OAuth grant, sign-in log, and Conditional Access evidence limits
* .copilot-tracking/research/subagents/2026-09-22/bff-architecture-security-review.md - token custody, CSRF, proxy controls, legacy identity bridge
* Conversation 2026-09-22 - user confirmed Azure Policy prohibits public access and requested private endpoints

Standing guardrails carried from assets/croesus-escalation-packet.md section 4 and assets/croesus-3way-session-findings.md section 7: no customer tenant changes, no Conditional Access edits, no AWS IP allowlisting, no mandated On-Behalf-Of.

## Implementation Phase 1: Prerequisite Verification and Gate Capture

<!-- parallelizable: false -->

This phase is read-only against Azure and GitHub. It produces no resource mutation. Every later phase depends on the answers captured here, so it runs first and alone.

Changes-file convention for every phase: a phase appends only under its own uniquely titled section in .copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md and never rewrites another phase's section. This is the only file that parallel phases share.

### Step 1.1: Capture effective Azure Policy evidence

Record the assignment that prohibits public access so the design targets the real constraint rather than an assumed one. Do not attempt a public-enable write to provoke a denial.

Files:

* .copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md - append a Policy Evidence section with assignment ID, definition or initiative member, scope, effect, parameters, and exemptions

Commands (read-only):

```powershell
$rg = 'croesus-bff-poc-rg'
az policy assignment list --disable-scope-strict-match --query "[].{name:name, id:id, policyDefinitionId:policyDefinitionId, enforcementMode:enforcementMode, scope:scope, parameters:parameters}" -o json
az policy state list -g $rg --filter "complianceState eq 'NonCompliant'" --query "[].{policyAssignmentId:policyAssignmentId, policyDefinitionId:policyDefinitionId, resourceId:resourceId}" -o json
az policy definition show --name <definitionNameFromAssignment> -o json
az policy set-definition show --name <initiativeNameFromAssignment> -o json
az policy exemption list --disable-scope-strict-match -o json
az monitor activity-log list -g $rg --offset 30d --query "[?contains(operationName.value, 'Microsoft.Web/sites')].{time:eventTimestamp, caller:caller, op:operationName.value, status:status.value}" -o table
```

The definition, set-definition, and exemption commands are required because assignment metadata alone does not reveal the effect body, the resolved effect parameters, or existing exemptions. All six commands are read-only.

Discrepancy references:

* Addresses DR-01 (policy attribution unverified)
* Addresses DD-01 (previous plan asserted no policy was enforcing publicNetworkAccess)

Success criteria:

* The enforcing assignment and definition IDs are recorded, or the absence of a matching assignment is recorded explicitly as unverified
* Whether the policy also constrains private endpoints, private DNS zones, subnets, public IPs, or egress is recorded
* No write operation was attempted against either App Service

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 Selected Approach, policy-versus-ingress distinction
* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - Policy constraint and the two different 403s

Dependencies:

* Azure CLI session with reader access to the subscription and policy scope

### Step 1.2: Confirm reusable network, DNS, browser, and runner topology

Determine whether approved infrastructure already exists before provisioning anything new. Absence from croesus-bff-poc-rg does not mean absence from the subscription.

Files:

* .copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md - append a Topology Inventory section

Commands (read-only):

```powershell
az network vnet list --query "[].{name:name, rg:resourceGroup, location:location, prefixes:addressSpace.addressPrefixes}" -o table
az network private-dns zone list --query "[?name=='privatelink.azurewebsites.net'].{name:name, rg:resourceGroup, links:numberOfVirtualNetworkLinks}" -o table
az network vnet-gateway list --query "[].{name:name, rg:resourceGroup, type:gatewayType}" -o table
az webapp show -g croesus-bff-poc-rg -n croesus-bff-a3v24wppuvd34-modern --query "{publicNetworkAccess:publicNetworkAccess, state:state, httpsOnly:httpsOnly}" -o json
az webapp show -g croesus-bff-poc-rg -n croesus-bff-a3v24wppuvd34-legacy --query "{publicNetworkAccess:publicNetworkAccess, state:state, httpsOnly:httpsOnly}" -o json
```

Record for each answer: owner, approval status, and whether it is reusable for this POC.

Success criteria:

* A named VNet and subnet with free address space is identified, or a gap is recorded
* Private DNS zone ownership for privatelink.azurewebsites.net is identified, or a gap is recorded
* The browser access path is chosen and named: VPN or ExpressRoute connected workstation, or an approved Bastion-accessed jump VM with its device-context caveat recorded
* The deployment runner option is chosen and named: connected self-hosted runner, or configured GitHub larger Windows runner with Azure private networking
* Current publicNetworkAccess values on both sites are recorded as the pre-change baseline
* The outbound dependency inventory is recorded: every destination the apps must reach, including the distributed cache backing store, Key Vault, Microsoft Entra endpoints, and Application Insights ingestion, together with whether a separate delegated VNet integration subnet is required and whether egress to each destination is permitted
* A verdict is recorded on whether a private endpoint and private DNS zone for the reference BFF are in scope for this engagement, or whether the BFF stays build-and-test-only in CI
* The BFF HTTPS hostname value is recorded here regardless of that verdict, so that Step 2.4 and Step 4.1 each read it from Phase 1 rather than from each other

Context references:

* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - Network and Deployment Contract, Smallest Viable Private Deployment
* .github/workflows/classic-net-bff-poc.yml (line 275) - current windows-latest deploy runner

Dependencies:

* Step 1.1 completion so that policy limits on network resources are known before selecting a topology

### Step 1.3: Gate the legacy comparison app on supported-target review

The legacy app must not regain reachability purely because the path is private. Private networking is not a runtime-support exemption.

Files:

* .copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md - append a Legacy Readiness Gate section with a pass or blocked verdict

Inspection targets:

* poc/legacy-net452/ project file target framework and packages.config entries
* The installed runtime the site actually uses, distinguished from the compile-time target
* infra/poc/main.bicep (approximately line 68 to line 118) - netFrameworkVersion and app settings for the legacy site

Success criteria:

* The compile-time target, the installed runtime, and the supported-status of each are recorded separately
* A verdict is recorded: legacy proceeds to private ingress in Phase 2, or legacy is blocked and only the modern app is wired in Phase 2
* If blocked, the remaining work to unblock is captured as a work item rather than silently dropping the two-app goal

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scope and Success Criteria, legacy support gate
* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - Security boundaries that private ingress cannot repair

Dependencies:

* Step 1.2 completion

### Step 1.4: Decide legacy identity bridge scope and bypass prevention

The reference BFF cannot claim to front the legacy app unless an explicit identity bridge exists and direct ingress to the legacy app is blocked. Private networking does not create either property. This step decides whether that work is authorized before Phase 3 assumes it.

Files:

* .copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md - append a Legacy Bridge Decision section

Decision inputs:

* Whether changes to poc/legacy-net452 are authorized for this engagement, or whether the legacy app must remain unmodified
* Whether the legacy app can be reached by any private client other than the BFF, given that both apps share a VNet-reachable address space once endpoints exist
* What the bridge would carry: a signed header contract, a mutual TLS client certificate, or a separate token audience, and how the legacy app would reject a forged version of it
* Logout semantics across the bridge, because a BFF logout that leaves a live legacy session is not a logout

Success criteria:

* A verdict is recorded: bridge in scope with a named mechanism, or bridge out of scope
* If out of scope, the plan records legacy integration as blocked rather than complete, and no artefact claims the BFF fronts the legacy app
* The bypass-prevention requirement is recorded either way, because an unauthenticated legacy route reachable from the private network is a finding regardless of whether the bridge is built

Discrepancy references:

* Addresses DR-07 (legacy identity bridge and direct-ingress bypass previously unplanned)

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - H5 and Scenario 3
* .copilot-tracking/research/subagents/2026-09-22/bff-architecture-security-review.md - legacy identity bridge finding

Dependencies:

* Step 1.3 completion

## Implementation Phase 2: Ingress Restoration and Successor Design

<!-- parallelizable: true -->

Only infrastructure-as-code files are touched in this phase. It can run alongside Phases 3, 4, and 5.

Rescoped by the ID-01 decision. The original phase built per-app private endpoints on the assumption that Azure Policy forbade public ingress. Phase 1 Step 1.1 found no such policy, and Phase 1 Step 1.2 found the plan degraded to F1, which an external process repeats on roughly a 24 hour cycle. A private endpoint cannot survive that cycle because F1 cannot host one. The phase now restores public ingress as an explicit parameterized choice and preserves the private path as an opt-in successor.

### Step 2.1: Parameterize ingress and restore public access for the POC

Replace the hardcoded ingress posture with a named parameter so the choice is visible in source and reversible without editing resource bodies.

Files:

* infra/poc/main.bicep - add an ingressMode parameter and drive publicNetworkAccess on every site from it

Required behaviour:

* A parameter `ingressMode` constrained to the values `Public` and `Private`, defaulted to `Public` for the POC
* Every site resource sets `publicNetworkAccess` from that parameter rather than from a literal
* A comment on the parameter records that the default changed because no policy enforced the alternative, citing DD-17, so a future reader does not assume the POC drifted

Discrepancy references:

* Supersedes DD-01. The withdrawn edit set publicNetworkAccess to Enabled; the replacement pin to Disabled is itself now withdrawn by ID-01. The parameter resolves both by making the posture explicit instead of hardcoded either way.

Success criteria:

* No site resource contains a literal publicNetworkAccess value
* No ipSecurityRestrictions rule is added as a substitute for either posture
* `az bicep build` succeeds

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 Network and Deployment Contract
* .copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md - Phase 1 Policy Evidence

Dependencies:

* Implementation Phase 1 completion

### Step 2.2: Retain the private endpoint path as an opt-in successor module

The private design is still correct for production. It is not built for the POC, but it must not be lost, because the research behind it remains valid and the successor environment will need it.

Files:

* infra/poc/modules/privateendpoint.bicep - new module accepting app resource ID, subnet ID, private DNS zone ID, and endpoint name
* infra/poc/main.bicep - instantiate the module only when `ingressMode` is `Private`

Module contents:

* Microsoft.Network/privateEndpoints with privateLinkServiceConnections targeting groupIds: [ 'sites' ]
* A privateDnsZoneGroups child resource referencing the privatelink.azurewebsites.net zone, covering both the application hostname and the scm hostname

Required behaviour:

* The module is authored and compiles, but no endpoint is instantiated under the default parameter values
* The module header comment states the two prerequisites the POC subscription does not currently satisfy: a plan at B1 or better that stays there, and a VNet in the same region as the apps with a linked privatelink.azurewebsites.net zone
* Both the application and scm hostnames are covered, because an endpoint that serves the browser while scm stays unresolved produces a working demo and a broken deployment

Success criteria:

* The endpoint targets the sites subresource, not a plan-level resource
* Endpoint and DNS zone group names are deterministic and match the existing naming convention at infra/poc/main.bicep approximately line 34 to line 38
* `az bicep build` succeeds with ingressMode at both values
* No private IP or privatelink hostname is introduced as an OIDC redirect URI in either mode

Context references:

* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - Windows Basic B1 supports private endpoints, per-app endpoints
* https://learn.microsoft.com/en-us/azure/app-service/overview-private-endpoint
* https://learn.microsoft.com/en-us/azure/private-link/private-endpoint-dns-integration

Dependencies:

* Step 2.1 completion

### Step 2.3: Make the plan tier explicit and self-diagnosing

The deployed plan is F1 while the template declares B1, and an external process returns it to F1 on roughly a 24 hour cycle. The template must make the tier a visible parameter and must refuse a combination it cannot honour, rather than deploying a private endpoint onto a tier that cannot host one.

Files:

* infra/poc/main.bicep - parameterize the plan SKU and add the incompatibility guard and diagnostic outputs
* infra/poc/main.bicepparam - set the SKU explicitly

Required behaviour:

* A parameter for the plan SKU name, defaulted to `B1`, replacing the current literal
* A compile-time or deploy-time assertion that fails with a readable message when `ingressMode` is `Private` and the SKU is a tier that cannot host a private endpoint, naming F1 and D1 explicitly
* Outputs exposing the resolved SKU name and the resolved ingress mode, so a verification script can read intent from the deployment rather than inferring it
* A comment recording that the deployed plan was observed at F1 on 2026-09-22 while this template declared B1, and that an external process is understood to repeat the degradation

Success criteria:

* No plan SKU literal remains in the site or plan resource bodies
* The guard rejects the Private plus F1 combination with a message naming the tier
* `az bicep build` succeeds

Context references:

* .copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md - Phase 1 Topology Inventory, observed F1 plan
* .copilot-tracking/plans/logs/2026-09-22/croesus-bff-private-ingress-log.md - DD-18 and WI-11

Dependencies:

* Step 2.1 completion

### Step 2.4: Add the reference BFF site on its own hostname

Step 4.1 registers redirect URIs against a hostname, so that hostname must come from somewhere. Step 1.2 records the hostname value and this step provisions the site behind a parameter defaulted to off. Phase 2 and Phase 4 therefore both read the hostname from Phase 1 and neither depends on the other.

Files:

* infra/poc/main.bicep - add a parameter `deployBffSite` defaulted to false, and a conditional site resource
* infra/poc/main.bicepparam - set deployBffSite and the BFF hostname value recorded in Step 1.2

Required behaviour:

* The site name is `croesus-bff-${uniqueSuffix}-bff`, distinct from the existing `-legacy` and `-modern` sites
* The hostname this step provisions is exactly the value recorded in the corrected Step 1.2 record, so Phase 4 never reads a value produced by Phase 2
* The BFF site must not reuse the `-modern` hostname. The modern site already claims `/signin-oidc` through `modernCallbackUri`, and two registrations claiming one redirect URI is a provisioning conflict
* When deployBffSite is false, no site is created, and the recorded hostname remains the value the registration uses once hosting exists
* The BFF site drives publicNetworkAccess from `ingressMode` like every other site, and receives a private endpoint only in Private mode
* The hostname is a normal HTTPS App Service hostname, never a private IP and never a privatelink hostname

Discrepancy references:

* Addresses DR-08 (reference BFF had no hosting or hostname source while Step 4.1 required one)
* Addresses DD-21 (Phase 1 recorded the modern app's hostname as the reference BFF origin)

Success criteria:

* Step 4.1 can resolve a concrete redirect URI hostname in both parameter states
* The provisioned hostname matches the `BFF_BASE_URI` default in scripts/provision-app-registrations.sh exactly
* `az bicep build` succeeds with deployBffSite both true and false

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1, a separately hosted BFF adds another endpoint

Dependencies:

* Step 2.3 completion, and the Step 1.2 hosting verdict and recorded hostname

### Step 2.5: Validate phase changes

Compile the templates without deploying, in every parameter combination the phase introduces.

Validation commands:

* `az bicep build --file infra/poc/main.bicep` - template compiles
* `az bicep build-params --file infra/poc/main.bicepparam` - parameters compile

Success criteria:

* Both commands exit zero with no warnings introduced by this phase
* Compilation is confirmed for ingressMode Public and Private, and for deployBffSite true and false

## Implementation Phase 3: Reference BFF Application

<!-- parallelizable: true -->

All work is confined to a new project directory. It can run alongside Phases 2, 4, and 5.

### Step 3.1: Scaffold the project and test project from the modern app conventions

Extend the conventions already proven in poc/modern-net10 rather than starting from a Blazor sample. The frontend framework question stays out of scope.

Files:

* poc/bff-yarp-net10/Croesus.BffYarp.csproj - net10.0, Yarp.ReverseProxy, Microsoft.Identity.Web, Microsoft.Identity.Web.TokenCache, pinned versions with no wildcards, and an explicit exclusion of the Tests directory from the default compilation glob
* poc/bff-yarp-net10/Program.cs - composition root
* poc/bff-yarp-net10/appsettings.json - AzureAd section and ReverseProxy routes and clusters
* poc/bff-yarp-net10/Tests/Croesus.BffYarp.Tests.csproj - the test project that Steps 3.2 through 3.7 write tests into, with a project reference to Croesus.BffYarp.csproj and pinned test package versions
* poc/bff-yarp-net10/Croesus.BffYarp.slnx - solution file covering the web project and the test project

Carry forward from poc/modern-net10/Program.cs:

* UseForwardedHeaders with XForwardedFor and XForwardedProto registered before authentication, taken from lines 97 to 118
* __Host- prefixed cookie names, HttpOnly, SecurePolicy.Always, Path root, null Domain
* SameSite.Lax on the session cookie, with the OIDC redirect rationale retained in a single-line comment
* MapInboundClaims = false and the tenant policy issuer validator

Re-derive rather than copy the forwarded-header trust boundary. The existing app calls UseForwardedHeaders only when KnownProxies is non-empty. A private endpoint changes the measured hop count and the trusted peer, so the KnownProxies and KnownNetworks values must be re-established against the actual private topology rather than inherited.

Success criteria:

* dotnet build succeeds
* dotnet test runs and discovers the test project even before any test exists
* The web project excludes the Tests directory from its compilation glob, so no test source compiles into the shipped application
* No package reference uses a wildcard version
* SaveTokens remains false
* The forwarded-header trust configuration is derived from the private topology and documented, with no blanket trust of all proxies

Context references:

* poc/modern-net10/Program.cs (lines 17 to 57) - cookie hardening, issuer validation, and claim mapping configuration to reuse
* poc/modern-net10/Program.cs (lines 97 to 118) - forwarded header configuration and the KnownProxies guard to re-derive
* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 Selected Approach

Dependencies:

* Implementation Phase 1 completion

### Step 3.2: Implement server-side token and session custody

This is the correction that distinguishes the reference app from the withdrawn design. Setting SaveTokens to true would place tokens in the browser-held encrypted ticket, which is not server-side custody.

Files:

* poc/bff-yarp-net10/Program.cs - add token acquisition and distributed caches, and a server-side ticket store
* poc/bff-yarp-net10/Security/ServerTicketStore.cs - ITicketStore implementation backed by the distributed cache

Required wiring:

* AddMicrosoftIdentityWebApp with EnableTokenAcquisitionToCallDownstreamApi for the downstream API scope
* A distributed token cache rather than an in-memory cache, so restart and multi-instance behaviour is testable
* CookieAuthenticationOptions.SessionStore assigned to the ticket store, so the browser receives only a session reference
* Persisted and protected Data Protection keys
* Bounded absolute and idle expiry on both the ticket and the cache entry

Discrepancy references:

* Addresses DD-02 by replacing SaveTokens = true with library-owned server-side caching

Success criteria:

* SaveTokens is false and no access or refresh token is serialized into any cookie
* A test asserts the response Set-Cookie value does not contain a sentinel token value
* Restarting the process does not silently sign the user out when the distributed cache persists

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 Selected Approach, token custody
* https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.cookieauthenticationoptions.sessionstore

Dependencies:

* Step 3.1 completion

### Step 3.3: Implement the constrained proxy boundary

The proxy is where the previous plan was thinnest. Destination allowlisting, credential stripping, and CSRF enforcement are requirements, not refinements.

Files:

* poc/bff-yarp-net10/Security/AccessTokenTransform.cs - request transform acquiring the token from the token acquisition service
* poc/bff-yarp-net10/Program.cs - route registration, antiforgery, and authorization wiring
* poc/bff-yarp-net10/appsettings.json - explicit clusters and destinations

Required behaviour:

* Acquire the downstream token through ITokenAcquisition for the configured scope, not through GetTokenAsync against cookie properties
* Remove the inbound Authorization header and the session cookie before forwarding, then set only the server-acquired bearer token
* Reject any request whose destination is influenced by user input; destinations come only from configuration
* Require antiforgery validation on every state-changing method, including proxied POST, PUT, PATCH, DELETE, and local logout
* Ensure GET and HEAD routes cannot mutate state
* Do not propagate unexpected downstream Set-Cookie headers back to the browser

Discrepancy references:

* Addresses DD-03 by replacing the six-line forwarding sketch with an enforced boundary

Success criteria:

* A test proves a forwarded request carries the server token and not the browser cookie
* A test proves a state-changing request without a valid antiforgery token is rejected
* A test proves an unlisted destination is refused

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 Required Security Behavior items 1 to 3
* .copilot-tracking/research/subagents/2026-09-22/bff-architecture-security-review.md - proxy mutation and credential stripping findings

Dependencies:

* Step 3.2 completion

### Step 3.4: Implement the session and token lifecycle

Renewal, challenge handling, and logout are the paths where a BFF most often fails closed incorrectly or fails open silently.

Files:

* poc/bff-yarp-net10/Program.cs - logout endpoint and challenge handling
* poc/bff-yarp-net10/Security/ClaimsChallengeHandler.cs - translate a downstream claims challenge into a bounded interaction-required result

Required behaviour:

* Normal refresh through the library token cache, with no browser involvement
* Expired or revoked sessions fail closed; never fall back to a browser-supplied token or another user's cache entry
* A JSON API returns a bounded interaction-required response that the frontend deliberately converts into navigation, rather than an invisible fetch of a login page
* Advertise the client capability claim only if the challenge path is actually implemented
* Non-idempotent operations are not retried automatically after a challenge without a replay-safety contract
* Local logout invalidates the server ticket and the associated cache entry

Success criteria:

* Tests cover expired session, revoked session, concurrent acquisition, cache outage, and interaction-required
* A test proves a post-logout request is rejected
* A test proves a challenge does not produce a redirect loop or a duplicated business operation

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 Required Security Behavior items 4 to 7

Dependencies:

* Step 3.3 completion

### Step 3.5: Implement the sanitized evidence surface

The evidence page is a customer artefact. It must be a typed allowlist, not a claims dump and not a tenant log browser.

Files:

* poc/bff-yarp-net10/Security/EvidenceCollector.cs - typed projection
* poc/bff-yarp-net10/Models/EvidenceResponse.cs - response contract
* poc/bff-yarp-net10/wwwroot/index.html - presentation

Permitted fields:

* Token custody indicator stating storage is server-side
* Cookie hardening flags actually observed: __Host- prefix, HttpOnly, Secure, SameSite
* Owned API audience validation outcome as a verdict, not as a raw token
* Application run identifier and per-operation acquisition identifiers
* Authentication method and time, and granted delegated scopes for the owned API

Prohibited output, in the response body, logs, and telemetry:

* Raw access tokens, refresh tokens, authorization codes, client secrets or assertions, complete ID tokens, cookie values, and complete token endpoint payloads
* Any claim decoded from a Microsoft Graph token, which is opaque to clients

Discrepancy references:

* Addresses DD-04 by removing audience-count and token-identifier comparisons presented as On-Behalf-Of proof
* Addresses DD-05 by removing Graph token claim inspection

Success criteria:

* The endpoint sets Cache-Control: no-store and requires an authenticated session
* A test asserts an opaque sentinel secret does not appear anywhere in the response, in addition to the existing JWT-shaped assertion
* No field claims that an On-Behalf-Of exchange occurred unless that hop was actually exercised and instrumented

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 4 Correlation and Privacy
* api/Tests/ReplayEndpointTests.cs - existing non-disclosure assertion convention to extend

Dependencies:

* Step 3.4 completion

### Step 3.6: Implement negative authorization and protocol tests

The positive path proves the flow works. These tests prove the boundaries reject what they must, and Step 6.2 gates on them, so they cannot remain implicit.

Files:

* poc/bff-yarp-net10/Tests/AudienceAndScopeTests.cs - resource-side rejection tests
* poc/bff-yarp-net10/Tests/ProtocolNegativeTests.cs - OIDC callback tests, both added to the test project created in Step 3.1

Required audience and scope tests:

* A token issued for a different audience is rejected by the owned API, not merely logged
* A token lacking the required delegated scope is rejected with an authorization failure distinct from an authentication failure
* A token accepted on the positive path proves the audience check is active rather than absent

Required protocol tests:

* A callback with a mismatched state value is rejected
* A callback with a mismatched or missing nonce is rejected
* A replayed authorization code or a replayed callback is rejected
* The OIDC nonce and correlation cookies are asserted separately from the session cookie, because the session cookie's SameSite Lax setting is a different decision with a different rationale

Discrepancy references:

* Addresses DR-09 (audience and scope tests referenced by the CI gate but never built)
* Addresses DR-10 (nonce, state, and callback replay evidence required by the research proof table)

Success criteria:

* Every listed test fails when its control is removed, verified once by temporarily disabling the control locally
* No test depends on live Entra access; protocol tests use synthetic callbacks

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 items 3 and 9, Scenario 4 proof table

Dependencies:

* Step 3.3 and Step 3.5 completion

### Step 3.7: Implement the legacy bridge contract or record it blocked

Execution branches on the Step 1.4 verdict. Both branches are legitimate outcomes; silently doing neither is not.

Files when the bridge is in scope:

* poc/bff-yarp-net10/Security/LegacyBridgeTransform.cs - the named bridge mechanism applied to legacy-bound routes only
* poc/bff-yarp-net10/appsettings.json - the legacy cluster and its destination allowlist

Files when the bridge is out of scope:

* .copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md - record legacy integration as blocked with the reason and the unblocking dependency

Required behaviour when in scope:

* One legacy route reachable only through an authenticated BFF session, and one legacy route that must be forbidden, both covered by tests
* The legacy app rejects a forged bridge credential, proven by a test that presents one
* Logout invalidates the legacy session as well as the BFF session

Required behaviour in both branches:

* Direct private ingress to legacy endpoints that bypasses the BFF is prevented or, if it cannot be prevented, recorded as an open finding

Discrepancy references:

* Addresses DR-07

Success criteria:

* No artefact states that the BFF fronts the legacy app unless the in-scope branch was executed and its tests pass
* The blocked branch names what would unblock it

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - H5 and Scenario 3

Dependencies:

* Step 1.4 verdict and Step 3.3 completion

### Step 3.8: Validate phase changes

Validation commands:

* dotnet build poc/bff-yarp-net10/Croesus.BffYarp.csproj - project compiles
* dotnet test for the new test project - behaviour, negative authorization, and protocol tests pass

Success criteria:

* Build and tests pass without relying on live Entra access

## Implementation Phase 4: Registration and Evidence Queries

<!-- parallelizable: true -->

Script and query files only. It can run alongside Phases 2, 3, and 5.

### Step 4.1: Add the confidential BFF registration block

A dedicated single-tenant web registration. Sharing one registration across app origins leaves only one front-channel logout URL.

Files:

* scripts/provision-app-registrations.sh - add an idempotent block creating or patching the BFF registration

The redirect hostname is the value recorded in Step 1.2, so this step is executable whether or not Step 2.4 provisions a site and does not depend on Phase 2.

Required shape:

* web.redirectUris containing the signin-oidc and signout-callback-oidc URIs on the normal HTTPS hostname recorded in Step 1.2
* web.logoutUrl set to the signout-oidc URI
* implicitGrantSettings with both access token and ID token issuance false
* spa.redirectUris and publicClient.redirectUris explicitly empty
* isFallbackPublicClient false
* signInAudience AzureADMyOrg
* Least-privilege delegated permissions for the owned API scope only

Ordering constraint:

* The owned API scope must be patched and committed before any preAuthorizedApplications patch, because Graph validates delegated permission IDs against scopes that already exist. This convention is already documented in scripts/provision-app-registrations.sh section 3.

Success criteria:

* Re-running the script produces no change on an already-provisioned tenant
* The credential follows the repository convention in docs/configuration-contract.md, using a Key Vault certificate rather than an inline secret
* No managed identity federated credential is introduced in this phase; it remains a later optional credential choice

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 App Registration Shape
* scripts/provision-app-registrations.sh - existing idempotent patterns and ordering constraint

Dependencies:

* Implementation Phase 1 completion

### Step 4.2: Set the owned API token version and optional claims

Access token claims are configured on the resource registration, not on the client.

Files:

* scripts/provision-app-registrations.sh - patch the API registration

Required changes:

* api.requestedAccessTokenVersion set to 2 so the audience value is deterministic
* Optional access token claims configured on the API registration only
* Group claims retained where authorization needs them, rather than removed to simplify evidence

Discrepancy references:

* Addresses DD-06 by moving optional claims off the client registration
* Addresses DD-07 by withdrawing the previous recommendation to set groupMembershipClaims to None purely to avoid overage

Success criteria:

* The API registration reports requestedAccessTokenVersion 2
* No optional access token claim is configured on the BFF client registration

Context references:

* https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims
* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 2 App Registration Shape

Dependencies:

* Step 4.1 completion

### Step 4.3: Replace speculative KQL with operation-mapped queries

The withdrawn queries inferred an OAuth grant from resource counts and assumed a single correlation identifier spanning every leg.

Files:

* scripts/evidence-kql.kusto - replace the audience-count proof query and add parameterized queries

Required query set:

* A query parameterized by the application run identifier and the observed per-operation identifiers captured by the app, rather than by an assumed shared CorrelationId
* A query resolving tenant service principal object identifiers rather than comparing a resource identity directly against a globally known application identifier
* A Conditional Access expansion query that reports the policy identifier, the target resource, and the evaluation mode alongside the result

Prohibited assertions in comments and query names:

* That two distinct audiences or token identifiers prove an On-Behalf-Of exchange
* That a populated client credential field identifies the grant
* That all four log categories appear for every user flow

Discrepancy references:

* Addresses DD-04 and DD-08

Success criteria:

* No query is presented as a release gate on its own
* Each query carries a bounded time window
* Comments state what each result does not prove

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 4 Flow and Proof Boundaries, Correlation and Privacy
* .copilot-tracking/research/subagents/2026-09-22/bff-auth-evidence-review.md - invalid evidence joins

Dependencies:

* Step 4.2 completion

### Step 4.4: Validate phase changes

Validation commands:

* bash -n scripts/provision-app-registrations.sh - shell syntax check
* Review scripts/evidence-kql.kusto for parameter placeholders and bounded windows

Success criteria:

* The script parses and every new block is idempotent by construction

## Implementation Phase 5: Provisioning and Verification Scripts

<!-- parallelizable: true -->

PowerShell script files only. It can run alongside Phases 2, 3, and 4.

### Step 5.1: Add ingress preflight assertions and 403 diagnosis

A reachable endpoint is not a successful deployment, and a generic redirect is not a valid challenge. Under ID-01 the reachable path is public, and the dominant operational failure is a 403 whose cause is ambiguous.

Files:

* scripts/provision-classic-net-bff-deployment.ps1 - add preflight assertions before package deployment
* scripts/verify-ingress.ps1 - new script performing the discriminating checks and the 403 diagnosis

Required assertions:

* Resolve the application hostname and the scm hostname and confirm both resolve
* Confirm TCP 443 reachability to both hostnames without disabling TLS validation
* Request the challenge route and assert the expected authority, client identifier, exact HTTPS redirect URI, and PKCE method S256
* Emit only allowlisted verdicts; inspect headers in memory and never write raw Set-Cookie or authorization header values into output or artifacts
* Confirm the deployed publicNetworkAccess matches the ingressMode the template declared, and report a mismatch as drift rather than silently continuing

Required 403 diagnosis:

An operator seeing a 403 cannot tell which of three causes produced it. The script must distinguish them by reading resource state through the control plane rather than guessing, and must name the remedy for each:

* Plan degraded below the declared SKU. Read the plan tier; if it is F1 or D1 while the template declares otherwise, report tier degradation and instruct a redeploy. This is expected on roughly a 24 hour cycle in this subscription.
* Public ingress disabled. Read publicNetworkAccess; if Disabled while ingressMode is Public, report ingress drift and name the setting to restore.
* Site stopped. Read the site state; if it is not Running, report that instead of either of the above.

The three checks run in that order and the script reports the first that matches, because tier degradation can cause the others as a side effect.

Route note:

* The modern challenge route is the site root. The legacy challenge route is /signin.

Discrepancy references:

* Addresses DD-09 by removing the withdrawn az webapp update public-enable commands. Under ID-01 the template owns the ingress posture through the ingressMode parameter, so an imperative enable command remains prohibited; the script reports drift and does not repair it.
* Addresses DR-02 by replacing the predicted secret expiry error with a metadata check

Success criteria:

* No script in the repository sets publicNetworkAccess imperatively in either direction
* The verification script fails with a distinguishable message for a DNS failure, a routing failure, an authorization failure, and a challenge shape failure
* A 403 is resolved to tier degradation, ingress drift, or a stopped site, each with a named remedy
* Credential expiry is reported from registration metadata rather than inferred from a failed sign-in

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 Discriminating Checks
* scripts/test-classic-net-bff-deployment-entra-static.ps1 - existing static assertion suite conventions
* .copilot-tracking/plans/logs/2026-09-22/croesus-bff-private-ingress-log.md - ID-01 and DD-18

Dependencies:

* Implementation Phase 1 completion

### Step 5.2: Add the posture negative check

Reachability alone does not demonstrate which posture is actually in force.

Files:

* scripts/verify-ingress.ps1 - add a posture assertion mode

Required behaviour:

* In Public mode, assert that the apps are reachable and record explicitly that public reachability is the accepted POC posture under ID-01, not an accident
* In Private mode, assert from a client with no private route that the application and scm endpoints are not usable, and record that a public DNS answer is not by itself evidence of public application access
* The Private mode check reports not-executed, rather than pass, when run from a context that cannot establish the unconnected-client condition

Success criteria:

* The check reports which posture it verified, and never reports a pass for a posture it could not actually test
* No output implies the POC demonstrates private ingress

Context references:

* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - Acceptance Gates for a Later Authorized Implementation

Dependencies:

* Step 5.1 completion

### Step 5.3: Validate phase changes

Validation commands:

* pwsh -NoProfile -Command "Get-Command -Syntax ./scripts/verify-ingress.ps1" - script parses
* Existing static PowerShell suites under scripts/ continue to pass

Success criteria:

* No parse errors and no new static suite failures

## Implementation Phase 6: Pipeline Wiring

<!-- parallelizable: true -->

Wave 2. Workflow files only. It runs after Phases 2 through 5 because it wires their outputs, and it can run alongside Phase 7 because the two share no file.

### Step 6.1: Wire the reference BFF and ingress verification into CI

Under ID-01 the apps are publicly reachable, so the existing hosted runner can deploy and probe them. No self-hosted runner is introduced. This is a deliberate simplification the ingress decision made available, and it is recorded so a future move to private ingress knows to revisit it.

Files:

* .github/workflows/classic-net-bff-poc.yml - extend the validate job and wire verification into the deploy job

Required changes:

* Add build and test steps for poc/bff-yarp-net10 to the validate job
* Keep the deploy job on windows-latest at line 275. Do not introduce a self-hosted or connected runner.
* Add a comment at the runner declaration recording that a hosted runner suffices only while ingressMode is Public, and that private ingress would require a connected runner
* Invoke scripts/verify-ingress.ps1 before and after package deployment
* Record that provisioning success is not the deployment acceptance test

Discrepancy references:

* Supersedes DD-10. The assumption that the existing runner cannot reach the target was correct for private ingress and does not apply under ID-01.

Success criteria:

* No workflow step enables basic publishing credentials or opens SCM to work around connectivity
* No workflow step sets publicNetworkAccess imperatively
* The protected environment gate and short-lived federated credentials are unchanged
* The runner comment states the condition under which the hosted runner stops being sufficient

Context references:

* .github/workflows/classic-net-bff-poc.yml (line 275) - current runner
* .copilot-tracking/plans/logs/2026-09-22/croesus-bff-private-ingress-log.md - ID-01

Dependencies:

* Implementation Phase 2, Phase 3, Phase 4, and Phase 5 completion

### Step 6.2: Separate deterministic checks from delegated user evidence

Workload identity authentication is not user authentication, and an optional skip must not silently satisfy a required gate.

Files:

* .github/workflows/classic-net-bff-poc.yml - add an evidence job with explicit verdict reporting

Required behaviour:

* Deterministic CI covers registration shape, configuration, build and tests, private deployment, challenge shape, antiforgery enforcement, credential stripping, audience validation, synthetic challenge handling, and redaction
* Interactive delegated-user evidence is collected in a separately approved run on the intended browser and device, and is reported as blocked or not-executed when absent
* Log ingestion polling uses a bounded deadline, and a timeout is reported as unavailable evidence rather than a pass
* No state-changing request is replayed merely to generate an additional log row
* No resource owner password flow, no Conditional Access exclusion, and no stored user password is introduced as a test shortcut

Discrepancy references:

* Addresses DD-11 by preventing report-only results and skipped tests from being counted as user security proof

Success criteria:

* The evidence artefact records pass, fail, or not-executed for every required criterion
* Missing required evidence blocks a complete-proof claim rather than defaulting to success
* Evidence artefact access is restricted to the named operator or collector identity, with a stated retention bound and a named reader list, because sanitized evidence is still identity telemetry

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 5 Execution Sequence and Required Gates
* api/Tests/LiveRedemptionTests.cs - optional skip convention that must not be inherited by required gates

Dependencies:

* Step 6.1 completion

### Step 6.3: Define rollback behaviour

Files:

* .github/workflows/classic-net-bff-poc.yml - document rollback in the job comments
* scripts/cleanup-classic-net-bff-deployment.ps1 - confirm teardown does not remove shared infrastructure

Required behaviour:

* Rollback reverts the application artefact or disables the affected route
* Rollback never opens the network and never removes a Conditional Access policy to restore a green pipeline
* Teardown does not delete shared private DNS zones, VNets, or access infrastructure that other workloads depend on

Success criteria:

* The teardown path is verified to target only resources this POC created

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 5, rollback

Dependencies:

* Step 6.2 completion

## Implementation Phase 7: Documentation Correction

<!-- parallelizable: true -->

Wave 2. Documentation files only. It runs after Phases 2 through 5 because it documents their outcome, and it can run alongside Phase 6 because the two share no file.

### Step 7.1: Document the ingress decision, its successor, and the proof limits

Files:

* docs/classic-net-bff-poc.md - record the ingress decision, the tier degradation behaviour, the 403 diagnosis path, and the legacy readiness gate
* docs/evidence-narrative.md - correct the proof boundaries
* docs/configuration-contract.md - add the new configuration keys for the reference app and the ingress parameters

Required content in docs/classic-net-bff-poc.md:

* The POC runs on public ingress by explicit decision, not by drift. Record that Phase 1 found no Azure Policy enforcing disabled public access on Microsoft.Web/sites at any reachable scope, which withdrew the original premise.
* Private ingress is the production-successor design, available through the ingressMode parameter and the opt-in endpoint module. State the two prerequisites it needs that the POC subscription lacks: a plan that stays at B1 or better, and a same-region VNet with a linked privatelink.azurewebsites.net zone.
* The plan is degraded to F1 on roughly a 24 hour cycle by an external process. Document that a 403 most often means tier degradation and that scripts/verify-ingress.ps1 distinguishes that from ingress drift and a stopped site. Give the redeploy command.
* Do not present the ingress choice as a security finding in either direction. It is a hosting decision that the evidence narrative does not depend on.

Required corrections in docs/evidence-narrative.md:

* Token Protection wording currently describes native application clients only; it is now in preview for browser-based web applications on Windows and macOS scoped to Azure Resource Manager. Microsoft Graph remains uncovered, so the conclusion stands while the wording is stale.
* Remove any implication that the status code alone identifies an OAuth grant
* State that the reference BFF reduces token exposure without changing the downstream token type
* State that the BFF's security properties are independent of the ingress posture, so the public-ingress POC does not weaken the evidence

Required additions to docs/configuration-contract.md:

* Rows for croesus-bff-cert and BFF_CLIENT_ID, closing WI-15
* Rows for the ingressMode and plan SKU parameters

Success criteria:

* No document claims that private networking resolves authentication, Conditional Access, or token binding
* No document claims a compliant-device outcome or the disappearance of device claims as a consequence of adopting the BFF
* No document states or implies that the POC demonstrates private ingress
* A reader hitting a 403 can find the diagnosis path without reading the scripts

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 4 CA and Unbound Corrections
* docs/evidence-narrative.md - existing Token Protection section requiring rewording
* .copilot-tracking/plans/logs/2026-09-22/croesus-bff-private-ingress-log.md - ID-01, DD-17, DD-18, WI-15

Dependencies:

* Implementation Phases 2 through 5 completion, because this step documents the ingress decision and tier behaviour from Phase 2, the implementation and registration from Phases 3 and 4, and the verification path from Phase 5

### Step 7.2: Validate phase changes

Validation commands:

* Markdown lint per repository configuration for the three edited documents

Success criteria:

* No new lint findings

## Implementation Phase 8: Validation

<!-- parallelizable: false -->

### Step 8.1: Run full project validation

Execute the complete validation surface:

* az bicep build --file infra/poc/main.bicep
* dotnet build for api, poc/modern-net10, poc/legacy-net452 where applicable, and poc/bff-yarp-net10
* dotnet test for api/Tests and the new BFF test project
* The static PowerShell suites under scripts/
* Markdown lint for edited documentation

### Step 8.2: Fix minor validation issues

Apply direct fixes for lint findings, compile warnings, and isolated test failures introduced by this work.

### Step 8.3: Report blocking issues

Report rather than attempt inline resolution when any of the following occur:

* The effective policy prohibits a resource the rescoped plan still requires
* The plan tier cannot be held at B1 long enough to complete a validation run
* The legacy readiness gate fails again after the Phase 9 retarget and the two-app goal requires a scope decision
* A live authentication failure appears that requires tenant-side changes

Record each as a work item with its blocking dependency and hand the decision back to the user.

## Implementation Phase 9: Legacy Runtime Retarget

<!-- parallelizable: true -->

Added after Phase 1. Step 1.3 returned a blocked verdict because poc/legacy-net452 targets .NET Framework 4.5.2, which reached end of support in April 2022. ID-02 resolved that by authorizing a retarget rather than dropping the app, because dropping it would silently reduce the two-app comparison scope. This phase touches only the legacy project, so it runs alongside Phases 2, 5, 6, and 7, and must complete before Phase 8.

### Step 9.1: Retarget the legacy comparison app to .NET Framework 4.8

Files:

* poc/legacy-net452/**/*.csproj - change the target framework version
* poc/legacy-net452/**/app.config or web.config - update the target framework attribute to match
* poc/legacy-net452/**/packages.config - only where a package has no 4.8-compatible version at the pinned level

Required behaviour:

* Target .NET Framework 4.8, not 4.8.1. 4.8 is in-box on App Service Windows and backward compatible with 4.5.2. 4.8.1 is only in-box on Windows Server 2025 and cannot be installed on Server 2016 or 2019, which would make the target depend on a host version this POC does not control.
* The `TargetFrameworkVersion` in the project and the `targetFramework` attribute in the configuration file must agree, because a mismatch changes runtime quirk behaviour without a build error
* Do not change the authentication code paths in this step. The retarget must be separable from behavioural change so a later failure is attributable.
* Record the directory name mismatch: the folder remains poc/legacy-net452 while the target becomes 4.8. Either rename and update every reference, or leave a note in the project file. Do not leave the mismatch unexplained.

Discrepancy references:

* Resolves DD-20 for Step 1.3 and closes WI-02
* Implements ID-02

Success criteria:

* The legacy project builds against .NET Framework 4.8
* No project in poc/legacy-net452 still targets 4.5.2
* The two-app comparison scope is preserved rather than reduced

Context references:

* .copilot-tracking/changes/2026-09-22/croesus-bff-private-ingress-changes.md - Phase 1 legacy readiness verdict
* .copilot-tracking/plans/logs/2026-09-22/croesus-bff-private-ingress-log.md - ID-02
* https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/versions-and-dependencies

Dependencies:

* Implementation Phase 1 completion

### Step 9.2: Validate phase changes

Validation commands:

* Build the legacy solution with MSBuild or `dotnet build`, whichever the project currently supports
* Confirm no new compiler warnings are introduced by the retarget

Success criteria:

* The build succeeds and the retarget introduces no new warnings
* If the build cannot run in this environment, the step reports not-executed with the reason rather than claiming a pass

## Dependencies

* Azure CLI with reader access for Phase 1, and contributor access scoped to the POC resource group for later deployment
* Bicep CLI for template compilation
* .NET 10 SDK for the reference application
* .NET Framework 4.8 developer pack or MSBuild targets for the legacy comparison app
* PowerShell 7 for the verification scripts
* An App Service plan at B1 or better for the demo, with the understanding that an external process degrades it to F1 on roughly a 24 hour cycle
* Entra ID P1 or better in the demo tenant for any Conditional Access evaluation

## Success Criteria

* Public application and SCM ingress remain disabled throughout, including during failure recovery
* Each approved app is reachable over its own private endpoint using normal HTTPS hostnames from the actual browser and deployment runner
* No OAuth token is present in any browser cookie, proven by a sentinel assertion rather than by inspection of a flag
* State-changing proxied operations enforce antiforgery validation, and forwarded requests carry only the server-acquired credential
* Evidence artefacts record pass, fail, or not-executed per criterion, and no claim of an On-Behalf-Of exchange appears unless that hop was exercised and instrumented
