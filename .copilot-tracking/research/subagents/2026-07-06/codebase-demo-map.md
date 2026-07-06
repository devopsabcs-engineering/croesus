<!-- markdownlint-disable-file -->
# Codebase Demo Map — Croesus OBO Demo (Tier 2 token-replay enablement)

Status: Complete
Scope: RESEARCH ONLY. No files were modified outside .copilot-tracking/research/.
Date: 2026-07-06
Repository root: c:\src\GitHub\devopsabcs-engineering\croesus

Purpose: Map the current Croesus mock-SaaS On-Behalf-Of (OBO) demo end-to-end so an
implementer can add Tier 2 (a server-side token-replay endpoint that reproduces the
customer's Conditional Access "Token Protection unbound / 1008" replay shape) plus a
reversible teardown. All file paths below are plain-text workspace-relative with line
numbers. Excerpts are verbatim from the current tree.

---

## 0. Executive summary — the discoveries that matter for Tier 2

- Tier 1 today is CLIENT-ONLY and SAFE. The only "replay" is the SPA replaying its own
  API-audienced token A directly to Microsoft Graph and showing Graph's 401. See
  spa/src/api.ts:159-215 (callGraphWithApiTokenWrongWay) and spa/src/App.tsx:139-141
  (the red "Replay API token to Graph (wrong)" button). This is Variant A from prior research.
- The API has NO token-forwarding / echo / replay endpoint. The only controller action is
  GET /api/me (api/Controllers/MeController.cs:48-145). Tier 2 must ADD a new API endpoint.
- The SPA requests ONLY the API scope. There is NO Graph scope anywhere in SPA source
  (spa/src/authConfig.ts:23-33). Tier 2 needs a NEW request object for a Graph delegated
  token plus SPA Graph User.Read delegated consent.
- Feature gates named in the README (Demo:EnableReplay, VITE_ENABLE_REPLAY_DEMO) DO NOT EXIST
  yet in any source, config, bicep, or workflow. They are stated as Tier 2 prerequisites only.
- CORS allows GET ONLY (api/Program.cs:31-43, .WithMethods("GET")). A Tier 2 POST /api/replay
  from the SPA requires widening the CORS policy to allow POST.
- Teardown pattern is display-name-based, existence-guarded, idempotent
  (scripts/teardown-app-registrations.sh). It deletes the two app registrations + the Key Vault
  cert; it does NOT currently remove any Graph delegated grant on the SPA or any replay-lab
  environment/consent. Tier 2 teardown must extend it to revoke the SPA Graph User.Read grant.
- The negative-control mechanism has TWO layers: (1) unit/integration tests in
  api/Tests/NegativeControlTests.cs proving a Graph-audience token → 401 at /api/me and that
  API-vs-Graph audiences are provably distinct; (2) a runtime script scripts/negative-test.sh
  proving mismatched-audience tokens are rejected in both directions.
- Licensing for observing 1008 is PRESENT (Entra ID P2 provisioned) but ENFORCEMENT is
  preview-only and does not cover browser SPA → custom API → Graph — so Tier 2 reproduces the
  replay SHAPE, not a literal 1008 signal (README "Entra ID licensing" section; docs/evidence-narrative.md).

---

## 1. Tech stack versions (recorded)

- API target framework: net8.0 — api/Croesus.Api.csproj:4 (`<TargetFramework>net8.0</TargetFramework>`).
- API SDK: Microsoft.NET.Sdk.Web; Nullable enabled; ImplicitUsings enabled — api/Croesus.Api.csproj:1-10.
- API NuGet package references — api/Croesus.Api.csproj:21-23:
  - Microsoft.Identity.Web 4.11.0
  - Microsoft.Identity.Web.MicrosoftGraph 4.11.0
  - Microsoft.ApplicationInsights.AspNetCore 2.22.0
  - (Note: Microsoft.Identity.Web.MicrosoftGraph 4.x pulls Graph SDK v4-style GraphServiceClient, matching MeController's `_graph.Me.Request().GetAsync()` fluent shape — api/Controllers/MeController.cs:87.)
- Test project target framework: net8.0; IsTestProject=true — api/Tests/Croesus.Api.Tests.csproj:4-9.
- Test framework = xUnit + ASP.NET Core WebApplicationFactory — api/Tests/Croesus.Api.Tests.csproj:12-18:
  - Microsoft.NET.Test.Sdk 17.11.1
  - Microsoft.AspNetCore.Mvc.Testing 8.0.10
  - System.IdentityModel.Tokens.Jwt 8.19.1
  - xunit 2.9.2
  - xunit.runner.visualstudio 2.8.2
- SPA build tool: Vite 6.4.3; TypeScript 5.6.3; React 18.3.1 — spa/package.json:9-24.
- SPA MSAL libraries — spa/package.json:11-12:
  - @azure/msal-browser ^3.28.1
  - @azure/msal-react ^2.2.0
- SPA build script: `tsc && vite build` — spa/package.json:8.
- CI toolchain — .github/workflows/deploy-croesus.yml: Node 22 (line 34), .NET 8.0.x (line 62).
- Solution container present: api/Croesus.Api.slnx (SLNX solution format).

---

## 2. API (api/)

### 2.1 api/Program.cs — auth, CORS, OBO, endpoint wiring

Full file is 61 lines. Key wiring:

- Application Insights: api/Program.cs:10 — `builder.Services.AddApplicationInsightsTelemetry();`
  Connection string comes from APPLICATIONINSIGHTS_CONNECTION_STRING (Key Vault reference at runtime).
- Microsoft.Identity.Web OBO fluent chain — api/Program.cs:18-24:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddMicrosoftGraph(builder.Configuration.GetSection("Graph"))
        .AddInMemoryTokenCaches();
```

  - AddMicrosoftIdentityWebApi validates inbound token (issuer/audience/signature) against "AzureAd".
  - EnableTokenAcquisitionToCallDownstreamApi turns on the OBO machinery (ITokenAcquisition).
  - AddMicrosoftGraph builds the GraphServiceClient used in MeController.
  - Confidential-client credential is AzureAd:ClientCredentials (SourceType configured), no inline secret.
- Authorization: api/Program.cs:26 — `builder.Services.AddAuthorization();`
- CORS — api/Program.cs:29-43:

```csharp
const string spaCorsPolicy = "SpaCors";
var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(spaCorsPolicy, policy => policy
        .WithOrigins(corsAllowedOrigins)
        .AllowAnyHeader()
        .WithMethods("GET"));
});
```

  - IMPORTANT for Tier 2: only GET is allowed cross-origin. A SPA-initiated POST /api/replay
    would fail CORS preflight until `.WithMethods("GET")` is widened (e.g. `.WithMethods("GET","POST")`).
- Evidence logger DI: api/Program.cs:46 — `builder.Services.AddScoped<OboClaimLogger>();`
- Controllers: api/Program.cs:48 — `builder.Services.AddControllers();`
- Middleware order — api/Program.cs:52-56: `UseCors(spaCorsPolicy)` → `UseAuthentication()` →
  `UseAuthorization()` → `MapControllers()`.
- Test hook: api/Program.cs:60 — `public partial class Program { }` (lets WebApplicationFactory<Program> host it).

There is NO existing negative-control endpoint and NO replay/echo endpoint in Program.cs — only
AddControllers()/MapControllers() with the single MeController.

### 2.2 api/Controllers/MeController.cs — leg-1 validation, leg-2 Graph call, logging

- Class attributes — api/Controllers/MeController.cs:22-25: `[Authorize]`, `[ApiController]`,
  `[Route("api/[controller]")]` (route = api/me), `[RequiredScope("access_as_user")]`.
- Constants — api/Controllers/MeController.cs:27-28:

```csharp
private const string GraphResource = "https://graph.microsoft.com";
private static readonly string[] GraphScopes = { "https://graph.microsoft.com/User.Read" };
```

- Constructor injects GraphServiceClient, ITokenAcquisition, IConfiguration, OboClaimLogger
  — api/Controllers/MeController.cs:35-46.
- Single action GET api/me — api/Controllers/MeController.cs:48-145:
  - Defensive audience binding (belt-and-suspenders "reject the token" rule)
    — api/Controllers/MeController.cs:51-64:

```csharp
var inboundAudience = FirstClaim("aud", "appid_aud");
var expectedClientId = _config["AzureAd:ClientId"];
var expectedAudience = _config["AzureAd:Audience"];
if (!AudienceMatchesThisApi(inboundAudience, expectedClientId, expectedAudience))
{
    return Unauthorized(new
    {
        error = "invalid_audience",
        message = "The presented token is not audienced to this API; the On-Behalf-Of exchange was not attempted."
    });
}
```

  - Leg-1 evidence dictionary (decoded inbound non-sensitive claims) — api/Controllers/MeController.cs:67-76:
    aud, scp, appid, oid, iss, iat, jti (via FirstClaim across primary + WS-Fed claim-type aliases).
  - Leg-2 OBO exchange — api/Controllers/MeController.cs:81-84:

```csharp
var oboResult = await _tokenAcquisition.GetAuthenticationResultForUserAsync(
    GraphScopes,
    authenticationScheme: null,
    user: User);
```

  - Graph /me call using OBO-acquired token B — api/Controllers/MeController.cs:87:
    `var me = await _graph.Me.Request().GetAsync(cancellationToken);`
  - OBO request-shape evidence (grant_type=jwt-bearer, requested_token_use=on_behalf_of, scope,
    credentialSource, credentialName) — api/Controllers/MeController.cs:90-97. Note it reads
    `AzureAd:ClientCredentials:0:SourceType` and `AzureAd:ClientCredentials:0:KeyVaultCertificateName`
    for the credential SOURCE only (never material).
  - Leg-2 evidence from MSAL result (aud=graph by construction, scp, expiresOn, correlationId,
    tokenSource) — api/Controllers/MeController.cs:101-108. The Graph JWT is deliberately NOT decoded.
  - Claims logged: api/Controllers/MeController.cs:110 — `_claimLogger.LogExchange(leg1, oboRequestShape, leg2);`
  - Response: api/Controllers/MeController.cs:112-144 returns `{ user, evidence = { leg1, leg2 } }`.
- Helpers:
  - FirstClaim(params string[] types) — api/Controllers/MeController.cs:147-159.
  - AudienceMatchesThisApi(audience, clientId, configuredAudience) — api/Controllers/MeController.cs:161-184
    (matches configured Audience OR bare clientId (v2) OR api://{clientId} (v1), case-insensitive).

Token/claims logging: ALL logging goes through OboClaimLogger; MeController never logs raw tokens.
Only decoded, bounded, non-sensitive claim values are placed into dictionaries and returned/logged.

### 2.3 api/Telemetry/OboClaimLogger.cs — claims-only App Insights logging with redaction

- JWT-shape redaction regex (three base64url segments) — api/Telemetry/OboClaimLogger.cs:22-25:

```csharp
private static readonly Regex JwtShaped = new(
    @"^[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}$",
    RegexOptions.Compiled);
```

- LogExchange(leg1Inbound, oboRequestShape, leg2Outbound) — api/Telemetry/OboClaimLogger.cs:40-78:
  - Builds a properties dictionary keyed `leg1.*`, `obo.*`, `leg2.*`, each value passed through Redact().
  - Emits `_telemetry?.TrackEvent("OboExchange", properties);` — api/Telemetry/OboClaimLogger.cs:63.
  - Also structured ILogger.LogInformation with the key claims — api/Telemetry/OboClaimLogger.cs:65-77.
- Redact(string?) — api/Telemetry/OboClaimLogger.cs:85-93: any token-shaped string → "[REDACTED:token]";
  everything else returned unchanged. This is the last-line-of-defence so raw tokens can never leak.
- Tier 2 implication: a replay endpoint that decodes a Graph token for evidence MUST reuse this
  redaction guard (or an equivalent) and follow the same claims-only pattern. The event name
  "OboExchange" is the current App Insights custom-event; a Tier 2 replay would want a distinct
  event name (e.g. "ReplayAttempt") to keep evidence queries clean.

### 2.4 api/Tests/NegativeControlTests.cs — the existing Tier 1 negative control (unit/integration)

xUnit + WebApplicationFactory<Program>. Three tests:

- GraphAudienceToken_PresentedToApi_IsRejectedWith401 — api/Tests/NegativeControlTests.cs:35-45:
  mints an HS256 token with aud = Graph, presents to /api/me, asserts 401 (OBO never runs).
- NoToken_PresentedToApi_IsRejectedWith401 — api/Tests/NegativeControlTests.cs:47-55: no bearer → 401.
- TokenA_PresentedDirectlyToGraph_WouldBeRejected_BecauseAudienceIsNotGraph
  — api/Tests/NegativeControlTests.cs:57-66: STRUCTURAL assertion `Assert.NotEqual(GraphAudience, ApiAudience)`
  (deterministic, credential-free; does not hit live Graph).
- ApiFactory (test host) — api/Tests/NegativeControlTests.cs:74-121: swaps the JWT trust anchor via
  `PostConfigure<JwtBearerOptions>` to a local symmetric key + fixed ValidAudiences = ApiAudience,
  ValidateIssuer=false, static empty OpenIdConnectConfiguration (never contacts Entra). In-memory config
  supplies AzureAd:* + Graph:* — api/Tests/NegativeControlTests.cs:88-96.
- TestAuth helper — api/Tests/NegativeControlTests.cs:124-159: constants
  ApiAudience = "api://11111111-1111-1111-1111-111111111111", GraphAudience = "https://graph.microsoft.com",
  a 256-bit symmetric signing key, and CreateToken(audience, scope) minting HS256 tokens with
  scp/appid/oid/jti claims.

What Tier 1 asserts overall: a foreign-audience (Graph) token is rejected at the auth middleware
before the controller/OBO runs, and the API vs Graph audiences are provably distinct — i.e. a
replay of the API token to Graph (or a Graph token to the API) cannot succeed.

Tier 2 test implication: a new POST /api/replay endpoint would need its OWN tests asserting (a) it is
[Authorize]d (rejects no-token / wrong-audience like /api/me), and (b) it never logs/returns the raw
forwarded token (redaction). The prompt's "token-redaction tests" prereq maps here.

### 2.5 api/appsettings.json and api/appsettings.Development.json — configuration keys

api/appsettings.json (full):

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "REPLACE_WITH_AZURE_TENANT_ID",
    "ClientId": "REPLACE_WITH_API_CLIENT_ID",
    "Audience": "api://REPLACE_WITH_API_CLIENT_ID",
    "ClientCredentials": [
      {
        "SourceType": "Base64Encoded",
        "Base64EncodedValue": "REPLACE_WITH_CERT_BASE64"
      }
    ]
  },
  "Graph": {
    "BaseUrl": "https://graph.microsoft.com/v1.0",
    "Scopes": "user.read"
  },
  "Cors": {
    "AllowedOrigins": []
  },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*"
}
```

- Config keys present: AzureAd:Instance, AzureAd:TenantId, AzureAd:ClientId, AzureAd:Audience,
  AzureAd:ClientCredentials[0]:SourceType, AzureAd:ClientCredentials[0]:Base64EncodedValue,
  Graph:BaseUrl, Graph:Scopes, Cors:AllowedOrigins, Logging, AllowedHosts — api/appsettings.json:1-29.
- appsettings.json SourceType = "Base64Encoded" (dev placeholder), whereas deployed App Service sets
  it to Base64Encoded backed by a Key Vault reference (see 4.2). MeController's OBO request-shape log
  reads a KeyVaultCertificateName key that is NOT present in appsettings.json (only relevant if a
  SourceType=KeyVault shape is used); with Base64Encoded it logs null for credentialName — harmless.
- NO Demo:EnableReplay key exists. This is the Tier 2 API feature gate to ADD (README prereq).
- api/appsettings.Development.json (full) — api/appsettings.Development.json:1-9: only DetailedErrors
  + verbose Logging (Default=Debug, Microsoft.Identity.Web=Information). No secrets, no replay gate.

---

## 3. React SPA (spa/)

### 3.1 spa/src/authConfig.ts — MSAL config, scopes (API-only, NO Graph)

- Reads Vite env vars — spa/src/authConfig.ts:9-11: VITE_TENANT_ID, VITE_SPA_CLIENT_ID, VITE_API_SCOPE.
- msalConfig — spa/src/authConfig.ts:13-21: clientId=spaClientId, authority=login.microsoftonline.com/{tenantId},
  redirectUri=window.location.origin, cache=sessionStorage. No knownAuthorities.
- apiRequest / loginRequest BOTH use ONLY the API scope — spa/src/authConfig.ts:26-33:

```ts
export const apiRequest: RedirectRequest = { scopes: [apiScope] };
export const loginRequest: RedirectRequest = { scopes: [apiScope] };
```

- spa/src/authConfig.ts:23-25 comment explicitly forbids any Graph scope in the SPA.
- pca export — spa/src/authConfig.ts:35.

WHICH SCOPES THE SPA REQUESTS TODAY: the API scope ONLY (api://<API_CLIENT_ID>/access_as_user).
No Microsoft Graph scope is requested anywhere. Tier 2 Variant B/C requires ADDING a new request
object (e.g. `graphRequest: { scopes: ["User.Read"] }` or a full Graph resource scope) here.

### 3.2 spa/src/getApiToken.ts — token A acquisition (silent → popup)

- getApiToken(instance, account) — spa/src/getApiToken.ts:14-30: acquireTokenSilent({...apiRequest, account}),
  fallback acquireTokenPopup(apiRequest) on InteractionRequiredAuthError. Returns token A (aud = API).
- Tier 2 implication: a sibling getGraphToken.ts (or a second branch here) using a Graph request object
  is the minimal change to make the SPA hold a Graph-audienced token.

### 3.3 spa/src/api.ts — API fetch, JWT decode, and the CLIENT-ONLY replay control

- Interfaces Leg1Claims / Leg2Claims / MeResponse — spa/src/api.ts:8-60.
- callApiMe(instance, account) — spa/src/api.ts:65-83: gets token A, `fetch(${VITE_API_BASE_URL}/api/me,
  { headers: { Authorization: Bearer <tokenA> } })`. Only header is Authorization; method is GET.
- decodeJwtClaims(token) — spa/src/api.ts:96-133: local base64url decode returning only aud/scp/jti/iat
  and a boolean hasCnf (proof-of-possession/token-binding presence), never the raw token.
- ReplayAttemptResult interface — spa/src/api.ts:140-153.
- callGraphWithApiTokenWrongWay(instance, account) — spa/src/api.ts:159-215 (THE Tier 1 replay control):
  - Acquires token A (aud = API), decodes claims locally for display — spa/src/api.ts:174-175.
  - `fetch("https://graph.microsoft.com/v1.0/me", { headers: { Authorization: Bearer <tokenA> } })`
    — spa/src/api.ts:181-185.
  - Interprets 401 as the EXPECTED negative-control success (audience binding), explicitly NOT the
    literal Token Protection 1008 signal — spa/src/api.ts:197-215.
  - Returns only safe evidence (attemptedTarget, expectedAudience, tokenAudience, tokenScope, tokenJti,
    tokenIssuedAt, hasCnf, status, ok, bodyPreview, interpretation); raw token never leaves the function.

This is Variant A (replay the API token to Graph → 401). Tier 2 is Variant C: SPA acquires a real
Graph token, POSTs it to a new API endpoint, and the API replays it server-side to Graph.

### 3.4 spa/src/App.tsx — UI wiring and the buttons

- Imports callApiMe + callGraphWithApiTokenWrongWay + panels — spa/src/App.tsx:1-16.
- ContrastPanel() — spa/src/App.tsx:18-62: static "Broken — token replay" vs "Correct — OBO" copy.
- SignInButton — spa/src/App.tsx:64-74 (instance.loginPopup(loginRequest)).
- App() state — spa/src/App.tsx:113-121: data, error, loading, replay, replayError, replayLoading.
- handleCallApi() — spa/src/App.tsx:123-138 (good path → EvidencePanel).
- handleReplayToGraph() — spa/src/App.tsx:140-154 (Tier 1 client-only replay → ReplayAttemptPanel).
- Buttons rendered inside AuthenticatedTemplate — spa/src/App.tsx:183-192: "Call API" +
  "Replay API token to Graph (wrong)" (replayButtonStyle red — spa/src/App.tsx:107-110).
- Panels: EvidencePanel when data set — spa/src/App.tsx:206-208; ContrastPanel always — line 210;
  ReplayAttemptPanel when replay set — spa/src/App.tsx:212-214.

NO VITE_ENABLE_REPLAY_DEMO gate exists in App.tsx; the Tier 1 replay button is always rendered when
authenticated. Tier 2 would add a gated section (behind VITE_ENABLE_REPLAY_DEMO) for the Variant C flow.

### 3.5 spa/src/main.tsx — bootstrap

- spa/src/main.tsx:14-23: `pca.initialize().then(() => createRoot(root).render(<MsalProvider instance={pca}><App/></MsalProvider>))`. Popup model; no redirect-promise handling.

### 3.6 spa/src/components/EvidencePanel.tsx — good-path evidence rendering

- Derives apiClientId from VITE_API_SCOPE via regex `^api:\/\/([^/]+)\//` — spa/src/components/EvidencePanel.tsx:5-6.
- Asserts leg1AudIsApi, leg1ScpOk (scp === "access_as_user"), distinctAud (leg1.aud !== leg2.aud)
  — spa/src/components/EvidencePanel.tsx:97-99. Renders two LegCards + a checklist.

### 3.7 spa/src/components/ReplayAttemptPanel.tsx — Tier 1 replay result rendering

- ReplayAttemptPanel({ result }) — spa/src/components/ReplayAttemptPanel.tsx:33-104:
  frames 401 as green-check success; shows attemptedTarget, tokenAudience, expectedAudience, Graph status,
  hasCnf, jti, iat; explicitly states "This is the audience-bound negative control, not the literal Token
  Protection 1008 signal" — spa/src/components/ReplayAttemptPanel.tsx:88-92.

### 3.8 SPA env vars (Vite) — current vs Tier 2

- Current VITE_* consumed: VITE_TENANT_ID, VITE_SPA_CLIENT_ID, VITE_API_SCOPE (authConfig.ts),
  VITE_API_BASE_URL (api.ts:72). Injected at build in .github/workflows/deploy-croesus.yml:41-45.
- NOT present anywhere: VITE_ENABLE_REPLAY_DEMO, VITE_GRAPH_SCOPE, VITE_GRAPH_BASE_URL. These are the
  Tier 2 additions (feature gate + Graph scope + optional Graph base URL).

---

## 4. Infrastructure (infra/)

### 4.1 infra/main.bicep — orchestrator

- targetScope = resourceGroup — infra/main.bicep:12.
- Params (all non-secret): location, tenantId, spaClientId, apiClientId, aadInstance, apiAudience
  (default api://${apiClientId}), spaAppName (croesus-spa), apiAppName (croesus-api), appServicePlanName
  (croesus-asp), keyVaultName, logAnalyticsWorkspaceName (croesus-law), appInsightsName (croesus-appi),
  certSecretName (croesus-api-cert), appInsightsConnectionStringSecretName (appinsights-connection-string)
  — infra/main.bicep:14-59.
- Modules: monitoring (infra/main.bicep:61-68), appService (infra/main.bicep:70-86), keyVault
  (infra/main.bicep:88-98). keyVault consumes appService.outputs.apiPrincipalId +
  monitoring.outputs.appInsightsConnectionString.
- Outputs: spaAppName, apiAppName, spaHostName, apiHostName, keyVaultUri, appInsightsConnectionStringSecretUri
  — infra/main.bicep:100-118.

### 4.2 infra/modules/appservice.bicep — plan + two Web Apps + API app settings

- App Service plan: Linux B1 Basic, reserved — infra/modules/appservice.bicep:56-67.
- SPA Web App (croesus-spa): NODE|20-lts, `pm2 serve /home/site/wwwroot --no-daemon --spa`, httpsOnly,
  ftpsState Disabled, minTls 1.2; appSettings SPA_CLIENT_ID + TENANT_ID (informational only)
  — infra/modules/appservice.bicep:69-101.
- API Web App (croesus-api): system-assigned MI, DOTNETCORE|8.0, httpsOnly — infra/modules/appservice.bicep:103-160.
  App settings (double-underscore → config keys):
  - AzureAd__Instance, AzureAd__TenantId, AzureAd__ClientId, AzureAd__Audience
    — infra/modules/appservice.bicep:116-131.
  - Cert via Key Vault reference — infra/modules/appservice.bicep:132-144:
    `AzureAd__ClientCredentials__0__SourceType = 'Base64Encoded'` and
    `AzureAd__ClientCredentials__0__Base64EncodedValue = '@Microsoft.KeyVault(SecretUri=${certSecretUri})'`.
  - App Insights — infra/modules/appservice.bicep:145-148:
    `APPLICATIONINSIGHTS_CONNECTION_STRING = '@Microsoft.KeyVault(SecretUri=${appInsightsConnectionStringSecretUri})'`.
  - CORS origin threaded from SPA hostname — infra/modules/appservice.bicep:149-152:
    `Cors__AllowedOrigins__0 = 'https://${spaApp.properties.defaultHostName}'`.
  - Key Vault secret URIs built from vault NAME only (no hard dependency) — infra/modules/appservice.bicep:50-53.
- Outputs: apiPrincipalId, apiHostName, spaHostName, apiAppName, spaAppName — infra/modules/appservice.bicep:163-177.

Tier 2 implication: to gate the replay endpoint at the platform, add a `Demo__EnableReplay` API app
setting (default 'false') here, plus optionally a second Cors__AllowedOrigins entry if a distinct
replay-lab SPA origin is used. The API cert threading pattern is the model to reuse.

### 4.3 infra/modules/keyvault.bicep — vault + RBAC + App Insights secret

- Key Vault: RBAC-enabled, soft-delete 90d, publicNetworkAccess Enabled, standard SKU
  — infra/modules/keyvault.bicep:38-53.
- Role assignment: Key Vault Secrets User (4633458b-17de-408a-b874-0445c86b69e6) → API MI
  — infra/modules/keyvault.bicep:29-34, 55-63.
- App Insights connection string stored as a secret — infra/modules/keyvault.bicep:65-72.
- The API cert (croesus-api-cert) is provisioned OUT OF BAND (by the provision script into KV) and is
  intentionally NOT declared here — infra/modules/keyvault.bicep:5-9.
- Outputs: keyVaultUri, keyVaultName, appInsightsConnectionStringSecretUri — infra/modules/keyvault.bicep:74-81.

### 4.4 infra/modules/monitoring.bicep — Log Analytics + App Insights + Entra sign-in diagnostics

- Log Analytics (croesus-law): PerGB2018, 30-day retention — infra/modules/monitoring.bicep:21-32.
- App Insights (workspace-based, IngestionMode LogAnalytics) — infra/modules/monitoring.bicep:34-45.
- Entra sign-in diagnostic setting (scope tenant()) streaming SignInLogs, NonInteractiveUserSignInLogs,
  AuditLogs to the workspace — infra/modules/monitoring.bicep:50-70. This is what powers the evidence KQL.
- Outputs: logAnalyticsWorkspaceId, appInsightsName, appInsightsConnectionString — infra/modules/monitoring.bicep:72-81.

Tier 2 implication: the NonInteractiveUserSignInLogs category is already streamed, so any Entra log row
a server-side replay produces would already be captured (though 1008 is not expected for this shape).

### 4.5 infra/main.bicepparam — non-secret params (placeholder GUIDs, overridden in CI)

- infra/main.bicepparam:12-24: tenantId/spaClientId/apiClientId placeholder GUIDs; resource names
  (croesus-spa/api/asp, croesus-kv-demo, croesus-law, croesus-appi, croesus-api-cert,
  appinsights-connection-string). Comment says values overridden in CI from vars.* — infra/main.bicepparam:1-14.

---

## 5. Scripts (scripts/)

### 5.1 scripts/provision-app-registrations.sh — idempotent create of SPA + API registrations

- Well-known constants — scripts/provision-app-registrations.sh:41-42:
  GRAPH_APP_ID=00000003-0000-0000-c000-000000000000, GRAPH_USER_READ=e1fe6dd8-ba31-4d61-89e7-88639da4683d.
- Config env vars — scripts/provision-app-registrations.sh:45-51: KEY_VAULT_NAME (required),
  CERT_NAME (croesus-api-cert), API_DISPLAY_NAME ("Croesus GPD Central API (mock)"),
  SPA_DISPLAY_NAME ("Croesus GPD Central SPA (mock)"), SPA_REDIRECT_URI (https://localhost:3000),
  SPA_DEPLOYED_REDIRECT_URI (optional), SIGN_IN_AUDIENCE (AzureADMyOrg).
- Look-up-or-create API reg — scripts/provision-app-registrations.sh:85-102; SPA reg — 116-130.
- SPA platform redirect URIs (spa, isFallbackPublicClient=true) via Graph PATCH — scripts/provision-app-registrations.sh:137-147.
- Expose access_as_user scope + knownClientApplications, then preAuthorizedApplications (two PATCHes)
  — scripts/provision-app-registrations.sh:152-190.
- Certificate: create/import self-signed cert in Key Vault, attach ONLY the public cert to the API reg
  (guarded so re-runs don't accumulate creds) — scripts/provision-app-registrations.sh:195-238.
- Graph User.Read (delegated) on the API + admin-consent — scripts/provision-app-registrations.sh:243-249.
- SPA → API access_as_user delegated permission + admin-consent — scripts/provision-app-registrations.sh:254-260.
- Outputs (non-secret): api_client_id, spa_client_id, api_scope (to $GITHUB_OUTPUT and stdout)
  — scripts/provision-app-registrations.sh:265-278.

Tier 2 implication: this is where SPA Graph User.Read (delegated) + admin consent would be ADDED
(currently Graph User.Read is granted only on the API, NOT the SPA — scripts/provision-app-registrations.sh:243-249
is API-only). The README Tier 2 prereq "Grant the SPA Microsoft Graph User.Read delegated consent" maps here.

### 5.2 scripts/teardown-app-registrations.sh — the reversible-teardown pattern to extend

- Config env vars mirror provision — scripts/teardown-app-registrations.sh:22-26: KEY_VAULT_NAME (required),
  CERT_NAME, API_DISPLAY_NAME, SPA_DISPLAY_NAME, PURGE_CERT (default false).
- get_app_id_by_name(name) → `az ad app list --display-name "$1" --query "[0].appId" -o tsv` — scripts/teardown-app-registrations.sh:28-30.
- delete_app(name): resolve appId by display name, `az ad app delete --id` if present, else log "already deleted"
  — scripts/teardown-app-registrations.sh:33-43. Existence-guarded, idempotent (safe to re-run).
- Deletes SPA first, then API — scripts/teardown-app-registrations.sh:48-49.
- Deletes the Key Vault cert if present — scripts/teardown-app-registrations.sh:54-60; optional purge of
  soft-deleted cert when PURGE_CERT=true — scripts/teardown-app-registrations.sh:62-69.

TEARDOWN PATTERN SUMMARY: enumerate by display name → existence check → delete → tolerate already-removed.
It does NOT currently revoke any Graph delegated grant, remove a replay-lab GitHub environment, or reset
any app setting/feature gate. Tier 2 reversible teardown must ADD: (a) revoke the SPA's Graph User.Read
delegated grant/consent, and (b) optionally reset Demo:EnableReplay / VITE_ENABLE_REPLAY_DEMO to false.

### 5.3 scripts/verify-app-registrations.sh — OBO-capability assertions (read-only)

- Checks (exit non-zero on any failure) — scripts/verify-app-registrations.sh:
  exposed scope access_as_user (lines 66-71), Application ID URI set (75-80), confidential credential present
  (84-92), SPA→API wiring preAuthorizedApplications + knownClientApplications (96-125), Graph User.Read
  delegated + admin-consent grants (129-149). Uses `az ad app show`/`permission list`/`list-grants` + jq.

### 5.4 scripts/setup-deploy-identity.sh — OIDC deploy identity + repo vars

- Creates the "Croesus Deploy Identity (mock)" app reg + SP (idempotent) — scripts/setup-deploy-identity.sh:63-76.
- Federated credentials (OIDC, no secret): environment subject `repo:${REPO}:environment:${ENVIRONMENT_NAME}`
  (default production) + branch subject `repo:${REPO}:ref:refs/heads/${DEFAULT_BRANCH}` — scripts/setup-deploy-identity.sh:81-104.
- RBAC: Contributor at resource-group scope — scripts/setup-deploy-identity.sh:109-129.
- Pushes repo vars AZURE_CLIENT_ID/AZURE_TENANT_ID/AZURE_SUBSCRIPTION_ID/RESOURCE_GROUP via gh CLI
  — scripts/setup-deploy-identity.sh:137-146.
- Trailing notes enumerate the remaining vars/secrets to set — scripts/setup-deploy-identity.sh:148-171.

Tier 2 implication: the README prereq "Route the deploy through a manually approved replay-lab environment"
would add a new GitHub environment (e.g. replay-lab) + a matching federated credential subject
(`repo:${REPO}:environment:replay-lab`) via this script's add_fic pattern.

### 5.5 scripts/smoke-test.sh — post-deploy two-leg claim summary (ROPC)

- Required env: TENANT_ID, SPA_CLIENT_ID, API_SCOPE, API_BASE_URL, TEST_USERNAME, TEST_PASSWORD
  — scripts/smoke-test.sh:34-39.
- Acquires token A via ROPC (grant_type=password, scope="${API_SCOPE} openid profile") — scripts/smoke-test.sh:73-81.
- Locally decodes leg-1 claims (claim() helper, never prints token) — scripts/smoke-test.sh:56-66, 96-101.
- Calls ${API_BASE_URL}/api/me expecting 200, pretty-prints the two-leg summary — scripts/smoke-test.sh:104-131.
- Optional GitHub step summary — scripts/smoke-test.sh:134-146.

### 5.6 scripts/negative-test.sh — the runtime negative control (gated)

- Required env: TENANT_ID, API_BASE_URL, TEST_SP_CLIENT_ID, TEST_SP_CLIENT_SECRET, SPA_CLIENT_ID,
  API_SCOPE, TEST_USERNAME, TEST_PASSWORD — scripts/negative-test.sh:40-47.
- Attempt 1: mint Graph-audience token via CI test SP client-credentials → present to /api/me → expect 401
  — scripts/negative-test.sh:62-84.
- Attempt 2: acquire token A (ROPC) → present directly to https://graph.microsoft.com/v1.0/me → expect 401
  — scripts/negative-test.sh:89-111.
- Exits non-zero if EITHER mismatched-audience token is accepted (2xx) — scripts/negative-test.sh:116-123.
- Design decision DR-06 (header comment): does NOT reuse a real interactive user token — scripts/negative-test.sh:11-16.

### 5.7 scripts/evidence-kql.kusto — sign-in-log corroboration

- Query 1: interactive SPA sign-in from SigninLogs (AppDisplayName == "Croesus SPA") — scripts/evidence-kql.kusto:15-20.
- Query 2 (PRIMARY corroboration): AADNonInteractiveUserSignInLogs where AppDisplayName == "Croesus API"
  or ResourceDisplayName == "Microsoft Graph"; projects CorrelationId/UserPrincipalName/ResourceIdentity/Status
  — scripts/evidence-kql.kusto:27-38. (Note: the display-name literals "Croesus SPA"/"Croesus API" here differ
  from the provisioned "Croesus GPD Central SPA/API (mock)" — the file header says to adjust them to match.)
- Query 3 (OPTIONAL/ADVANCED): Token Protection bound/unbound from TokenProtectionStatusDetails, extracting
  signInSessionStatus / signInSessionStatusCode (1008 = unbound) — scripts/evidence-kql.kusto:47-58. Header
  explicitly warns 1008 generally will NOT appear for a browser SPA + custom API + Graph flow.

The workflow evidence job runs an inline copy of Query 2 (not the .kusto file) — see 6.2.

---

## 6. CI/CD — .github/workflows/deploy-croesus.yml

### 6.1 Structure and OIDC

- Triggers: push to main + workflow_dispatch — .github/workflows/deploy-croesus.yml:12-14.
- Permissions: id-token: write (OIDC), contents: read — .github/workflows/deploy-croesus.yml:16-18.
- Env vars from vars.*: AZURE_TENANT_ID, AZURE_CLIENT_ID (deploy identity FIC, NOT the API cred),
  AZURE_SUBSCRIPTION_ID, SPA_APP_NAME, API_APP_NAME — .github/workflows/deploy-croesus.yml:20-26.
- Jobs:
  - build-spa (Node 22): builds SPA with VITE_SPA_CLIENT_ID/VITE_TENANT_ID/VITE_API_SCOPE/VITE_API_BASE_URL
    from vars.*; uploads dist — .github/workflows/deploy-croesus.yml:29-52.
  - build-api (.NET 8.0.x): `dotnet publish api/Croesus.Api.csproj -c Release`; uploads artifact
    — .github/workflows/deploy-croesus.yml:54-66.
  - deploy (environment: production — MANUAL GATE): OIDC azure/login@v2 (client-id/tenant-id/subscription-id),
    azure/webapps-deploy@v3 to SPA_APP_NAME and API_APP_NAME. CODE-ONLY deploy (no Bicep step)
    — .github/workflows/deploy-croesus.yml:68-101.

### 6.2 The post-deploy evidence job

- evidence (needs deploy, environment: production) — .github/workflows/deploy-croesus.yml:103-208.
- Env: TENANT_ID, SPA_CLIENT_ID, API_SCOPE, API_BASE_URL (vars.*) + TEST_SP_CLIENT_ID,
  TEST_SP_CLIENT_SECRET, TEST_USERNAME, TEST_PASSWORD (secrets.*) — .github/workflows/deploy-croesus.yml:108-118.
- Gate: "OBO evidence mode" step keys off vars.ENABLE_ROPC_EVIDENCE — .github/workflows/deploy-croesus.yml:126-139.
  When != 'true', smoke + negative tests are SKIPPED (tenant MFA blocks ROPC → AADSTS50079) and the note
  points to interactive SPA validation.
- Smoke test — .github/workflows/deploy-croesus.yml:140-145 (`if: vars.ENABLE_ROPC_EVIDENCE == 'true'` → bash scripts/smoke-test.sh).
- Negative control — .github/workflows/deploy-croesus.yml:146-148 (same gate → bash scripts/negative-test.sh).
- Log Analytics correlation: runs an inline copy of evidence-kql Query 2 via `az monitor log-analytics query`
  against vars.LOG_ANALYTICS_WORKSPACE_ID, tees to $GITHUB_STEP_SUMMARY + obo-evidence.md
  — .github/workflows/deploy-croesus.yml:149-176.
- Portal deep links written to summary — .github/workflows/deploy-croesus.yml:177-192.
- Uploads obo-evidence.md artifact — .github/workflows/deploy-croesus.yml:193-200.

Tier 2 implication: the README prereq "Route the deploy through a manually approved replay-lab environment"
maps to adding a replay-lab environment + a job (or gated steps) that provision the SPA Graph consent and run
a Tier 2 replay evidence step. The existing `environment: production` manual gate + `if: vars.*` step gating
are the established patterns to mirror.

---

## 7. README Tier 2 sections (verbatim extract)

### 7.1 "Tier 2 replay lab (deferred)" — README.md (section after "Read the evidence")

Stated Tier 2 prerequisites (verbatim, README.md):

> Tier 2 prerequisites (deferred):
> - Grant the SPA Microsoft Graph `User.Read` delegated consent.
> - Add the `VITE_ENABLE_REPLAY_DEMO=false` and `Demo:EnableReplay=false` feature gates, off by default.
> - Add an API-authenticated, fixed-target replay endpoint with token-redaction tests.
> - Route the deploy through a manually approved replay-lab environment.

Follow-on items:
- WI-01: "Tier 2 is intentionally deferred behind its own plan (follow-on item WI-01) because it introduces
  a deliberate token-forwarding weakness and requires tenant and app-registration changes."
- WI-02 (from the licensing section): "Confirm the current support matrix when the Tier 2 plan is written
  (follow-on item WI-02)."

Framing (verbatim): "Tier 2 would reproduce the customer's server-side replay shape more faithfully: the SPA
acquires a real Microsoft Graph token, forwards it to an API endpoint, and the API replays it from server-side
context — the same unbound-token pattern the sign-in logs attribute to the Croesus AWS backend."

Boundary (verbatim): "Tier 2 reproduces the replay mechanics. It does not, on its own, emit the literal Token
Protection `1008` (\"unbound\") status — that value is a Conditional Access sign-in-log signal, not an HTTP response".

### 7.2 "Entra ID licensing for the Tier 2 replay lab" — README.md (final section)

Findings (verbatim highlights):

> - Reading the `1008` "unbound" status from sign-in logs is telemetry available with Entra ID P1 or P2 sign-in logs.
> - Enforcing token protection — the Conditional Access grant control **Require token protection for sign-in
>   sessions** (preview) — requires **Microsoft Entra ID P2**.

Tenant (`MngEnvMCAP675646.onmicrosoft.com`, queried 2026-06-30 via Graph subscribedSkus):

| Service plan | Capability | Provisioning status |
| --- | --- | --- |
| AAD_PREMIUM_P2 | Microsoft Entra ID P2 (Token Protection, risk-based CA) | Success |
| AAD_PREMIUM | Microsoft Entra ID P1 (Conditional Access, sign-in logs) | Success |
| MFA_PREMIUM | Multifactor authentication | Success |

Delivered by Microsoft_365_E5_(no_Teams) SKU (5 seats, 2 consumed).

Conclusion (verbatim):
> - **Licensing: present.** Entra ID P2 is provisioned, so a Conditional Access token-protection policy and
>   sign-in-log inspection are both available to the lab.
> - **Feature coverage: partial (preview).** "Require token protection for sign-in sessions" is in preview and
>   currently enforces on a limited set of clients and resources (native Windows desktop apps to Exchange Online
>   and SharePoint Online), not arbitrary browser or SPA calls to Microsoft Graph.

### 7.3 Additional README facts relevant to Tier 2

- The README "Wrong versus right" and "App registration comparison" tables (README.md, "Mock Croesus SaaS OBO
  demo" section) define the good-vs-bad contrast: replay = one token/one aud/no credential/no scope/identical jti;
  OBO = two tokens/distinct aud/cert-in-KV/access_as_user/distinct jti.
- The mermaid sequence diagram (README.md and docs/obo-demo-guide.md:22-40) documents the canonical good-path leg
  ordering.

---

## 8. Docs (docs/) — configuration catalog + demo/evidence narrative

### 8.1 docs/configuration-contract.md — the authoritative config catalog

- Public config table — docs/configuration-contract.md (Public configuration values):
  AZURE_TENANT_ID (VITE_TENANT_ID / AzureAd:TenantId / bicep tenantId),
  SPA_CLIENT_ID (VITE_SPA_CLIENT_ID / bicep spaClientId),
  API_CLIENT_ID (AzureAd:ClientId / bicep apiClientId),
  API_SCOPE=api://<API_CLIENT_ID>/access_as_user (VITE_API_SCOPE / AzureAd:Scopes),
  API_BASE_URL (VITE_API_BASE_URL), SPA_APP_NAME (croesus-spa), API_APP_NAME (croesus-api),
  RESOURCE_GROUP, KEY_VAULT_NAME, LOG_ANALYTICS_WORKSPACE_ID, APPLICATIONINSIGHTS_CONNECTION_STRING.
- Deploy identity table: AZURE_CLIENT_ID, AZURE_SUBSCRIPTION_ID.
- Secret table: croesus-api-cert (Key Vault only), TEST_SP_CLIENT_ID, TEST_SP_CLIENT_SECRET,
  TEST_USERNAME, TEST_PASSWORD (GitHub Actions secrets only).
- Rule (docs/configuration-contract.md, "How a new value enters the contract"): every NEW config value must be
  added as a row before wiring; credentials go in the secret table under the Key Vault-only rule. Tier 2's
  Demo:EnableReplay, VITE_ENABLE_REPLAY_DEMO, VITE_GRAPH_SCOPE must each get a row here.
- Emphasis (docs/configuration-contract.md IMPORTANT callout): "The SPA requests only API_SCOPE. No Microsoft
  Graph scope appears anywhere in SPA configuration." Tier 2 explicitly changes this for the gated lab.

### 8.2 docs/obo-demo-guide.md — provision/deploy/exercise/evidence walkthrough

- Steps: (1) provision KV + registrations, (2) deploy infra + apps, (3) exercise flow (SPA or smoke-test.sh),
  (4) run negative tests (SPA "Replay API token to Graph (wrong)" button OR scripts/negative-test.sh),
  (5) read App Insights claim evidence, (6) correlate Entra sign-in logs.
- docs/obo-demo-guide.md (Step 4) explicitly states the SPA replay button 401 "is not the literal Token
  Protection 1008 signal from the customer logs; it is a safe audience-boundary demonstration".
- Tenancy note: single-tenant demo (signInAudience=AzureADMyOrg); multi-tenant guidance for real SaaS.

### 8.3 docs/evidence-narrative.md — escalation-question mapping + the 1008 boundary

- Headline proof = audience binding (two distinct aud + distinct jti), corroborated by negative tests +
  correlated non-interactive sign-in logs.
- Q1-Q6 mapping table (docs/evidence-narrative.md) ties each escalation question to concrete demo evidence.
- "Why Token Protection 1008 is only an optional exhibit" (docs/evidence-narrative.md): 1008 applies to
  native-app clients → EXO/SPO/Teams, NOT browser SPA → custom API → Graph; presenting 1008 as the OBO proof
  for this shape would assert a signal the platform does not emit here.

---

## 9. Prior in-repo Tier 2 research already exists (reuse, don't redo)

Two prior research documents already scoped Tier 2 (called "Variant C"):

- .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md — defines Variant A/B/C,
  selects the two-tier plan (Tier 1 client-only now, Tier 2 gated server replay behind source/infra/build/CI/auth
  gates). Variant C requirements it lists: SPA Graph User.Read + admin consent; new VITE_GRAPH_SCOPE (default
  User.Read) + optional VITE_GRAPH_BASE_URL; new API endpoint POST /api/replay (API-authenticated, demo-only);
  CORS widened to allow POST.
- .copilot-tracking/research/subagents/2026-06-30/1008-and-real-app-replay.md — authoritative "what 1008 is"
  (Entra Token Protection token-binding status, 0=bound / 1008=unbound; a sign-in-log signal, not AADSTS/HTTP)
  and the real-app replay behavior (AWS backend re-presents the user's Graph token, aud=Graph, User.Read, from
  3.97.32.113). Also records the real app-registration GUIDs and the open AADSTS-code gap.
- .copilot-tracking/research/subagents/2026-06-30/good-path-code-map.md — earlier good-path map with the exact
  bad-path insertion points (authConfig graphRequest, getGraphToken.ts, api.ts sibling call, ReplayPanel,
  MeController has no echo endpoint, CORS is GET-only).

---

## 10. Where a Tier 2 replay endpoint would slot in (gap analysis)

Concrete insertion points, keyed to the README Tier 2 prereqs:

1. SPA Graph token acquisition:
   - Add `graphRequest` (Graph scope) to spa/src/authConfig.ts:26-33 (currently API-scope only).
   - Add getGraphToken.ts sibling to spa/src/getApiToken.ts (silent → popup with graphRequest).
   - Add a `callApiReplay(instance, account)` in spa/src/api.ts that acquires the Graph token and POSTs it to
     the new endpoint (mirroring callApiMe at spa/src/api.ts:65-83, but POST + body).
2. Feature gates (both default OFF):
   - VITE_ENABLE_REPLAY_DEMO — consumed in spa/src/App.tsx to conditionally render the Tier 2 section
     (App.tsx currently always renders the Tier 1 replay button at spa/src/App.tsx:186-188), injected in
     .github/workflows/deploy-croesus.yml build-spa env (lines 41-45) and documented in docs/configuration-contract.md.
   - Demo:EnableReplay — read in api/Program.cs (map the endpoint only when true) or checked inside the new
     controller; default in api/appsettings.json + set via infra/modules/appservice.bicep API app settings
     (Demo__EnableReplay='false' alongside the existing settings at infra/modules/appservice.bicep:116-152).
3. API replay endpoint:
   - New controller (e.g. ReplayController) with `[Authorize][RequiredScope("access_as_user")]` and a POST action
     that reads a forwarded Graph token from the body and re-presents it to a FIXED target
     (https://graph.microsoft.com/v1.0/me) server-side, logging claims-only evidence via OboClaimLogger
     (api/Telemetry/OboClaimLogger.cs) with a NEW event name (e.g. "ReplayAttempt"). Must reuse the JwtShaped
     redaction guard.
   - Register/gate in api/Program.cs (only when Demo:EnableReplay).
   - Widen CORS to allow POST — api/Program.cs:39 `.WithMethods("GET")` → `.WithMethods("GET","POST")`.
4. Tests:
   - Add tests alongside api/Tests/NegativeControlTests.cs asserting the replay endpoint is [Authorize]d and that
     the forwarded token is never logged/returned (token-redaction tests). WebApplicationFactory<Program> host is
     reusable; a Demo:EnableReplay=true in-memory config would exercise the gated endpoint.
5. Provisioning + teardown:
   - Extend scripts/provision-app-registrations.sh to grant the SPA (not just the API) Graph User.Read delegated
     + admin consent (currently API-only at scripts/provision-app-registrations.sh:243-249).
   - Extend scripts/teardown-app-registrations.sh to revoke that SPA Graph grant (reversibility) — the current
     teardown only deletes the two registrations + the cert (scripts/teardown-app-registrations.sh:48-60).
6. Deploy gating:
   - Add a manually approved replay-lab GitHub environment + federated credential subject via
     scripts/setup-deploy-identity.sh add_fic pattern (scripts/setup-deploy-identity.sh:81-104); gate the Tier 2
     provisioning/evidence steps in .github/workflows/deploy-croesus.yml behind it (mirroring the existing
     `environment: production` + `if: vars.ENABLE_ROPC_EVIDENCE == 'true'` patterns).

---

## 11. Existing feature gates and negative-control mechanism (direct answers)

- Existing FEATURE GATES in the codebase today:
  - vars.ENABLE_ROPC_EVIDENCE — CI-only gate on the smoke + negative-test steps (.github/workflows/deploy-croesus.yml:126-148).
    Default off; when off, ROPC steps skip (tenant MFA blocks ROPC).
  - `environment: production` — GitHub manual-approval gate on deploy + evidence jobs (.github/workflows/deploy-croesus.yml:75, 107).
  - NO Demo:EnableReplay and NO VITE_ENABLE_REPLAY_DEMO exist yet (README lists them as Tier 2 prereqs to ADD).
- Current NEGATIVE-CONTROL mechanism (two forms, both audience-binding, NOT 1008):
  - Client-only runtime: spa/src/api.ts:159-215 callGraphWithApiTokenWrongWay → SPA replays its API token to
    Graph → expects 401; rendered by spa/src/components/ReplayAttemptPanel.tsx.
  - Test-suite: api/Tests/NegativeControlTests.cs (Graph-audience token → 401 at /api/me; provable audience
    distinctness) + runtime scripts/negative-test.sh (both mismatched-audience directions → 401).
- TEARDOWN pattern: scripts/teardown-app-registrations.sh — display-name enumeration, existence-guarded delete,
  idempotent, deletes SPA + API registrations + KV cert (+ optional cert purge). Does NOT yet revoke Graph
  grants or reset feature gates.

---

## 12. Open items / clarifying questions for the implementer

1. Endpoint contract: should the Tier 2 endpoint be `POST /api/replay` (SPA forwards a Graph token in the body)
   as prior research proposed (Variant C), or a server-side self-acquire? The README says "the SPA acquires a
   real Microsoft Graph token, forwards it to an API endpoint" — i.e. body-forwarding. Confirm the exact route
   name and request/response schema.
2. Fixed target: prior research and the README both say "fixed-target". Confirm the target is
   https://graph.microsoft.com/v1.0/me (matching the Tier 1 target) and that the endpoint must reject any
   caller-supplied target (to bound the deliberate weakness).
3. Gate semantics: should Demo:EnableReplay gate route REGISTRATION in Program.cs (endpoint absent when off) or
   only in-action 404/403 (endpoint present but refuses)? Absent-when-off is safer.
4. Evidence event name: confirm a distinct App Insights event (e.g. "ReplayAttempt") vs reusing "OboExchange"
   (api/Telemetry/OboClaimLogger.cs:63) — a distinct name keeps evidence-kql/queries unambiguous.
5. 1008 expectation: confirm the plan explicitly documents that Tier 2 reproduces the replay SHAPE only and does
   NOT assert a literal 1008 (per docs/evidence-narrative.md and README licensing section) — to avoid overclaiming.
6. Display-name mismatch: scripts/evidence-kql.kusto uses "Croesus SPA"/"Croesus API" while provisioning creates
   "Croesus GPD Central SPA/API (mock)". Confirm whether Tier 2 evidence queries should be corrected to match.
7. Teardown scope: confirm Tier 2 teardown must revoke the SPA Graph User.Read grant AND reset the feature gates,
   and whether it should also remove the replay-lab GitHub environment (likely manual, out of script scope).
