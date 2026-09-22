<!-- markdownlint-disable-file -->
# Release Changes: Croesus BFF Private Ingress and Security Evidence

**Related Plan**: croesus-bff-private-ingress-plan.instructions.md
**Implementation Date**: 2026-09-22

## Summary

Restore reachability to the Croesus POC App Services, add a hardened reference BFF with server-side token custody and a constrained proxy boundary, and correct the authentication evidence surface so it does not overstate what the logs prove.

The ingress architecture is under re-ratification. The plan was written on the premise that Azure Policy prohibits public network access. Phase 1 falsified that premise: no policy assignment at any reachable scope enforces publicNetworkAccess on Microsoft.Web/sites. Phase 1 also found the deployed App Service plan is F1 Free, which does not support private endpoints at all. Private ingress is therefore an elective architecture rather than a compliance obligation, and Phase 2 is blocked pending a user decision. See the Phase 1 Policy Evidence and Phase 1 Topology Inventory sections for the evidence.

## Changes

### Added

* poc/bff-yarp-net10/Croesus.BffYarp.csproj - net10.0 web project, pinned versions, Tests directory excluded from the compilation glob
* poc/bff-yarp-net10/Croesus.BffYarp.slnx - solution covering the web and test projects
* poc/bff-yarp-net10/Program.cs - composition root; SaveTokens false, SessionStore assigned, antiforgery and authorization wiring
* poc/bff-yarp-net10/appsettings.json - AzureAd section, reverse proxy routes and destination allowlist
* poc/bff-yarp-net10/Models/EvidenceResponse.cs - typed evidence contract
* poc/bff-yarp-net10/Security/ServerTicketStore.cs - distributed ITicketStore keeping tokens off the browser
* poc/bff-yarp-net10/Security/AccessTokenTransform.cs - acquires the server token and strips browser credentials before forwarding
* poc/bff-yarp-net10/Security/ProxyBoundaryMiddleware.cs - antiforgery enforcement and response header suppression
* poc/bff-yarp-net10/Security/ProxyDestinationPolicy.cs - configuration-only destination allowlist, fails startup on violation
* poc/bff-yarp-net10/Security/ClaimsChallengeHandler.cs - bounded interaction-required result without automatic replay
* poc/bff-yarp-net10/Security/EvidenceCollector.cs - typed allowlist projection
* poc/bff-yarp-net10/Security/ForwardedHeadersPolicy.cs - trust boundary re-derived rather than inherited, no blanket proxy trust
* poc/bff-yarp-net10/Security/TenantPolicy.cs - issuer validation carried from the modern app
* poc/bff-yarp-net10/Security/BffAuthenticationSettings.cs - bound configuration
* poc/bff-yarp-net10/wwwroot/index.html, evidence.js, evidence.css - evidence presentation
* poc/bff-yarp-net10/Tests/Croesus.BffYarp.Tests.csproj - test project, pinned versions, project reference
* poc/bff-yarp-net10/Tests/TokenCustodyTests.cs - 6 custody tests including the decrypted-ticket assertion
* poc/bff-yarp-net10/Tests/ProxyBoundaryTests.cs - 10 boundary tests
* poc/bff-yarp-net10/Tests/SessionLifecycleTests.cs - 10 lifecycle tests
* poc/bff-yarp-net10/Tests/ServerTicketStoreTests.cs - 6 store tests
* poc/bff-yarp-net10/Tests/ProtocolNegativeTests.cs - 8 state, nonce, and replay tests
* poc/bff-yarp-net10/Tests/AudienceAndScopeTests.cs - 9 audience and scope tests with paired control-removal tests
* poc/bff-yarp-net10/Tests/EvidenceSurfaceTests.cs - 7 non-disclosure tests
* poc/bff-yarp-net10/Tests/BffFactory.cs, FakeTokenAcquisition.cs, RecordingDownstream.cs, TestConfiguration.cs, Usings.cs - test host and fakes

### Modified

* scripts/provision-app-registrations.sh - BFF confidential registration, API token version 2, API-only optional claims, Key Vault certificate helper
* scripts/evidence-kql.kusto - speculative queries replaced with operation-mapped, bounded, placeholder-parameterized queries

### Removed

None.

## Phase 1 Policy Evidence

Read-only evidence captured 2026-09-22 against subscription 64c3d212-40ed-4c6d-a825-6adfbdf25dad (ME-MngEnvMCAP675646-emknafo-1), tenant aa93b9d9-037d-4f08-a26d-783cff0e2369. No write operation was issued.

### Headline finding

No Azure Policy assignment enforces `publicNetworkAccess` on `Microsoft.Web/sites` at any scope reachable from this subscription. The user premise that Azure Policy prohibits public access on the POC App Services is not supported by the assignment, initiative, compliance-state, or exemption evidence below. DD-01, the earlier plan statement that no policy was enforcing `publicNetworkAccess`, is the statement the evidence supports. DR-01 is now resolved: attribution was attempted and the policy hypothesis was falsified, though the actual author of the setting remains unattributed because the activity log retention window contains no relevant write.

### Assignments at subscription scope

| Assignment name | Definition or initiative ID | Enforcement | Targets Microsoft.Web |
|---|---|---|---|
| SqlVmAndArcSqlServersProtection | policySetDefinitions/c1529623-9fc2-45bc-b84b-b14cd0b7484e | Default | No |
| OpenSourceRelationalDatabasesProtectionSecurityCenter | policySetDefinitions/e77fc0b3-f7e9-4c58-bc13-cb753ed8e46e | Default | No |
| SecurityCenterBuiltIn (ASC Default) | policySetDefinitions/1f3afdf9-d0c9-4c3d-847f-89da613e70a8 | Default | Audit only |
| tag-governance-assignment | custom policySetDefinitions/tag-governance-initiative | DoNotEnforce | No |
| tag-inheritance-assignment | custom policySetDefinitions/tag-inheritance-initiative | Default | No |
| Defender for Containers provisioning ARC k8s Enabled | policyDefinitions/708b60a6-d253-4fe0-9114-4be4c00f012c | Default | No |

### Assignments at tenant root management group scope

Scope: `/providers/Microsoft.Management/managementGroups/aa93b9d9-037d-4f08-a26d-783cff0e2369`. This is the only management group in the tenant, so this scope plus the subscription scope is the complete inherited set.

| Assignment name | Display name | Definition or initiative | Enforcement |
|---|---|---|---|
| MCAPSGovDenyPolicies | MCAPSGov Deny Policies | custom initiative MCAPSGovDenyPolicies | Default |
| MCAPSGovDeployPolicies | MCAPSGov Deploy and Modify Policies | custom initiative MCAPSGovDeployPolicies | Default |
| MCAPSGovAuditPolicies | MCAPSGov Audit Policies | custom initiative MCAPSGovAuditPolicies | Default |
| 503119ae423148fabbc3f2fd | Block Azure RM Resource Creation | policyDefinitions/067b5e1b-d1f2-4cac-8863-1993c410fa66 | Default |
| sys.mfa-write | MFA Enforcement for Resource Write Actions | policyDefinitions/54f51f64-eaa5-44cf-8674-830bcfd14d21 | Default |
| sys.mfa-delete | MFA Enforcement for Resource Delete Actions | policyDefinitions/f69cd8b8-9a6e-46b4-be84-244e8b127944 | Default |

The MCAPSGovDenyPolicies assignment carries `parameters: null` and `notScopes: null`, so every member definition resolves to its initiative default.

### Deny initiative members

MCAPSGovDenyPolicies contains VM SKU denials (three references), AKS node count, VMSS node count, Azure OpenAI provisioned capacity, Sentinel commitment, two Azure SQL Entra-only authentication denials, Key Vault Managed HSM purge protection, and the built-in `NotAllowedResourceTypes` definition 6c112d4e-5bc7-47ae-a041-ea2d9dccd749. No member targets `Microsoft.Web`.

### Modify initiative members relevant to public network access

MCAPSGovDeployPolicies contains `AIFoundryHub_PublicNetwork_Modify`, `StorageAccount_PublicNetwork_Modify`, `KeyVault_PublicNetwork_Modify`, `CosmosDB_PublicNetwork_Modify`, and `AzureSQL_PublicNetwork_Modify`. There is no `AppService_PublicNetwork_Modify` or any equivalent for `Microsoft.Web/sites`. The governance pattern of disabling public ingress exists in this tenant, but App Service was never brought into it by policy.

### Constraints on network resources

Neither enforced deny path blocks the resources Phase 2 would need.

* `NotAllowedResourceTypes` resolves to a list containing only classic (ASM) resource types: `microsoft.classiccompute/*`, `microsoft.classicnetwork/*`, `microsoft.classicstorage/*`, `microsoft.classicsubscription/*`, and `microsoft.classicinfrastructuremigrate/*`.
* Block Azure RM Resource Creation denies only seven classic types and is additionally gated on `resourceGroup().tags['ringValue']` matching a parameter list.
* No assignment denies `Microsoft.Network/virtualNetworks`, `Microsoft.Network/privateEndpoints`, `Microsoft.Network/privateDnsZones`, `Microsoft.Network/publicIPAddresses`, or `Microsoft.Network/virtualNetworkGateways`.
* No assignment constrains egress.

### Compliance state for croesus-bff-poc-rg

Only three distinct non-compliant results exist for this resource group.

* SecurityCenterBuiltIn definition 91a78b24-f231-4a8a-8da9-02c35b2b6510, effect `auditifnotexists`, against both `croesus-bff-a3v24wppuvd34-modern` and `croesus-bff-a3v24wppuvd34-legacy`.
* SecurityCenterBuiltIn definition 2b9ad585-36bc-4615-b300-fd4435808332, effect `auditifnotexists`, against both sites.
* tag-governance-assignment definition 96670d01-0a4d-4649-9c89-2d3abc0a5025, effect `deny`, against the resource group itself for required tags. The assignment enforcement mode is `DoNotEnforce`, so it records non-compliance without blocking.

No `deny` result of any kind evaluates against either App Service.

### Exemptions

`az policy exemption list --disable-scope-strict-match` returned an empty array. There are no exemptions to account for, and no exemption is masking a deny.

### Attribution attempt

No `Microsoft.Web/sites/write` record exists in the activity log for `croesus-bff-poc-rg` within a 30-day offset, and none exists for `rg-croesus` within an 89-day offset. The only records in `croesus-bff-poc-rg` are `publishxml` and `ListPublishingCredentials` actions on 2026-09-22. The setting therefore predates the activity log retention window, and the caller who set `publicNetworkAccess` to `Disabled` cannot be identified from Azure control-plane evidence.

The infrastructure-as-code does not set it either. `infra/poc/main.bicep` declares both site resources without any `publicNetworkAccess` property, so a redeployment of that template would not produce the observed state.

### Read-only commands executed

```powershell
az policy assignment list --disable-scope-strict-match --query "..." -o json
az policy assignment list --scope "<rg>" --disable-scope-strict-match -o table
az account management-group list -o json
az policy assignment list --scope "<managementGroup>" -o json
az policy assignment show --name MCAPSGovDenyPolicies --scope "<managementGroup>" -o json
az policy set-definition show --name MCAPSGovDenyPolicies --management-group "<mg>" -o json
az policy set-definition show --name MCAPSGovDeployPolicies --management-group "<mg>" -o tsv
az policy set-definition show --name MCAPSGovAuditPolicies --management-group "<mg>" -o tsv
az policy definition show --name 067b5e1b-d1f2-4cac-8863-1993c410fa66 --management-group "<mg>" -o json
az policy definition list --management-group "<mg>" -o json
az policy state list -g croesus-bff-poc-rg --filter "complianceState eq 'NonCompliant'" -o json
az policy exemption list --disable-scope-strict-match -o json
az monitor activity-log list -g croesus-bff-poc-rg --offset 30d -o table
az monitor activity-log list -g rg-croesus --offset 89d -o table
```

### Consequence for the plan

The plan objective "keep public network access disabled because Azure Policy prohibits it" rests on a premise the evidence contradicts. Private endpoints remain a defensible architecture on their own security merits, but they are now an elective design choice rather than a compliance obligation. Re-enabling public ingress on the POC apps is not blocked by any policy in this tenant. The engagement owner should confirm which of those two paths is intended before Phase 2 spends effort on private networking.

## Phase 1 Topology Inventory

### Pre-change baseline on both POC App Services

Both sites in `croesus-bff-poc-rg` (Canada East) report identical ingress posture.

| Property | croesus-bff-a3v24wppuvd34-modern | croesus-bff-a3v24wppuvd34-legacy |
|---|---|---|
| state | Running | Running |
| enabled | true | true |
| publicNetworkAccess (site) | Disabled | Disabled |
| publicNetworkAccess (siteConfig) | Disabled | Disabled |
| httpsOnly | true | true |
| netFrameworkVersion | v4.0 | v4.0 |
| virtualNetworkSubnetId | null | null |
| vnetRouteAllEnabled | false | false |
| ipSecurityRestrictions | Allow all, priority 2147483647 | Allow all, priority 2147483647 |
| scmIpSecurityRestrictions | Allow all, priority 2147483647 | Allow all, priority 2147483647 |

The IP rules are permissive defaults. They are not the cause of the 403, and they would not govern private-endpoint traffic if endpoints existed.

### Blocking discrepancy: the plan tier is Free, not Basic

`croesus-bff-a3v24wppuvd34-plan` is deployed as SKU `F1`, tier `Free`, capacity 0, hosting two sites in Canada East. The prior research reasoned throughout on the premise of a Windows Basic B1 plan, and `infra/poc/main.bicep` declares `name: 'B1'` with `tier: 'Basic'` and `capacity: 1`. Deployed state and declared state diverge.

This matters because the Free tier supports neither private endpoints nor regional VNet integration nor Always On. Every Phase 2 step that assumes an endpoint can be attached to these sites is unreachable until the plan is scaled to Basic or higher. The research statement that Basic B1 supports private endpoints is correct and is not in dispute; the deployed plan simply is not Basic. Phase 2 must add a plan SKU change as its first prerequisite, or the divergence between the template and the deployment must be explained before anything else proceeds.

### Virtual networks in the subscription

| VNet | Resource group | Location | Address space |
|---|---|---|---|
| croesus-vnet | rg-croesus | canadacentral | 10.10.0.0/16 |
| devbox-vm-001-vnet | rg-dev-vm-001 | canadacentral | 10.0.0.0/16 |
| vnet-examprep-jrerox25jsvva | rg-examprep-dev | canadacentral | 10.0.0.0/16 |
| vnet-air-canada-threat-assessment | rg-air-canada-threat-assessment-poc | eastus2 | 10.30.0.0/16 |
| vnet-desjardins-quote-preparation | rg-desjardins-quote-preparation | eastus2 | 10.20.0.0/16 |

`croesus-vnet` is the only candidate that is already shaped for this scenario. Its subnets are `snet-pe` at 10.10.1.0/24 with `privateEndpointNetworkPolicies` set to `Disabled`, and `snet-app` at 10.10.2.0/24 delegated to `Microsoft.Web/serverFarms`. Ownership is the Croesus workload itself, and it sits in the same subscription, so reuse needs no cross-team approval. No approval record was found in Azure, so approval status is asserted only by common ownership.

There are no peerings on `croesus-vnet` or on `devbox-vm-001-vnet`.

`devbox-vm-001-vnet` and `vnet-examprep-jrerox25jsvva` both use 10.0.0.0/16, so those two cannot be peered to each other without renumbering. Neither overlaps `croesus-vnet`.

### Region mismatch

The POC App Services are in Canada East. The only suitable VNet is in Canada Central. A private endpoint NIC lives in the VNet region, and Azure Private Link does allow an endpoint in one region to target a resource in another, so this is workable rather than fatal. It does add cross-region latency on every browser request and it should be named explicitly rather than discovered during Phase 2. Deploying a new Canada East VNet is the alternative, at the cost of a second network to own and a second DNS link to maintain.

### Private DNS

No `privatelink.azurewebsites.net` zone exists anywhere in the subscription. This zone must be created and linked, and there is no existing owner to inherit from.

Zones that do exist, for reference on prevailing convention: `privatelink.vaultcore.azure.net` in rg-croesus with one VNet link, plus `privatelink.documents.azure.com`, `privatelink.blob.core.windows.net`, `privatelink.queue.core.windows.net`, `privatelink.table.core.windows.net`, `privatelink.cognitiveservices.azure.com`, and `privatelink.openai.azure.com` across the other POC resource groups. Each zone is created in the resource group that owns the workload and linked to that workload's VNet. Following that convention places the new zone in the Croesus resource group.

### Browser access path: undetermined

No mechanism exists today to reach any VNet privately from a workstation.

* No VPN gateway in rg-croesus, rg-dev-vm-001, or rg-examprep-dev.
* No ExpressRoute gateway.
* No Azure Bastion host anywhere in the subscription.
* No VNet peering that would let an existing connected network reach `croesus-vnet`.

One Windows VM exists, `devbox-vm-001` in rg-dev-vm-001, Canada Central. It sits in a VNet that is not peered to `croesus-vnet`, so it cannot resolve or reach a private endpoint placed in `snet-pe` without either a peering or relocation.

Whether a VPN-connected or ExpressRoute-connected workstation exists outside Azure cannot be determined from Azure evidence. This is recorded as an open decision rather than a gap with a chosen fix. The decision owner is the engagement lead. The blocking dependency is a statement of what private connectivity the demo audience actually has.

Options, none selected: peer `devbox-vm-001-vnet` to `croesus-vnet` and use the existing VM as a jump host, carrying the caveat that the device context of that VM is not the device context of the reviewer's managed workstation and therefore weakens any Conditional Access narrative; deploy Azure Bastion into `croesus-vnet` and a new jump VM; deploy a VPN gateway and issue point-to-site profiles.

### Deployment runner: undetermined

`.github/workflows/classic-net-bff-poc.yml` line 275 sets `runs-on: windows-latest` for the deploy job. A GitHub-hosted runner has no route into a private VNet, so once the SCM hostname resolves to a private endpoint, this job fails at publish. Neither a self-hosted runner nor a GitHub larger runner with Azure private networking exists today, and neither can be verified from Azure. The decision owner is the engagement lead. The blocking dependency is whether self-hosted runner infrastructure may be introduced for this POC.

### Outbound dependency inventory

Derived from `infra/poc/main.bicep` app settings rather than from runtime observation.

| Destination | Required by | Present in POC today | Egress permitted |
|---|---|---|---|
| Microsoft Entra login endpoint, from `environment().authentication.loginEndpoint` | Both sites, for OIDC | Yes, as app setting | Yes, no policy constrains egress |
| Microsoft Graph | Not referenced by the POC templates | No | Not applicable |
| Key Vault | Not used by the POC; the client secret is passed as a plain app setting | No | Not applicable |
| Distributed cache backing store | Not provisioned; required only if the Phase 3 reference BFF adopts server-side token custody | No | Undetermined, depends on the store chosen |
| Application Insights ingestion | No component exists in croesus-bff-poc-rg; croesus-appi exists only in rg-croesus | No | Not applicable to the POC today |

A separate delegated VNet integration subnet is not required for ingress. It becomes required only if the reference BFF must reach a private downstream. `snet-app` is already delegated and already consumed by `croesus-api`, and a delegated subnet supports one plan at a time, so a second delegated subnet would be needed rather than reusing that one.

### Reference environment, for contrast

`rg-croesus` in Canada Central holds the non-POC Croesus environment: `croesus-asp` at SKU B1 tier Basic, `croesus-api`, `croesus-spa`, `kv-croesus-a65e90` with `croesus-kv-pe`, `croesus-appi`, `croesus-law`, `croesus-vnet`, and two subnet NSGs.

Both `croesus-api` and `croesus-spa` also report `publicNetworkAccess: Disabled`. `croesus-api` is VNet-integrated into `snet-app` with `vnetRouteAllEnabled: true`, which is outbound only. `croesus-spa` has no integration. The only private endpoint in that resource group is the Key Vault endpoint. Neither web app has an inbound private endpoint, so both are currently unreachable by the same mechanism as the POC apps.

This is the strongest available evidence on the disabled-ingress question: the pattern is applied by hand across four App Services in two resource groups, in a tenant whose governance initiatives disable public access on five other resource types but never on `Microsoft.Web`. It reads as an operator convention, not as policy enforcement.

### BFF hostnames recorded for downstream steps

Step 2.4 and Step 4.1 both read these values from here rather than from each other.

* Reference BFF, new dedicated site: `https://croesus-bff-a3v24wppuvd34-bff.azurewebsites.net`
* Existing modern comparison app: `https://croesus-bff-a3v24wppuvd34-modern.azurewebsites.net`
* Legacy comparison app: `https://croesus-bff-a3v24wppuvd34-legacy.azurewebsites.net`

Corrected by the orchestrator after Phase 4. The original record listed the `-modern` hostname as the reference BFF origin. That conflates two different applications: `-modern` is the existing comparison app provisioned at infra/poc/main.bicep line 38, and it already owns `/signin-oidc` on the shared registration through `modernCallbackUri` at line 45. The reference BFF is the separate conditional site that Step 2.4 provisions behind `deployBffSite`, so it needs its own origin or the two registrations collide on an identical redirect URI. Step 4.1 was corrected to match and the `-bff` suffix follows the same naming convention.

Naming derives from infra/poc/main.bicep around lines 36 to 38, where the suffix comes from `uniqueString(subscription().id, resourceGroup().id, namePrefix)`. The suffix `a3v24wppuvd34` is therefore stable for this subscription and resource group and will not drift on redeployment.

### Verdict on private endpoint scope for the reference BFF

Undetermined. Three unresolved dependencies each independently block a decision: the Free tier plan cannot host a private endpoint at all; no browser access path exists; and the Phase 1 policy evidence removes the compliance rationale that motivated private ingress in the first place. Build-and-test-only in CI remains viable and is unaffected by any of the three. The decision owner is the engagement lead.

## Phase 1 Legacy Readiness Gate

### Compile-time target

`poc/legacy-net452/LegacyNet452.csproj` line 4 declares `<TargetFramework>net452</TargetFramework>`. The project is SDK-style and uses `PackageReference`; there is no `packages.config`. `poc/legacy-net452/web.config` line 13 declares `<compilation debug="true" targetFramework="4.5.2" />`. The test project `poc/legacy-net452/Tests/LegacyNet452.Tests.csproj` also targets `net452`.

.NET Framework 4.5.2 reached end of support on 2022-04-26. The compile-time target is an out-of-support framework version.

### Installed runtime

Both App Services report `siteConfig.netFrameworkVersion: v4.0`. On Windows App Service this selects the 4.x CLR, and the worker runs whichever in-place 4.x runtime is installed on that worker image. .NET Framework 4.x releases are in-place upgrades sharing CLR 4.0, so an assembly compiled against 4.5.2 reference assemblies executes on the installed 4.8.x runtime. The installed runtime is therefore supported even though the compile-time target is not.

These two facts must not be collapsed. The site is not running an unsupported runtime. It is running supported code compiled against an unsupported target, which is a supply-chain and servicing-story problem rather than a live unpatched-runtime problem.

The worker OS version is not exposed by any read-only command available here, so the specific installed 4.8.x build could not be confirmed.

### Package support status

| Package | Version | Status |
|---|---|---|
| Microsoft.Owin.Host.SystemWeb | 4.2.3 | Supported |
| Microsoft.Owin.Security.Cookies | 4.2.3 | Supported |
| Microsoft.Owin.Security.OpenIdConnect | 4.2.3 | Supported |
| Microsoft.NETFramework.ReferenceAssemblies.net452 | 1.0.3 | Reference assemblies only, `PrivateAssets="all"`, not shipped |

No package in the legacy app is itself out of support. The single support defect is the target framework moniker.

### Legacy readiness verdict

Blocked for Phase 2 private ingress as currently targeted.

The blocker is not technical feasibility. It is authorization. Retargeting `poc/legacy-net452` from `net452` to a supported moniker is the obvious remedy and is a small change to a project with three OWIN package references and no `packages.config` to migrate. But two engagement guardrails constrain who may make that call.

* `assets/croesus-3way-session-findings.md` section 7 states that the unsupported-runtime finding, tracked as A7, must not be used as leverage in the escalation and deserves its own track.
* `assets/croesus-escalation-packet.md` section 4 states that the .NET Framework 4.5.2 support status will be raised separately through vendor risk review rather than through this escalation.

Retargeting the vendor-representative sample inside this engagement would pre-empt that separate track. Whether that is authorized is a decision for the engagement lead, not something this phase can settle from repository or Azure evidence.

Phase 2 should therefore wire only the modern app, and the reduction from the two-app goal should be recorded against a named owner rather than absorbed silently.

### Note on a 4.8.1 target

If retargeting is authorized, 4.8 is the safer choice and 4.8.1 is not. .NET Framework 4.8.1 is not installable on Windows Server 2016 or Windows Server 2019, is installable on Windows Server 2022, and is in-box on Windows Server 2025. The App Service Windows worker OS version behind this plan could not be read with the read-only commands available, so 4.8.1 availability on these workers is unconfirmed. Targeting `net48` avoids the question entirely, because 4.8 is present on every current App Service Windows worker image.

### Work items to unblock

1. Obtain an explicit decision from the engagement lead on whether `poc/legacy-net452` may be retargeted within this engagement, or whether it must remain unmodified pending the separate vendor risk review track.
2. If authorized, retarget both the application and test projects to `net48`, update `web.config` `targetFramework` to `4.8`, and rerun the existing legacy test suite before any ingress work.
3. If not authorized, record the two-app restoration goal as formally reduced to one app, with the owner and the review-track dependency named, and ensure no later artefact claims both apps were restored.

## Phase 1 Legacy Bridge Decision

### Authorization input

Whether `poc/legacy-net452` may be modified for this engagement is undetermined, and it is the same open decision that gates the Legacy Readiness Gate above. Every bridge mechanism worth considering requires verification code on the legacy side, so the bridge question cannot be answered ahead of the authorization question.

### Bypass reachability

Once private endpoints exist for both apps on a shared VNet, any client that can reach that VNet can reach the legacy app directly at its own hostname. Nothing in the current configuration prevents that.

* App Service IP restrictions do not govern private-endpoint traffic, so the permissive `Allow all` rules recorded in the Topology Inventory are irrelevant in both directions.
* The two apps would share one address space by construction, because a single private endpoint subnet is the intended design.
* The legacy app contains no mechanism to distinguish BFF-originated traffic from direct traffic. A search of `poc/legacy-net452/*.cs` for forwarded-header inspection, direct `Headers[...]` access, client-certificate handling, or anonymous-route markers returned no matches. The app authenticates its own users through OWIN OpenID Connect and knows nothing about an upstream proxy.

An unauthenticated or independently authenticated legacy route reachable from the private network is a finding on its own terms. It is recorded here regardless of the bridge outcome, because it does not depend on the bridge being built.

### Mechanism options, none selected

* Signed header contract. The BFF attaches a header carrying a signed assertion of the authenticated subject; the legacy app validates the signature and rejects anything unsigned or stale. Requires legacy-side verification code, key distribution, and replay defence through a nonce or short expiry. Cheapest to implement, weakest without strict network isolation, because a header is forgeable by any private client that can reach the app directly.
* Mutual TLS client certificate. The legacy app requires a client certificate and validates the thumbprint against an allowlist containing only the BFF. Requires `clientCertEnabled` on the site, certificate lifecycle management, and legacy-side thumbprint validation. Strongest of the three against forgery from the private network, because it binds the caller rather than the payload.
* Separate token audience. The BFF acquires a token whose audience is the legacy app and forwards it; the legacy app validates issuer, audience, and signature. Requires a second Entra registration and legacy-side token validation. Cleanest identity story, largest change to the legacy app, and it re-opens the middle-tier question that the engagement guardrails explicitly decline to mandate.

### Logout semantics

The legacy app maintains its own OWIN cookie session, independent of anything the BFF holds. A BFF logout that clears only BFF state leaves a live legacy session behind, and that is not a logout. Any bridge design must specify how the legacy session is terminated, and any evidence artefact must be honest about what a logout actually ended.

### Legacy bridge verdict

Bridge out of scope for now.

Legacy integration is recorded as blocked, not complete. No artefact produced by this plan may claim that the reference BFF fronts the legacy application, because no bridge exists and private networking does not create one. If Phase 3 proceeds, it proceeds against the modern app alone.

Decision owner: the engagement lead, in coordination with the vendor risk review track that owns the 4.5.2 finding. Blocking dependency: explicit authorization to modify the vendor-representative legacy sample, which is the same dependency that blocks the Legacy Readiness Gate.

This addresses DR-07 by recording the bridge and the bypass as unplanned-and-now-named rather than leaving them implicit.

## Phase 3 Reference BFF Application

Complete. A new self-contained project under poc/bff-yarp-net10 with 56 passing tests, built and verified without live Entra access. No existing source file was modified.

### Token custody, the correction this phase exists for

`SaveTokens` stays false at poc/bff-yarp-net10/Program.cs line 144, and `CookieAuthenticationOptions.SessionStore` is assigned the distributed `ServerTicketStore` at line 121. The browser therefore receives an opaque session reference rather than an encrypted ticket carrying tokens. `TokenCustodyTests.SignIn_DecryptedCookieTicket_CarriesOnlyASessionReference` decrypts the actual cookie and asserts this, rather than asserting a configuration flag.

### Step 3.7 executed its blocked branch

The Step 1.4 verdict was bridge out of scope, so no `LegacyBridgeTransform.cs` was written and no legacy cluster was added. Legacy integration is recorded as blocked, not complete. No artefact in this phase states or implies that the BFF fronts the legacy app.

The bypass-prevention requirement stands independently of that verdict: an unauthenticated legacy route reachable from a private network is a finding whether or not the bridge is ever built. It is unaddressed because Phase 2 is blocked, so no private reachability exists yet to prevent.

### Test coverage by step

* Step 3.2 token custody: 6 tests, including the decrypted-ticket and sentinel-secret assertions
* Step 3.3 proxy boundary: 10 tests covering credential stripping, antiforgery on state-changing methods, destination allowlisting, and downstream `Set-Cookie` suppression
* Step 3.4 lifecycle: 10 tests covering expiry, revocation, concurrency, store outage, interaction-required, and logout
* Step 3.5 evidence surface: 7 tests covering opaque-sentinel and JWT-shaped non-disclosure, `Cache-Control: no-store`, authenticated-session requirement, and absence of any On-Behalf-Of assertion
* Step 3.6 protocol negatives: 8 tests covering state mismatch, nonce mismatch and absence, correlation cookie absence, and callback replay
* Step 3.6 audience and scope: 9 tests against a test-hosted resource server with genuine `JwtBearer` validation
* `ServerTicketStore`: 6 tests covering restart survival, fail-closed on store outage, opaque keys, and bounded expiry

### Control-removal verification

Audience validation and the delegated-scope policy are each permanently paired with a control-removed test asserting the same token is admitted once the control is off, so the negative tests re-verify as non-vacuous on every run rather than only once by hand. Non-disclosure controls were verified by temporarily injecting sentinel and JWT-shaped values and confirming the tests fail, then reverting.

Two assertions were not verified by control removal and are recorded as such rather than claimed: `Evidence_SetsCacheControlNoStore` and `Evidence_RequiresAnAuthenticatedSession` assert observed HTTP behaviour without a paired removal test.

### Deviation from the details file

The details file named poc/bff-yarp-net10/Tests/AudienceAndScopeTests.cs and poc/bff-yarp-net10/Tests/ProtocolNegativeTests.cs only. The evidence non-disclosure tests required by the Step 3.5 success criteria had no named file, so they were placed in poc/bff-yarp-net10/Tests/EvidenceSurfaceTests.cs following the same convention.

The audience and scope tests run against a resource server hosted inside the test project rather than against api/Croesus.Api.csproj. This satisfies the Step 3.6 requirement that no test depend on live Entra access, and complementary coverage against the real API already exists in api/Tests/NegativeControlTests.cs.

## Phase 4 Registration and Evidence Queries

Complete. Script and query files only.

### Step 4.1 registration shape

A dedicated single-tenant confidential web registration for the BFF, placed between sections 2 and 3 of scripts/provision-app-registrations.sh so the documented ordering constraint holds structurally: the `access_as_user` scope PATCH still commits before the `preAuthorizedApplications` PATCH that references its identifier.

Both `implicitGrantSettings` flags are false, `spa.redirectUris` and `publicClient.redirectUris` are explicitly empty, `isFallbackPublicClient` is false, and `signInAudience` is `AzureADMyOrg`. The credential is a Key Vault certificate through a new `ensure_kv_cert_credential` helper, create-if-absent in the vault and attach-only-if-no-key-credential on the registration. No secret literal and no federated credential were introduced.

### Step 4.2 token version and claims

`requestedAccessTokenVersion: 2` folded into the existing `api` PATCH on the API registration so the audience value is deterministic. Optional claims `idtyp` and `xms_cc` are a separate PATCH on the API object only; the BFF client registration carries none. `groupMembershipClaims` is deliberately not written, with an inline comment recording that the earlier recommendation to set it to `None` was withdrawn under DD-07 because group claims are retained where authorization needs them.

### Step 4.3 evidence queries

Four queries replace the withdrawn set: a run-scoped membership test over observed per-operation identifiers rather than an assumed shared `CorrelationId`, tenant service principal object-identifier resolution rather than comparison against a globally known application identifier, per-policy Conditional Access expansion reporting evaluation mode alongside result, and a separate Token Protection exhibit. Fourteen placeholder tokens and four bounded time windows. An assertion audit over the file returns only negative statements, the release-gate disclaimer, and factual event names.

### Correction applied by the orchestrator

`BFF_BASE_URI` defaulted to the `-modern` hostname, which is the existing comparison app rather than the reference BFF. Registering the BFF's `/signin-oidc` there would collide with `modernCallbackUri` at infra/poc/main.bicep line 45 on a different registration object. The default now points at `https://croesus-bff-a3v24wppuvd34-bff.azurewebsites.net` and the variable documentation states which site it refers to and when to override it. `bash -n` re-verified at exit 0 after the edit.

## Additional or Deviating Changes

* Phase 1 falsified the plan's founding premise rather than confirming it
  * Step 1.1 was written expecting to record an enforcing policy assignment. It recorded a verified absence instead, across six subscription-scope assignments and six tenant-root management-group assignments, with an empty exemption list.
  * The plan's first user requirement, that public access stays disabled because Azure Policy prohibits it, is not supported by the tenant evidence. The disabled state appears to be operator convention, applied consistently across four App Services in two resource groups.
  * Independently corroborated by the orchestrator before escalation.
* Phase 2 is blocked before it began, on a prerequisite the plan did not anticipate
  * The deployed plan croesus-bff-a3v24wppuvd34-plan is F1 Free. Free tier supports neither private endpoints nor VNet integration.
  * infra/poc/main.bicep declares B1 Basic, so the deployed resources diverge from the template in this repository. The research finding that Basic B1 supports private endpoints remains correct and is not the error.
  * No privatelink.azurewebsites.net zone, VPN gateway, ExpressRoute gateway, Bastion host, or peering exists anywhere in the subscription, so no private browser or runner path exists today.
* Step 1.3 returned a blocked verdict on the legacy app
  * Compile-time target net452 reached end of support on 2022-04-26. The installed runtime is supported, but the compile-time target is not.
  * Per the plan, this is a recorded gate outcome rather than a silent reduction of the two-app scope.
* Step 1.4 returned an out-of-scope verdict on the legacy identity bridge
  * Phase 3 Step 3.7 therefore executes its blocked branch, and no artefact may claim the BFF fronts the legacy app.
  * The bypass-prevention requirement is recorded independently of that verdict.

## Release Summary
