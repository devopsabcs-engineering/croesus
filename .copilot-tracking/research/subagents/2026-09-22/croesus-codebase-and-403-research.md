<!-- markdownlint-disable-file -->
# Subagent Research: Croesus Codebase Inventory and POC 403 Root Cause

Date: 2026-09-22
Workspace: c:\src\GitHub\devopsabcs-engineering\croesus
Status: **Complete** for Q2, Q3, Q4, Q5, Q6, Q7, Q8, Q9. **Substantially complete** for Q1 (all files read or characterized; a few long-form docs summarized rather than quoted exhaustively).

---

## HEADLINE ANSWER (Q4 — the 403)

Both App Services return **HTTP 403 "Web App - Unavailable"** because **`publicNetworkAccess` is set to `Disabled` on both sites while no private endpoint exists**. The sites are therefore unreachable from every network path.

This setting is **NOT in the repository's Bicep**. It is **configuration drift** applied to the live resources after deployment.

Verbatim live config (read-only `az webapp show`, 2026-09-22):

```json
// croesus-bff-a3v24wppuvd34-legacy
{
  "availabilityState": "Normal",
  "clientCertEnabled": false,
  "clientCertMode": "Required",
  "enabled": true,
  "httpsOnly": true,
  "netFrameworkVersion": "v4.0",
  "publicNetworkAccess": "Disabled",
  "state": "Running",
  "usageState": "Normal"
}
```

```json
// croesus-bff-a3v24wppuvd34-modern
{
  "availabilityState": "Normal",
  "clientCertEnabled": false,
  "clientCertMode": "Required",
  "enabled": true,
  "httpsOnly": true,
  "publicNetworkAccess": "Disabled",
  "state": "Running",
  "usageState": "Normal"
}
```

Live HTTP probe:

```text
legacy=403
modern=403
<!DOCTYPE html>
<html>
<head>
    <title>Web App - Unavailable</title>
```

`Web App - Unavailable` is the Azure App Service **front-end / ARR network-layer** rejection page. It is served *before* the worker process, which is why it is identical on a .NET Framework site and a self-contained .NET 10 site, and why it is unaffected by application code, Easy Auth, or deployment content.

**Remediation (single command per app):**

```powershell
az webapp update -g croesus-bff-poc-rg -n croesus-bff-a3v24wppuvd34-legacy --set publicNetworkAccess=Enabled
az webapp update -g croesus-bff-poc-rg -n croesus-bff-a3v24wppuvd34-modern --set publicNetworkAccess=Enabled
```

---

## Q4 detail — ranked hypotheses with evidence for and against

### H1 (CONFIRMED) — `publicNetworkAccess: 'Disabled'` with no private endpoint

* **For:** Both sites report `"publicNetworkAccess": "Disabled"` verbatim (above). `az resource list -g croesus-bff-poc-rg` returns **only three resources** — the plan and the two sites — so there is **no private endpoint, no VNet, no private DNS zone**:

  ```json
  [
    { "name": "croesus-bff-a3v24wppuvd34-plan",   "type": "Microsoft.Web/serverFarms" },
    { "name": "croesus-bff-a3v24wppuvd34-modern", "type": "Microsoft.Web/sites" },
    { "name": "croesus-bff-a3v24wppuvd34-legacy", "type": "Microsoft.Web/sites" }
  ]
  ```

  `az webapp show ... --query "privateEndpointConnections"` returned **empty**. Public access disabled + zero private endpoints = no reachable ingress path at all.
* **For:** The response body is the App Service platform `Web App - Unavailable` page, which is the documented rendering for network-layer denial, not an application response.
* **For:** Identical symptom on both apps despite completely different runtimes and packages — points to a platform/site-level network control, not app code.
* **Against:** Nothing. This is dispositive.

### H2 (RULED OUT) — `ipSecurityRestrictions` default-deny

`az webapp config access-restriction show` on **both** apps returns an explicit allow-all and no deny rule:

```json
{
  "ipSecurityRestrictions": [
    {
      "action": "Allow",
      "description": "Allow all access",
      "ipAddress": "Any",
      "name": "Allow all",
      "priority": 2147483647
    }
  ],
  "ipSecurityRestrictionsDefaultAction": null,
  "scmIpSecurityRestrictions": [
    {
      "action": "Allow",
      "description": "Allow all access",
      "ipAddress": "Any",
      "name": "Allow all",
      "priority": 2147483647
    }
  ],
  "scmIpSecurityRestrictionsDefaultAction": null,
  "scmIpSecurityRestrictionsUseMain": false
}
```

There is no `ipSecurityRestrictions` block anywhere in `infra/poc/main.bicep` either. **Not the cause.**

### H3 (RULED OUT) — Easy Auth `authsettingsV2` blanket 403

`az webapp auth show` on both apps returns `"enabled": false` with `"configVersion": "v1"` and every provider field `null`:

```json
{
  "configVersion": "v1",
  "enabled": false,
  "clientId": null,
  "defaultProvider": null,
  "issuer": null,
  "name": "authsettings",
  ...
}
```

Easy Auth is **off**. Authentication in this POC is implemented **in the application** (OWIN/Katana on legacy, Microsoft.Identity.Web on modern), not by the platform. `infra/poc/main.bicep` contains **no** `Microsoft.Web/sites/config` resource named `authsettingsV2`. **Not the cause.**

### H4 (RULED OUT) — client certificate required

`clientCertMode` reads `"Required"` but `clientCertEnabled` is `false`. When `clientCertEnabled` is false the mode is **inert** — App Service does not request or require a client certificate. Also, a client-cert failure would produce a TLS handshake failure or `403.7`, not the `Web App - Unavailable` front-end page. **Not the cause.**

### H5 (RULED OUT) — Azure Policy forcing the setting

`az policy state list -g croesus-bff-poc-rg` shows only:

* `deny` effect on definition `96670d01-0a4d-4649-9c89-2d3abc0a5025` = **"Require a tag on resource groups"** (confirmed by `az policy definition show`) — a tagging control, scoped to the resource group, unrelated to networking.
* `modify` effect from assignment `tag-inheritance-assignment` (definition `ea3f2387-9b95-492a-a190-fcdc54f7b070`) — tag inheritance, **Compliant**.
* Two `auditIfNotExists` definitions (`91a78b24-...`, `2b9ad585-...`) — audit-only, cannot mutate.

`az policy assignment list --disable-scope-strict-match` filtered for network-related assignments returned `[]`. **No policy is enforcing `publicNetworkAccess`.** The setting was therefore applied manually or by an out-of-band process.

### H6 (RULED OUT) — empty wwwroot / missing deployment

An undeployed Windows App Service returns the **"Your web app is running and waiting for your content"** default page with `200`, or a `404`. It does not return `403 Web App - Unavailable`. Additionally, the deploy workflow's `Verify authentication redirects` step (`.github/workflows/classic-net-bff-poc.yml`, around lines 435-490) asserts an auth challenge with a matching `client_id` and `redirect_uri`, so content was present at deploy time. **Not the cause.**

### H7 (RULED OUT) — `httpsOnly` / `ftpsState`

Both apps have `httpsOnly: true`; the probe used `https://`, so no HTTP→HTTPS redirect was involved. `ftpsState: 'Disabled'` (`infra/poc/main.bicep` lines ~78 and ~131) affects FTP publishing only, not HTTPS ingress. **Not the cause.**

---

## Q3 — Deployment topology of the two POC apps

### Source of truth: `infra/poc/main.bicep`

Deterministic naming (lines ~34-38):

```bicep
var uniqueSuffix = uniqueString(subscription().id, resourceGroup().id, namePrefix)
var appServicePlanName = 'croesus-bff-${uniqueSuffix}-plan'
var legacyAppName = 'croesus-bff-${uniqueSuffix}-legacy'
var modernAppName = 'croesus-bff-${uniqueSuffix}-modern'
```

This matches the live names exactly (`a3v24wppuvd34` is the resolved `uniqueString`).

Plan (lines ~53-66): one **Windows B1 Basic** plan, `kind: 'app'`, `reserved: false` (Windows), capacity 1 — **both apps share one worker**.

**Legacy app** (lines ~68-118): `netFrameworkVersion: 'v4.0'`, `alwaysOn: true`, `ftpsState: 'Disabled'`, `minTlsVersion: '1.2'`, `httpsOnly: true`. App settings are flat Web.config-style keys: `ClientId`, `TenantId`, `AuthorityMode`, `AllowedTenantIds`, `ClientSecretEnvironmentVariable` (value `APPSETTING_CROESUS_LEGACY_CLIENT_SECRET`), `CROESUS_LEGACY_CLIENT_SECRET` (the `@secure()` param), `RedirectUri`, `PostLogoutRedirectUri`.

**Modern app** (lines ~120-160): `appCommandLine: 'Croesus.ModernBff.exe'` (self-contained `win-x64`), `use32BitWorkerProcess: false`, same `ftpsState`/`minTlsVersion`/`httpsOnly`/`alwaysOn`. App settings use ASP.NET Core double-underscore binding: `ASPNETCORE_ENVIRONMENT=Poc`, `AzureAd__Instance`, `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__ClientSecret`, `AzureAd__CallbackPath=/signin-oidc`, `Authentication__Mode`, `AllowedHosts`, plus an indexed `Authentication__AllowedTenantIds__<n>` array built at lines ~48-51.

**Redirect URIs** (lines ~42-43):

```bicep
var legacyCallbackUri = '${legacyBaseUrl}/signin-oidc'
var modernCallbackUri = '${modernBaseUrl}/signin-oidc'
```

i.e. `https://croesus-bff-a3v24wppuvd34-legacy.azurewebsites.net/signin-oidc` and the `-modern` equivalent. Both are converged onto **one shared confidential Entra `web` registration**.

**Critically: `infra/poc/main.bicep` sets NONE of** `publicNetworkAccess`, `ipSecurityRestrictions`, `clientCertEnabled`, `clientCertMode`, `scmIpSecurityRestrictionsUseMain`, or `authsettingsV2`. A repo-wide grep for those tokens returns hits only in `infra/main.json` (line 889, the Key Vault), `infra/modules/keyvault.bicep` (line 61), and `infra/modules/monitoring.bicep` (lines 41-42) — all belonging to the **separate** OBO-demo stack, not the BFF POC.

### Workflow: `.github/workflows/classic-net-bff-poc.yml`

Two jobs. `validate` (line 66) builds and tests both proofs (lines 122-137), publishes packages (line 138), compiles Bicep (line 163), runs PowerShell static suites including `scripts/test-classic-net-bff-deployment-entra-static.ps1` (line 190), and optionally runs `az deployment group what-if` (line 226) only when the RG already exists. `deploy` (line 271) enters the protected `poc-demo` environment, logs in with OIDC (line 287), creates the RG (line 294), resolves names via what-if (line 314), runs `scripts/provision-classic-net-bff-deployment.ps1` + `az deployment group create` in one process (lines 352-418) with the client secret held only in the process environment and cleared in `finally`, then deploys both packages with `azure/webapps-deploy@v3` (lines 423-433), then `Verify authentication redirects` (line 435).

`teardown-classic-net-bff-poc.yml` performs guarded deletion requiring a `destroy:<name-prefix>` confirmation.

Per `docs/classic-net-bff-poc.md`: the legacy package stays compiled for `net452` but runs on App Service's installed .NET Framework **4.8** runtime; the modern package is self-contained `win-x64` because platform runtime availability lags SDK releases. Caveats section is explicit that B1 is a demo tier with no slots, HA, monitoring, or private networking — which corroborates that `publicNetworkAccess: Disabled` was never an intended part of this design.

---

## Q2 — The "unbound" issue and the "token replay" concern

### What the customer observed

From `assets/app-registration-analysis-findings.md` lineage, summarized in `.copilot-tracking/plans/logs/2026-06-15/app-registration-analysis-log.md` line 62:

> both rows share SessionId 007bc799-6350-25bf-fbe9-ebbcb7093b63 and deviceId 5b4b24f4-4540-46a6-a4cf-4518e81f06c8; the interactive row is Token Protection "bound" (code 0) from Desjardins IP 142.195.80.133, the non-interactive row is "unbound" (code 1008) from AWS IP 3.97.32.113, both ResultType 0 (success)

So: one interactive user sign-in from a Desjardins IP marked **bound (0)**, immediately followed by a **non-interactive** sign-in from a **Croesus AWS IP (3.97.32.113)** marked **unbound (1008)**, targeting **Microsoft Graph**.

### What "1008" actually means (the corrected position)

`README.md` line 33:

> The `1008` "unbound" line means the client is **not integrated with the platform broker** (Windows Account Manager), a device- and session-binding status. It does not identify the grant or prove that an access token was replayed.

`assets/croesus-escalation-packet.md` line 60:

> We want to be precise about what our logs do and do not prove. The blocked event shows Token Protection status "unbound" (code 1008). That status means the client is **not integrated with the platform broker** (Windows Account Manager) — a device- and session-binding signal — and does **not** by itself prove that an access token was replayed, nor does it identify the OAuth grant. The grant remains unclassified from a captured request, and we are not asserting one.

`docs/evidence-narrative.md`, "Token Protection 1008: a labeled Tier 2b exhibit, not the OBO proof":

> Token Protection token binding, and therefore the 1008 "unbound" signal, applies to native-application clients reaching specific resources: Exchange Online, SharePoint Online, and Teams. It does not fire for a browser-based SPA calling a custom API that then calls Microsoft Graph.

and:

> `1008` is out of Token Protection scope for any flow reaching Microsoft Graph and is not replay evidence. Token Protection supports native applications only and does not cover Microsoft Graph, so an `Unbound (1008)` line against a sign-in that later reaches Graph is expected and benign. Keep that statement in view so the analysis does not drift back to treating `1008` as proof of token replay.

`README.md` line 40 (V1 finding row) restates it as "Medium (unclassified)".

### The current repo-documented root-cause hypothesis

Not "replay". The **client-boundary / registration-shape** problem. From `assets/croesus-escalation-packet.md` section 1:

> Your three exports are declared under the Entra `spa` (public client) platform. If the same backend redeems the authorization code and retains tokens, that backend must instead authenticate as a confidential `web` client. Entra rejects a plain server-side redemption of a `spa` authorization code with `AADSTS9002327`.

And the resulting fork, stated in `package.json` `description` (the mirrored README callout):

> exactly one of these is true: (a) the redemption **fails**, and Central needs a **`web` confidential-client** registration; or (b) the redemption **succeeds**, which means the backend authenticates as a **confidential registration nobody has shown us** — beyond the three `spa` exports we hold.

`docs/evidence-narrative.md` names the two surviving explanations:

> the backend synthesises an `Origin` header on a server-to-server call, or it authenticates as a **`web` registration outside the three exports we hold**.

### The word "unbound" also names the demo anti-pattern

`.copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md` line 2 titles the work "Reproduce the Croesus Unbound Token-Replay Anti-Pattern Behind the 1008 Finding"; lines 115-120 define:

> "1008" is the Microsoft Entra **Token Protection** token-binding status in the raw sign-in logs: **0 = bound, 1008 = unbound**.

and line 129 gives the mechanical reason the vendor cannot do OBO:

> credential, the app **cannot** do OBO even if it wanted to — so it replays the

### Guardrail in force

`assets/croesus-escalation-packet.md` section 4: *"We will not change Conditional Access, allowlist AWS public IPs, or mandate OBO before the registration shape and the grant are settled."*

---

## Q1 — Repo inventory (relevant to the BFF POC)

| Path | What it is |
| --- | --- |
| `docs/classic-net-bff-poc.md` | How-to for the two-runtime BFF comparison. Architecture (one confidential `web` registration, two exact callbacks `https://localhost:44352/signin-oidc` and `https://localhost:7100/signin-oidc`), a Mermaid sequence diagram of server-side code redemption, prerequisites, the App Service deployment path, the GitHub OIDC bootstrap-paradox warning, credential rotation (max 7 days), browser comparison steps, multitenancy, teardown, and the "Hosted PoC caveats" list. Explicit: `net452` is "evidence code, not an Internet-facing or production destination". |
| `docs/configuration-contract.md` | Authoritative single-source catalog of every non-secret config value, mapped to SPA build var / API app setting / Bicep param / `vars.*`. Two governing rules: public values live as GitHub Actions variables; the API confidential-client credential is a **Key Vault certificate** only. Includes the `Demo:EnableReplay` and `VITE_ENABLE_REPLAY_DEMO` toggles (default `false`) and a note that the real scope enforcement is the `[RequiredScope("access_as_user")]` attribute, not the `AzureAd:Scopes` setting. |
| `docs/evidence-narrative.md` | Maps the six escalation questions (Q1-Q6) to the concrete evidence the OBO demo produces. Headline proof = **audience binding**, explicitly *not* code 1008. Contains the "Registration correctness" and "Token Protection 1008: a labeled Tier 2b exhibit" sections quoted above, plus the `AADSTS9002327` citation and the honest caveat that revoking a delegated grant does not invalidate already-issued access tokens. |
| `docs/obo-demo-guide.md` | Operational walk-through that produces the evidence (referenced from `evidence-narrative.md` steps 5, 7, 8). |
| `assets/croesus-escalation-packet.md` | The customer-facing packet. Section 0 = what Croesus has confirmed (A-F), section 1 = framing, section 2 = **Q1-Q15** question table (priority Q8 → Q7 → Q15 → Q10 → Q13), section 3 = evidence to request with redaction rules, section 4 = what Desjardins will do (short-term VPN/parity levers vs durable registration correction). |
| `assets/croesus-3way-session-findings.md` | The authoritative working brief. Sections: what the session established (incl. "The decisive one"), what changed (A4, .NET 4.5.2 impact), what is still open, routes **R1-R9** (R1 VPN, R2 tenant parity, R3 AWS named location, R4 registration shape, R5 workload identity federation, R6/R7 identity-model changes, R8 framework uplift, R9 credential ladder), Experiments A and B, recommended sequence, guardrails, and the explicit Conseiller boundary. `docs/classic-net-bff-poc.md` states the PoC implements the **R8 and R9** proof path. |
| `assets/app-registration-analysis.md`, `-findings.md`, `-verification.md` | The original three-registration analysis, its findings (the bound/unbound contrast, AWS egress IPs), and the verification steps. `-findings.md` is cited throughout as "the 'wrong' (token replay) baseline being contrasted". |
| `assets/latest-info/email-thread-with-croesus.md`, `mathieu-santerre.md` | Correspondence artifacts. Mathieu Santerre (Desjardins) is credited in `package.json` as the capturer of the browser HAR containing only `/authorize` and no `/token`. |
| `assets/dev-dev.txt`, `dev-prod.txt`, `prod-prod.txt` | **Not token captures.** These are **Microsoft Graph application-registration JSON exports** (see below). |
| `scripts/evidence-kql.kusto` | KQL queries against Entra sign-in logs. Query 4 is the Token Protection `signInSessionStatusCode == "1008"` query cited in `docs/evidence-narrative.md`. |

### `assets/*.txt` — what they actually contain and prove

All three are **Graph `application` objects** (not sign-in logs, not tokens). Structure confirmed by reading `assets/dev-dev.txt` in full and grepping the discriminating fields across all three.

`assets/dev-dev.txt` lines 9-20, 44-51, 86-92:

```json
 "displayName": "sp-CentralGPD-UAT-dev-fed",
 "identifierUris": [],
 "isFallbackPublicClient": null,
 "signInAudience": "AzureADMyOrg",
 ...
 "api": { "oauth2PermissionScopes": [], "preAuthorizedApplications": [] },
 "keyCredentials": [],
 "passwordCredentials": [],
 "publicClient": { "redirectUris": [] },
 "web": { "redirectUris": [], "implicitGrantSettings": { "enableAccessTokenIssuance": false, "enableIdTokenIssuance": true } },
 "spa": {
  "redirectUris": [
   "https://spsfondation.dev.desjardins.com/affwebservices/tools/oidc-tool.html",
   "https://pat-gpd-central.certif.desjardins.com/CentralWebApp/LogonSso.aspx"
  ]
 }
```

Grep across all three (`assets/dev-dev.txt`, `dev-prod.txt`, `prod-prod.txt` — lines 9, 12, 15, 20, 33, 44, 49, 51, 72, 88 in each) shows an **identical shape**:

* `displayName`: `sp-CentralGPD-UAT-dev-fed` (dev-dev, dev-prod) and `sp-CentralGPD-prod-fed` (prod-prod)
* `signInAudience: "AzureADMyOrg"` — single tenant, all three
* `identifierUris: []` — **no Application ID URI**
* `api.oauth2PermissionScopes: []` — **no exposed API scope**
* `keyCredentials: []` and `passwordCredentials: []` — **no certificate, no client secret**
* `web.redirectUris: []` and `publicClient.redirectUris: []` — **empty**
* `spa.redirectUris: [ ... ]` — **populated**; redirect URIs live only under the `spa` node
* `requiredResourceAccess`: Microsoft Graph (`00000003-0000-0000-c000-000000000000`), delegated scope `e1fe6dd8-ba31-4d61-89e7-88639da4683d` (`User.Read`)

**What they prove:** all three registrations are declared as **`spa`-platform public clients with no credential and no exposed API**. Therefore (a) a standards-compliant OBO exchange is **structurally impossible** for these registrations, and (b) a plain server-side `/token` redemption against them should be rejected with `AADSTS9002327`. Since Prod works, either a fourth undisclosed `web` registration exists or the backend synthesizes an `Origin` header — exactly the fork the escalation packet's Q8 targets.

These are pinned by automated tests — `api/Tests/RegistrationShapeTests.cs` lines 7-9:

> Reads the captured Microsoft Entra application-registration exports (`assets/*.txt`) and asserts structural facts about them. These tests do not run the API; they pin what the exported registrations actually contain so the evidence narrative cannot silently drift from the captured artifacts

---

## Q5 — `api/` project shape

`api/Program.cs` lines 17-29 — the OBO "good path" chain:

```csharp
// Microsoft.Identity.Web fluent chain (correct-by-construction OBO):
//   AddMicrosoftIdentityWebApi          -> validates the inbound API token (issuer, audience, signature).
//   EnableTokenAcquisitionToCallDownstreamApi -> turns on the On-Behalf-Of token machinery.
//   AddMicrosoftGraph                   -> a GraphServiceClient that performs the OBO exchange and /me call.
//   AddInMemoryTokenCaches              -> caches the acquired downstream (Graph) tokens.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddMicrosoftGraph(builder.Configuration.GetSection("Graph"))
        .AddInMemoryTokenCaches();
```

The **reversible replay gate**, `api/Program.cs` lines 56-68:

```csharp
// Reversible Tier 2 gate: the deliberate token-replay endpoint is only MAPPED when Demo:EnableReplay is
// true. When false (the default), an application feature provider removes ReplayController from the MVC
// model so its route is absent entirely (POST /api/replay -> 404), rather than present-but-refusing.
var configuration = builder.Configuration;
builder.Services
    .AddControllers()
    .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(
        new ExcludeControllerFeatureProvider(
            typeof(ReplayController),
            () => !configuration.GetValue<bool>("Demo:EnableReplay"))));
```

CORS (lines 33-47) allows only `Cors:AllowedOrigins` with methods `GET, POST` — POST exists solely for the gated replay endpoint. `api/appsettings.json` line 22 has `"EnableReplay": false`.

### What the replay endpoint does

`api/Controllers/ReplayController.cs`:

* Attributes (lines 27-30): `[Authorize][ApiController][Route("api/[controller]")][RequiredScope("access_as_user")]` — same security surface as `MeController`. Even when enabled it requires a valid **API-audienced** token carrying `access_as_user`.
* Line 33: `private const string FixedGraphTarget = "https://graph.microsoft.com/v1.0/me";` with the comment *"The replay target is fixed by the server. A caller-supplied target is never read or honored."*
* Body is `{ "graphToken": "..." }` only. Missing token → `400 missing_token`.
* It decodes only a bounded claim set (`aud`, `scp`, `jti`, `iat`, `cnf`-presence) via `DecodeNonSensitiveClaims`, then deliberately re-presents the forwarded Graph token to the fixed target with a `Bearer` header — "exactly the motion an audience-bound / token-protected environment must reject."
* `Interpret(status, ok)` maps outcomes: `200` → Graph **accepted** the replay (token not audience-bound here; a hardened/CA-1008 config would reject); `401` → *"the expected outcome when the token is bound and cannot simply be replayed"*; `403` → *"consistent with a policy or binding control blocking the replay"*; `0` → transport failure.
* Evidence is emitted through `OboClaimLogger.LogReplayAttempt` as a distinct `ReplayAttempt` App Insights event. The raw token is **never** returned or logged.

### What the tests assert

`api/Tests/ReplayEndpointTests.cs` (lines 24, 49-93) — three pinned behaviors:

* Line 24: *"With `Demo:EnableReplay=false` the route is ABSENT (404) — the gate is truly reversible."* → `Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)` (line 60).
* Unauthenticated call when enabled → `Assert.Equal(HttpStatusCode.Unauthorized, ...)` (line 70).
* Authorized call → `HttpStatusCode.OK` (line 90) **and** `Assert.DoesNotContain(forwardedGraphToken, body)` (line 93) — the forwarded token must never appear in the response.

`api/Tests/NegativeControlTests.cs` lines 20-28 — runs the real middleware with a swapped JWT trust anchor and asserts *"only what is provable without contacting Microsoft Entra or Microsoft Graph"*; the API-token-to-Graph direction is *"a fact about the two audience strings; it does not contact Graph or prove a live Graph rejection."*

`api/Tests/MeControllerTests.cs` lines 34-37 — asserts the **shape** of the two-leg evidence (leg 1 inbound API-audience, leg 2 downstream Graph-audience) and the projected profile, deliberately **not** stub-controlled values.

`api/Tests/LiveRedemptionTests.cs` lines 12-76 — opt-in, **skipped by default** via a custom `LiveRedemptionFactAttribute` that sets `Skip` in its constructor so results are *Skipped, never Failed*. It tests falsifiable assertions 1-5 about `spa`-vs-`web` code redemption, reading `CROESUS_WEB_CLIENT_ASSERTION` etc. from the environment — never from source.

`api/Tests/RegistrationShapeTests.cs` — pins the `assets/*.txt` structure (above).

---

## Q6 — `poc/legacy-net452/` and `poc/modern-net10/`

### `poc/legacy-net452/`

Contents: `Authentication/`, `Configuration/`, `Telemetry/`, `Web/`, `Tests/`, `Properties/`, `Startup.cs`, `LegacyNet452.csproj`, `web.config`, `README.md`.

Auth stack: **OWIN / Katana** — `Microsoft.Owin.Security.OpenIdConnect` with cookie authentication. **Not** MSAL, **not** ADAL, **not** `Microsoft.Identity.Web`.

`poc/legacy-net452/Startup.cs` lines 20-42:

```csharp
public void Configuration(IAppBuilder app)
{
    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
    var settings = LegacyAuthenticationSettings.LoadAndValidate();
    ConfigureApplication(app, settings);
}
...
    UseAuthenticationExceptionBoundary(app);
    app.SetDefaultSignInAsAuthenticationType(CookieOptionsFactory.AuthenticationType);
    app.UseCookieAuthentication(CookieOptionsFactory.Create());
    app.UseOpenIdConnectAuthentication(oidcOptions);
    app.UseStageMarker(PipelineStage.Authenticate);
```

The explicit `ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12` on line 22 is the concrete answer to escalation **Q14** (4.5.2 does not negotiate TLS 1.2 by default).

Grant flow: **authorization code with PKCE**, redeemed server-side by Katana's native middleware. `docs/classic-net-bff-poc.md` states: *"The legacy middleware uses native Katana PKCE and code redemption. It does not contain custom PKCE glue or a custom verifier store."*

Endpoints (lines 44-90): `/signin` issues an OIDC challenge; `/api/session` returns `401` when unauthenticated, otherwise a JSON `SessionProjection.FromIdentity(identity)` with `Cache-Control: no-store`; `/` serves a minimal HTML page that `fetch`es `/api/session` with `credentials: 'same-origin'`. No token is ever placed in the page.

`UseAuthenticationExceptionBoundary` (lines 94-123) catches exceptions, calls `AuthenticationEventLogger.AuthenticationFailed(exception)`, and returns a generic `500 "Authentication could not be completed."` — no exception detail leaks.

### `poc/modern-net10/`

Contents: `Models/`, `Security/`, `Tests/`, `Properties/`, `Program.cs`, `Croesus.ModernBff.csproj`, `appsettings.json`, `web.config`, `README.md`.

Auth stack: **Microsoft.Identity.Web** (`AddMicrosoftIdentityWebApp`) over ASP.NET Core cookie + OIDC handlers.

`poc/modern-net10/Program.cs` lines 17-20 and 50-57:

```csharp
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
...
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.SaveTokens = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.IssuerValidator = tenantPolicy.ValidateIssuer;
```

`SaveTokens = false` is load-bearing for the BFF claim — no OAuth token is retained in the auth cookie.

Cookie hardening, lines 34-46:

```csharp
options.Cookie.Name = "__Host-Croesus.ModernBff.Session";
options.Cookie.HttpOnly = true;
options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
options.Cookie.SameSite = SameSiteMode.Lax;
options.Cookie.Path = "/";
options.Cookie.Domain = null;
options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
options.SlidingExpiration = false;
```

Antiforgery (lines 22-28) uses `__Host-Croesus.ModernBff.Antiforgery`, `HttpOnly`, `Always`, `SameSite=Strict`. `IdentityModelEventSource.ShowPII = false` (line 16). `OnTokenValidated` (lines 58-79) applies `TenantPolicy.ValidatePrincipal` and calls `context.Fail("Tenant validation failed.")` on a `SecurityTokenValidationException`. `OnRemoteFailure` (lines 81-92) routes through `OidcRemoteFailureDiagnostic.Log` and returns a generic `500 "Authentication failed."`.

Neither host exposes an API scope or implements OBO — `docs/classic-net-bff-poc.md`: *"Neither sample exposes an API scope or implements On-Behalf-Of."*

---

## Q7 — `spa/` folder

A **Vite + React + TypeScript** SPA. `spa/src/` contains `api.ts`, `App.tsx`, `authConfig.ts`, `getApiToken.ts`, `getGraphToken.ts`, `main.tsx`, `vite-env.d.ts`, `components/`.

Auth library: **MSAL.js** (`@azure/msal-browser`).

`spa/src/authConfig.ts` lines 18-25:

```typescript
export const msalConfig: Configuration = {
  auth: {
    clientId: spaClientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: window.location.origin,
  },
  cache: { cacheLocation: "sessionStorage", storeAuthStateInCookie: false },
};
```

**Yes — it holds tokens in the browser**, in `sessionStorage`, which is the public-client anti-pattern a BFF replaces. That is **deliberate**: this SPA is the *mock Croesus SaaS* used to demonstrate the correct OBO flow, and is a separate artifact from the `poc/` BFF hosts. It is the SPA half of the SPA → middle-tier API → Graph OBO demo, not part of the BFF comparison.

Its discipline is that it requests **only** the API scope, lines 27-37:

```typescript
// CRITICAL: the SPA requests ONLY the middle-tier API scope.
// No Microsoft Graph scope appears here. Graph access happens exclusively
// inside the API via the On-Behalf-Of exchange.
export const apiRequest: RedirectRequest = { scopes: [apiScope] };
export const loginRequest: RedirectRequest = { scopes: [apiScope] };
```

The single exception is the gated Tier 2a replay demo, lines 12-16 and 39-45:

```typescript
// GATED Tier 2a demo only: the Graph delegated scope the SPA acquires so it can
// forward a Graph token to the API's server-side replay endpoint. Defaults to
// "User.Read". This is the ONE intentional exception to the "SPA requests only
// the API scope" invariant and is used exclusively by the gated replay flow.
export const graphRequest: RedirectRequest = { scopes: [graphScope] };
```

All values are injected at build time via `import.meta.env.VITE_*` (lines 7-16) per `docs/configuration-contract.md`.

---

## Q8 — Pipeline / CI

**CI exists.** Three GitHub Actions workflows, no Azure Pipelines:

| Workflow | Purpose |
| --- | --- |
| `.github/workflows/classic-net-bff-poc.yml` | The BFF POC. `validate` \| `deploy` dispatch (line 7-13). Builds and tests both proofs, publishes packages, compiles Bicep, runs PowerShell static security suites, optional what-if; `deploy` job provisions Entra + infra and deploys both apps, then verifies auth redirects. Detailed in Q3. |
| `.github/workflows/teardown-classic-net-bff-poc.yml` | Guarded teardown requiring `destroy:<name-prefix>`; deletes only an exactly-named, ownership-tagged registration and the deterministic RG. |
| `.github/workflows/deploy-croesus.yml` | The separate OBO mock-SaaS stack (SPA + API + Key Vault + App Insights), including the `evidence` job that runs `scripts/smoke-test.sh` and `scripts/negative-test.sh`. |

Auth to Azure is **GitHub OIDC federated credential** (`azure/login@v2`) with `permissions: contents: read, id-token: write` — no stored deploy secret.

---

## Q9 — Root `package.json` and `docs/deck/generate-deck.py`

`package.json` is **not** a real Node project for this repo — `"scripts": { "test": "echo \"Error: no test specified\" && exit 1" }`, `"main": "index.js"`, no dependencies. Its notable content is the `description` field, which carries a full multi-paragraph escalation callout (the "Step 1 is done: the redemption is confirmed server-side" block quoted in Q2). It exists mainly for repo metadata (`repository`, `bugs`, `homepage` → `github.com/devopsabcs-engineering/croesus`).

`docs/deck/generate-deck.py` is a Python generator for a customer-facing slide deck — the artifact-production tooling for the engagement. (Not read line-by-line; see gaps.)

---

## Gaps / could not verify

* **Who set `publicNetworkAccess: Disabled`, and when.** The `az monitor activity-log list` query failed on shell quoting (cmd.exe parsing of `?[contains(...)]`) and was not retried. A correctly-quoted rerun (or a PowerShell `--query` in a file) within the 90-day retention window would name the caller and timestamp. This is the single most useful unfinished check.
* **`docs/obo-demo-guide.md`, `assets/app-registration-analysis.md`, `-findings.md`, `-verification.md`, `assets/latest-info/*`, `scripts/evidence-kql.kusto`** were characterized from cross-references, grep hits, and the documents that cite them rather than read end-to-end. Their load-bearing claims are corroborated elsewhere, but exact line numbers for their internal sections are not pinned here.
* **`scripts/provision-classic-net-bff-poc.ps1`, `provision-classic-net-bff-deployment.ps1`, both `cleanup-*.ps1`, and both `test-*-static.ps1`** were verified *negatively* — a repo-wide grep for `publicNetworkAccess|ipSecurityRestrictions|clientCertEnabled|clientCertMode|authsettings|Return403` returned **zero hits in `scripts/`**, so none of them set the offending property. Their full control flow (Entra convergence, credential rotation, state file handling) is described in `docs/classic-net-bff-poc.md` but was not independently read line-by-line.
* **`infra/main.bicep`, `infra/modules/appservice.bicep`, `infra/main.bicepparam`** belong to the **separate OBO demo stack**, not the BFF POC (which uses `infra/poc/`). They were only grepped for the network properties. `infra/modules/keyvault.bicep` line 61 does set `publicNetworkAccess: 'Disabled'` — but on a **Key Vault** in the other stack, and that Key Vault does not exist in `croesus-bff-poc-rg`.
* **Whether the POC ever worked.** The deploy workflow's `Verify authentication redirects` step would have failed against a 403, implying the apps were reachable at deploy time and the setting was changed later — but no deployment-run history was inspected to confirm.
* **`docs/deck/generate-deck.py`** contents not read.
* **Application logs.** `az webapp log` was not run; with the front-end rejecting every request at the network layer, worker logs would contain nothing relevant to the 403.

---

## Recommended next research (not completed)

- [ ] Rerun the activity-log query with correct quoting to identify the caller and timestamp that set `publicNetworkAccess=Disabled` on both sites.
- [ ] After remediation, re-probe both root URLs and confirm the expected `302` to `login.microsoftonline.com` (legacy) / auth challenge (modern) rather than `403`.
- [ ] Verify the shared Entra `web` registration still exists and its client secret has not expired — `docs/classic-net-bff-poc.md` caps the demo credential at **7 days**, and the POC was deployed well before 2026-09-22, so the secret is almost certainly expired even once the network block is lifted.
- [ ] Read `docs/obo-demo-guide.md` and `scripts/evidence-kql.kusto` end-to-end to pin the exact evidence-capture steps and Query 4's text.
- [ ] Read the four `scripts/provision-*`/`cleanup-*` PowerShell files line-by-line to confirm no teardown path partially ran (a partial cleanup is an alternative explanation for a post-deploy config change).
- [ ] Determine whether the repo should defensively pin `publicNetworkAccess: 'Enabled'` in `infra/poc/main.bicep` so a redeploy self-heals this drift.

---

## Clarifying questions

1. Do you want the 403 **fixed** (i.e. authorization to run `az webapp update --set publicNetworkAccess=Enabled` on both apps), or only diagnosed? This research made no changes.
2. Is the POC expected to be **reachable from the public internet**, or was `publicNetworkAccess=Disabled` an intentional lockdown by someone else that should instead be paired with a private endpoint?
3. Should `infra/poc/main.bicep` be updated to set `publicNetworkAccess` explicitly so the setting is governed by source rather than drift?
4. Is the intent to **extend the existing `api/` replay harness** (Q5) for further evidence, or to revive the **BFF comparison** apps (Q3/Q6) for a customer demonstration? That changes which follow-on work matters.
