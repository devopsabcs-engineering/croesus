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

## Implementation Phase 2: Private Ingress Infrastructure

<!-- parallelizable: true -->

Only infrastructure-as-code files are touched in this phase. It can run alongside Phases 3, 4, and 5.

### Step 2.1: Pin disabled public ingress in Bicep

Make the compliant state explicit in source so a redeploy cannot reintroduce public ingress.

Files:

* infra/poc/main.bicep - add publicNetworkAccess: 'Disabled' to both site resources, and set scmIpSecurityRestrictionsUseMain consistently

Edit locations:

* infra/poc/main.bicep (approximately line 68 to line 118) - legacy site properties block
* infra/poc/main.bicep (approximately line 120 to line 160) - modern site properties block

Discrepancy references:

* Addresses DD-01 by replacing the withdrawn publicNetworkAccess: 'Enabled' edit with the opposite pin

Success criteria:

* Both site resources declare publicNetworkAccess: 'Disabled'
* No ipSecurityRestrictions rule is added as a substitute for private ingress, because IP rules are not evaluated for private endpoint traffic
* az bicep build succeeds

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 Network and Deployment Contract
* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - App Service access-restriction rules are not evaluated for private endpoint traffic

Dependencies:

* Implementation Phase 1 completion

### Step 2.2: Add a private endpoint module and per-app endpoints

One private endpoint per reachable app. A shared App Service plan does not imply a shared endpoint.

Files:

* infra/poc/modules/privateendpoint.bicep - new module accepting app resource ID, subnet ID, private DNS zone ID, and endpoint name
* infra/poc/main.bicep - instantiate the module for the modern app, and conditionally for the legacy app based on the Step 1.3 verdict

Module contents:

* Microsoft.Network/privateEndpoints with privateLinkServiceConnections targeting groupIds: [ 'sites' ]
* A privateDnsZoneGroups child resource referencing the privatelink.azurewebsites.net zone

Success criteria:

* The endpoint targets the sites subresource, not a plan-level resource
* The legacy endpoint is gated by a parameter so a blocked legacy verdict does not provision it
* Endpoint and DNS zone group names are deterministic and match the existing naming convention at infra/poc/main.bicep approximately line 34 to line 38
* az bicep build succeeds

Context references:

* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - Windows Basic B1 supports private endpoints, per-app endpoints
* https://learn.microsoft.com/en-us/azure/app-service/overview-private-endpoint

Dependencies:

* Step 2.1 completion

### Step 2.3: Wire private DNS records for application and SCM hostnames

Both the application hostname and the SCM hostname must resolve to that app's endpoint IP, or deployment fails while browsing appears to work.

Files:

* infra/poc/modules/privateendpoint.bicep - ensure the DNS zone group creates records for the app and its scm hostname
* infra/poc/main.bicepparam - add parameters for the existing VNet, subnet, and private DNS zone resource IDs identified in Step 1.2

Success criteria:

* Records for the app name and the app scm name resolve within the linked zone
* The zone is linked to every client VNet used by the browser and the deployment runner, or hybrid forwarding is documented in the changes file
* No private IP or privatelink hostname is introduced as an OIDC redirect URI

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 Network and Deployment Contract, DNS records
* https://learn.microsoft.com/en-us/azure/private-link/private-endpoint-dns-integration

Dependencies:

* Step 2.2 completion

### Step 2.4: Add the optional reference BFF site and endpoint

Step 4.1 registers redirect URIs against a hostname, so that hostname must come from somewhere. Step 1.2 records the hostname value, and this step provisions the site behind a parameter defaulted to off. Phase 2 and Phase 4 therefore both read the hostname from Phase 1 and neither depends on the other.

Files:

* infra/poc/main.bicep - add a parameter deployBffSite defaulted to false, and a conditional site resource plus a private endpoint module instantiation
* infra/poc/main.bicepparam - set deployBffSite and the BFF hostname value recorded in Step 1.2

Required behaviour:

* The hostname this step provisions is exactly the value recorded in Step 1.2, so Phase 4 never reads a value produced by Phase 2
* When deployBffSite is false, no site, endpoint, or DNS record is created, and the hostname recorded in Step 1.2 remains the value the registration uses once hosting exists
* When deployBffSite is true, the BFF site declares publicNetworkAccess Disabled and receives its own private endpoint on the sites subresource, matching Step 2.2
* The hostname is a normal HTTPS App Service hostname, never a private IP and never a privatelink hostname

Discrepancy references:

* Addresses DR-08 (reference BFF had no hosting or hostname source while Step 4.1 required one)

Success criteria:

* Step 4.1 can resolve a concrete redirect URI hostname in both parameter states
* az bicep build succeeds with deployBffSite both true and false

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1, a separately hosted BFF adds another endpoint

Dependencies:

* Step 2.3 completion, and the Step 1.2 hosting verdict and recorded hostname

### Step 2.5: Validate phase changes

Compile the templates without deploying.

Validation commands:

* az bicep build --file infra/poc/main.bicep - template compiles
* az bicep build-params --file infra/poc/main.bicepparam - parameters compile

Success criteria:

* Both commands exit zero with no warnings introduced by this phase

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

### Step 5.1: Add private path preflight assertions

A reachable endpoint is not a successful deployment, and a generic redirect is not a valid challenge.

Files:

* scripts/provision-classic-net-bff-deployment.ps1 - add preflight assertions before package deployment
* scripts/verify-private-ingress.ps1 - new script performing the discriminating checks

Required assertions:

* Resolve the application hostname and the scm hostname, and confirm both resolve to the expected private endpoint address
* Confirm TCP 443 reachability to both hostnames without disabling TLS validation
* Request the challenge route and assert the expected authority, client identifier, exact HTTPS redirect URI, and PKCE method S256
* Emit only allowlisted verdicts; inspect headers in memory and never write raw Set-Cookie or authorization header values into output or artifacts
* Confirm publicNetworkAccess is still Disabled after deployment

Route note:

* The modern challenge route is the site root. The legacy challenge route is /signin.

Discrepancy references:

* Addresses DD-09 by removing the withdrawn az webapp update public-enable commands
* Addresses DR-02 by replacing the predicted secret expiry error with a metadata check

Success criteria:

* No script in the repository contains a command that sets publicNetworkAccess to Enabled
* The verification script fails with a distinguishable message for a DNS failure, a routing failure, an authorization failure, and a challenge shape failure
* Credential expiry is reported from registration metadata rather than inferred from a failed sign-in

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 1 Discriminating Checks
* scripts/test-classic-net-bff-deployment-entra-static.ps1 - existing static assertion suite conventions

Dependencies:

* Implementation Phase 1 completion

### Step 5.2: Add a public negative check

Private reachability alone does not demonstrate that public access remains closed.

Files:

* scripts/verify-private-ingress.ps1 - add an unconnected-client assertion mode

Required behaviour:

* From a client with no private route, assert the application and scm endpoints are not usable
* Record that a public DNS answer is not by itself evidence of public application access

Success criteria:

* The negative check is explicitly labelled as requiring execution from an unconnected network, and reports not-executed rather than pass when that context is unavailable

Context references:

* .copilot-tracking/research/subagents/2026-09-22/bff-private-ingress-review.md - Acceptance Gates for a Later Authorized Implementation

Dependencies:

* Step 5.1 completion

### Step 5.3: Validate phase changes

Validation commands:

* pwsh -NoProfile -Command "Get-Command -Syntax ./scripts/verify-private-ingress.ps1" - script parses
* Existing static PowerShell suites under scripts/ continue to pass

Success criteria:

* No parse errors and no new static suite failures

## Implementation Phase 6: Pipeline Wiring

<!-- parallelizable: true -->

Wave 2. Workflow files only. It runs after Phases 2 through 5 because it wires their outputs, and it can run alongside Phase 7 because the two share no file.

### Step 6.1: Split hosted validation from private deployment

Standard hosted runners have no route to a private endpoint. Successful workload identity login does not create one.

Files:

* .github/workflows/classic-net-bff-poc.yml - retain the validate job on a hosted runner, and move package deployment and private probes to the connected runner

Required changes:

* Add build and test steps for poc/bff-yarp-net10 to the validate job
* Change the deploy job runner from windows-latest (line 275) to the connected runner label chosen in Step 1.2
* Keep Resource Manager provisioning on a hosted runner only if its access is permitted by policy, and record that provisioning success is not the deployment acceptance test
* Invoke scripts/verify-private-ingress.ps1 before and after package deployment

Discrepancy references:

* Addresses DD-10 by removing the assumption that the existing runner can reach a private SCM endpoint

Success criteria:

* No workflow step enables basic publishing credentials or opens SCM to work around connectivity
* The protected environment gate and short-lived federated credentials are unchanged
* The private runner is not exposed to untrusted pull request code

Context references:

* .github/workflows/classic-net-bff-poc.yml (line 275) - current runner
* https://docs.github.com/en/organizations/managing-organization-settings/about-azure-private-networking-for-github-hosted-runners-in-your-organization

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

### Step 7.1: Correct the private access prerequisites and proof limits

Files:

* docs/classic-net-bff-poc.md - add private access prerequisites, browser and runner requirements, and the legacy readiness gate
* docs/evidence-narrative.md - correct the proof boundaries
* docs/configuration-contract.md - add the new configuration keys for the reference app and the private topology parameters

Required corrections in docs/evidence-narrative.md:

* Token Protection wording currently describes native application clients only; it is now in preview for browser-based web applications on Windows and macOS scoped to Azure Resource Manager. Microsoft Graph remains uncovered, so the conclusion stands while the wording is stale.
* Remove any implication that the status code alone identifies an OAuth grant
* State that the reference BFF reduces token exposure without changing the downstream token type

Success criteria:

* No document claims that private networking resolves authentication, Conditional Access, or token binding
* No document claims a compliant-device outcome or the disappearance of device claims as a consequence of adopting the BFF

Context references:

* .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md - Scenario 4 CA and Unbound Corrections
* docs/evidence-narrative.md - existing Token Protection section requiring rewording

Dependencies:

* Implementation Phases 2 through 5 completion, because this step documents the private access prerequisites from Phase 2, the implementation and registration from Phases 3 and 4, and the verification and runner requirements from Phase 5

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

* The effective policy prohibits private endpoints, private DNS, or the selected runner resources
* No approved browser path or connected runner can be obtained
* The legacy readiness gate fails and the two-app goal requires a scope decision
* A live authentication failure appears that requires tenant-side changes

Record each as a work item with its blocking dependency and hand the decision back to the user.

## Dependencies

* Azure CLI with reader access for Phase 1, and contributor access scoped to the POC resource group for later deployment
* Bicep CLI for template compilation
* .NET 10 SDK for the reference application
* PowerShell 7 for the verification scripts
* An approved private network path for the browser and the deployment runner
* Entra ID P1 or better in the demo tenant for any Conditional Access evaluation

## Success Criteria

* Public application and SCM ingress remain disabled throughout, including during failure recovery
* Each approved app is reachable over its own private endpoint using normal HTTPS hostnames from the actual browser and deployment runner
* No OAuth token is present in any browser cookie, proven by a sentinel assertion rather than by inspection of a flag
* State-changing proxied operations enforce antiforgery validation, and forwarded requests carry only the server-acquired credential
* Evidence artefacts record pass, fail, or not-executed per criterion, and no claim of an On-Behalf-Of exchange appears unless that hop was exercised and instrumented
