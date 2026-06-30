# Good-Path Code Map — Croesus OBO Flow (for bad-path "token replay" reproduction design)

Status: Complete

Research scope: Map the existing "good path" OBO demo (SPA + API + infra/config) so a
contrasting "bad path" (unbound token replay anti-pattern) can be designed. RESEARCH ONLY —
no code changes were made.

App-registration facts to validate against (from request):
- SPA (public client) client id: 06ef7c0a-9df3-4bcd-8b6f-ee275ca0adc2
- API (confidential client) client id: bc6338a5-a02a-4ddf-b1f4-9a9234bed8a8
- Tenant: aa93b9d9-037d-4f08-a26d-783cff0e2369
- API scope: api://bc6338a5-a02a-4ddf-b1f4-9a9234bed8a8/access_as_user

NOTE: None of these literal GUIDs appear in committed source. All are injected at build/runtime
via Vite env vars (SPA) or App Service settings (API). See "Env/config var inventory" below.

---

## 1. SPA auth/scope code map

### authConfig.ts (spa/src/authConfig.ts) — the single source of MSAL config and scopes

- Lines 1-5: imports `Configuration`, `PublicClientApplication`, `RedirectRequest` from `@azure/msal-browser`.
- Lines 9-11: reads three env vars — `VITE_TENANT_ID`, `VITE_SPA_CLIENT_ID`, `VITE_API_SCOPE`.
- Lines 13-21: `msalConfig`:
  - `auth.clientId = spaClientId` (VITE_SPA_CLIENT_ID)
  - `auth.authority = https://login.microsoftonline.com/${tenantId}`
  - `auth.redirectUri = window.location.origin`
  - cache: `sessionStorage`, `storeAuthStateInCookie: false`
  - There is NO `knownAuthorities` entry (single-tenant common authority only).
- Lines 26-28: `apiRequest: RedirectRequest = { scopes: [apiScope] }` — the ONLY scope is the API scope.
- Lines 32-34: `loginRequest: RedirectRequest = { scopes: [apiScope] }` — login also uses only the API scope.
- Line 36: `export const pca = new PublicClientApplication(msalConfig)`.

Key facts:
- No Microsoft Graph scope (`User.Read`, `https://graph.microsoft.com/.default`, etc.) appears
  anywhere in SPA source. The comments at lines 23-25 explicitly forbid it.
- `apiScope` value at runtime is `api://<API_CLIENT_ID>/access_as_user`.
- Authority is the standard login.microsoftonline.com tenant authority — Graph is a Microsoft
  first-party resource, so MSAL CAN request a Graph scope against this same authority with no
  `knownAuthorities` change. The only thing stopping a Graph token today is that no Graph request
  object exists.

### main.tsx (spa/src/main.tsx)

- Lines 3-5: imports `MsalProvider`, `pca` (from authConfig), `App`.
- Lines 13-21: `pca.initialize().then(...)` then renders `<MsalProvider instance={pca}><App/></MsalProvider>`.
- No redirect-promise handling (popup model is used, see App.tsx).

### App.tsx (spa/src/App.tsx) — UI + the MSAL calls + where buttons live

- Lines 1-8: imports `useState`, `@azure/msal-react` helpers (`AuthenticatedTemplate`,
  `UnauthenticatedTemplate`, `useMsal`), `loginRequest` from authConfig, `callApiMe`/`MeResponse`
  from api, `EvidencePanel`.
- Lines 10-56: `ContrastPanel()` — a STATIC explanatory two-column panel ("Broken — token replay"
  vs "Correct — On-Behalf-Of"). It is purely descriptive text today; there is NO live bad-path
  code wired to it. This is the natural home for a live "Call API the WRONG way" button.
- Lines 58-66: `SignInButton` — `instance.loginPopup(loginRequest)`. **MSAL call: loginPopup.**
- Lines 68-74: `SignOutButton` — `instance.logoutPopup()`.
- Lines 76-99: button style constants.
- Lines 101-159: `App()` default export:
  - Line 102: `const { instance, accounts } = useMsal();`
  - Lines 103-105: state `data: MeResponse | null`, `error`, `loading`.
  - Lines 107-122: `handleCallApi()` — the GOOD path handler. Gets `accounts[0]`, calls
    `callApiMe(instance, account)`, sets `data` or `error`.
  - Lines 124-159: render. Inside `<AuthenticatedTemplate>` (lines 135-141) it renders the
    "Call API" button bound to `handleCallApi`. Line 147-149 renders `<EvidencePanel data={data}/>`
    when `data` is present. Line 152 renders `<ContrastPanel/>`.

MSAL call summary:
- Interactive sign-in: `instance.loginPopup(loginRequest)` (App.tsx:62).
- Token acquisition: `acquireTokenSilent` then `acquireTokenPopup` fallback (getApiToken.ts, below).
- NO `loginRedirect` / `acquireTokenRedirect` used.

### getApiToken.ts (spa/src/getApiToken.ts) — token A acquisition

- Lines 1-6: imports `AccountInfo`, `IPublicClientApplication`, `InteractionRequiredAuthError`,
  and `apiRequest` from authConfig.
- Lines 16-31: `getApiToken(instance, account)`:
  - Line 20: `instance.acquireTokenSilent({ ...apiRequest, account })` → returns `res.accessToken`
    (token A, aud = API).
  - Lines 22-27: on `InteractionRequiredAuthError`, `instance.acquireTokenPopup(apiRequest)`.
  - Line 28: rethrow other errors.

This is the function a bad-path would PARALLEL: a `getGraphToken.ts` (or a second branch here)
would call `acquireTokenSilent`/`acquireTokenPopup` with a NEW request object whose `scopes` is a
Graph scope (e.g. `["https://graph.microsoft.com/User.Read"]` or `[".../.default"]`). That second
request object does not exist yet — adding it to authConfig.ts (e.g. `graphRequest`) plus a token
helper is the minimal change to make the SPA hold a Graph token.

### api.ts (spa/src/api.ts) — how the SPA calls the API (the fetch)

- Lines 1-2: imports `getApiToken`, MSAL types.
- Lines 8-60: TypeScript interfaces `Leg1Claims`, `Leg2Claims`, `MeResponse` describing the
  API's evidence response shape.
- Lines 65-83: `callApiMe(instance, account)`:
  - Line 71: `const token = await getApiToken(instance, account);` (token A).
  - Line 72: `const baseUrl = import.meta.env.VITE_API_BASE_URL.replace(/\/+$/, "");`
  - Lines 74-76: `fetch(\`${baseUrl}/api/me\`, { headers: { Authorization: \`Bearer ${token}\` } })`.
    **The fetch URL is `{VITE_API_BASE_URL}/api/me`; the only header is `Authorization: Bearer`.**
  - Lines 78-81: throws on non-ok with status + body.
  - Line 83: returns parsed `MeResponse`.

Bad-path insertion options here:
- Add a sibling `callGraphWithReplayedToken(instance, account)` that:
  (a) takes the API token A and `fetch("https://graph.microsoft.com/v1.0/me", { Authorization:
      Bearer <token A> })` — this demonstrates "replay the API token to Graph" (Graph rejects on
      wrong `aud`); OR
  (b) acquires a Graph token directly in the browser and calls Graph (the "browser holds a Graph
      bearer credential" anti-pattern).
- A variant that POSTs the API token to the API for it to echo, IF an echo endpoint existed (it
  does NOT — see section 2).

### EvidencePanel.tsx (spa/src/components/EvidencePanel.tsx) — result rendering

- Lines 1: imports `Leg1Claims`, `Leg2Claims`, `MeResponse` types from ../api.
- Lines 5-7: `apiClientId` derived from `VITE_API_SCOPE` via regex `^api:\/\/([^/]+)\//`. This is
  the ONLY place the SPA derives the API client id, and it does so without hardcoding a downstream
  resource id.
- Lines 9-12: `audIsApi(aud)` helper.
- Lines 15-22: `formatIat()` helper.
- Lines 24-35: `ClaimRow` presentational row (label/value/ok → green/red).
- Lines 37-66: `LegCard` presentational card wrapper.
- Lines 81-156: `EvidencePanel({ data })`:
  - Lines 82-83: pulls `leg1`/`leg2` from `data.evidence`.
  - Lines 85-88: asserts `leg1AudIsApi`, `leg1ScpOk` (`scp === "access_as_user"`),
    `distinctAud` (leg1.aud !== leg2.aud), `upn`.
  - Lines 90-156: renders two `LegCard`s (leg 1 token A, leg 2 token B) + a checklist `<ul>`.
- Lines 158: `export default EvidencePanel`.

The contents folder (spa/src/components/) holds ONLY `EvidencePanel.tsx`. A bad-path result panel
could be a new sibling component (e.g. `ReplayPanel.tsx`) rendered from `ContrastPanel` or a new
state branch in App.tsx, mirroring how `EvidencePanel` is rendered when `data` is set.

---

## 2. API endpoints + audience-validation behavior

Only ONE controller exists: api/Controllers/MeController.cs. There is NO token-echo / forwarding /
introspection endpoint anywhere in the API.

### MeController.cs

- Lines 22-25: class attributes — `[Authorize]`, `[ApiController]`, `[Route("api/[controller]")]`
  (→ route `api/me`), `[RequiredScope("access_as_user")]`.
- Lines 27-28: constants `GraphResource = "https://graph.microsoft.com"`,
  `GraphScopes = { "https://graph.microsoft.com/User.Read" }`.
- Lines 30-46: ctor injects `GraphServiceClient`, `ITokenAcquisition`, `IConfiguration`,
  `OboClaimLogger`.
- Lines 48-145: `Get()` — the single GET `api/me` action:
  - Lines 51-64: **defensive audience binding.** Reads inbound `aud`/`appid_aud` claim,
    compares against `AzureAd:ClientId` and `AzureAd:Audience`; if mismatch, returns
    `401 Unauthorized` with `{ error = "invalid_audience" }` and does NOT call OBO.
  - Lines 67-76: leg-1 evidence dictionary from inbound token claims.
  - Lines 81-84: `GetAuthenticationResultForUserAsync(GraphScopes, ...)` — the OBO exchange (token B).
  - Line 87: `_graph.Me.Request().GetAsync()` — Graph `/me` call using token B.
  - Lines 90-97: OBO request "shape" evidence (grant_type, requested_token_use, scope, credentialSource).
  - Lines 101-108: leg-2 evidence from MSAL result (aud=graph, scp, expiresOn, correlationId, tokenSource).
  - Line 110: `_claimLogger.LogExchange(...)`.
  - Lines 112-144: returns `Ok(new { user, evidence = { leg1, leg2 } })`.
- Lines 147-159: `FirstClaim(params string[] types)` helper.
- Lines 161-184: `AudienceMatchesThisApi(audience, clientId, configuredAudience)` — returns true
  if audience equals configured `AzureAd:Audience`, OR equals bare `clientId` (v2), OR equals
  `api://{clientId}` (v1). Case-insensitive.

### Audience validation — two layers

1. Framework layer (Program.cs:21 `AddMicrosoftIdentityWebApi(builder.Configuration, "AzureAd")`):
   validates issuer + audience + signature of the inbound token against `AzureAd` config. A token
   whose `aud` is NOT this API (e.g. a Graph token, `aud = https://graph.microsoft.com`/`00000003-...`)
   is rejected with **401 before the action runs.**
2. Action layer (MeController.cs:51-64): explicit belt-and-suspenders `aud` check → 401
   `invalid_audience` even if the framework somehow let it through.

Consequences for a bad-path demo:
- Sending a **Graph-audienced token to `api/me`** → 401 (framework audience validation fails;
  Graph aud != API aud). Good "replay rejected" evidence.
- Sending the **API-audienced token A directly to Graph** (`graph.microsoft.com/v1.0/me`) → Graph
  returns 401 `InvalidAudience` (token A's aud is the API, not Graph). This is the cleanest
  "unbound token replay rejected by the downstream resource" demonstration and needs NO API change.
- There is no endpoint that echoes/forwards a presented token, so a "SPA posts token, API replays
  it to Graph" variant is NOT possible without adding a new (intentionally-vulnerable) endpoint.

### Program.cs (api/Program.cs)

- Line 10: `AddApplicationInsightsTelemetry()`.
- Lines 19-24: the Microsoft.Identity.Web fluent chain —
  `AddAuthentication(JwtBearer).AddMicrosoftIdentityWebApi(config,"AzureAd")
   .EnableTokenAcquisitionToCallDownstreamApi().AddMicrosoftGraph(config.GetSection("Graph"))
   .AddInMemoryTokenCaches()`.
- Line 26: `AddAuthorization()`.
- Lines 31-43: CORS policy `SpaCors` from `Cors:AllowedOrigins`, `.AllowAnyHeader().WithMethods("GET")`.
  NOTE: only GET is allowed cross-origin — a bad-path that needs a POST to the API would require a
  CORS method change.
- Line 46: `AddScoped<OboClaimLogger>()`.
- Line 48: `AddControllers()`.
- Lines 52-56: `UseCors(SpaCors)` → `UseAuthentication()` → `UseAuthorization()` → `MapControllers()`.
- Line 61: `public partial class Program {}` (for test host).

### OboClaimLogger.cs (api/Telemetry/OboClaimLogger.cs)

- Lines 22-24: `JwtShaped` regex — any 3-segment base64url string is treated as a token.
- Lines 38-77: `LogExchange(leg1, oboRequestShape, leg2)` — writes structured props to App Insights
  (`TrackEvent("OboExchange", ...)`) and an ILogger info line. All values pass through `Redact`.
- Lines 84-92: `Redact(value)` — replaces token-shaped strings with `[REDACTED:token]`.

Relevance to bad-path: if a replay demo logs anything, this redactor will mask raw tokens. Any new
evidence logging should reuse `OboClaimLogger` to stay consistent.

---

## 3. Env / config var inventory

### SPA Vite vars (all public, non-secret)

| Var | Defined / injected | Consumed at |
| --- | --- | --- |
| `VITE_SPA_CLIENT_ID` | spa/.env.example:7; CI deploy-croesus.yml:43 (`vars.SPA_CLIENT_ID`) | authConfig.ts:10 (`msalConfig.auth.clientId`) |
| `VITE_TENANT_ID` | spa/.env.example:10; CI deploy-croesus.yml:44 (`vars.AZURE_TENANT_ID`) | authConfig.ts:9 (authority) |
| `VITE_API_SCOPE` | spa/.env.example:13; CI deploy-croesus.yml:45 (`vars.API_SCOPE`) | authConfig.ts:11 (apiRequest/loginRequest scope); EvidencePanel.tsx:5 (derives apiClientId) |
| `VITE_API_BASE_URL` | spa/.env.example:16; CI deploy-croesus.yml:46 (`vars.API_BASE_URL`) | api.ts:72 (fetch base URL) |

- Only `spa/.env.example` exists; there is NO `spa/.env` / `spa/.env.local` / `spa/.env.*`
  committed. Local devs copy `.env.example` → `.env.local` (per the file's header comment).
- `vite.config.ts` (spa/vite.config.ts) does NOT customize `envPrefix`; default `VITE_` prefix
  applies. Dev server port 3000 (matches redirect URI http://localhost:3000 per the comment).

NEW vars a bad-path would need (do not exist today):
- A Graph scope/resource for the SPA to request directly — e.g. `VITE_GRAPH_SCOPE`
  (`https://graph.microsoft.com/User.Read`) and/or `VITE_GRAPH_BASE_URL`
  (`https://graph.microsoft.com/v1.0`). These would be added to spa/.env.example, the CI
  build-spa env block (deploy-croesus.yml:42-46), and consumed by a new `graphRequest` in
  authConfig.ts + a new fetch in api.ts. (A pure "replay token A to Graph" variant needs no Graph
  scope var — only a Graph base URL — since it reuses the existing API token.)

### API config keys (App Service settings → IConfiguration)

| Key | Source | Used at |
| --- | --- | --- |
| `AzureAd:Instance` | appsettings.json:3; bicep `AzureAd__Instance` (appservice.bicep:114-117) | Program.cs:21 |
| `AzureAd:TenantId` | appsettings.json:4; bicep `AzureAd__TenantId` (118-121) | Program.cs:21 |
| `AzureAd:ClientId` | appsettings.json:5; bicep `AzureAd__ClientId` (122-125) | MeController.cs:54,177 |
| `AzureAd:Audience` | appsettings.json:6 (`api://<id>`); bicep `AzureAd__Audience` (126-129) | MeController.cs:55,171 |
| `AzureAd:ClientCredentials:0:SourceType` | appsettings.json:8; bicep `...__0__SourceType` = `Base64Encoded` (130-139) | OBO credential; MeController.cs:94 (evidence) |
| `AzureAd:ClientCredentials:0:Base64EncodedValue` | bicep KV reference (140-143) | confidential-client cert |
| `Graph:BaseUrl` / `Graph:Scopes` | appsettings.json:11-14 | Program.cs:23 (`AddMicrosoftGraph`) |
| `Cors:AllowedOrigins` (`Cors__AllowedOrigins__0`) | appsettings.json:16-18 (empty); bicep (153-156) = SPA host | Program.cs:32-39 |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | bicep KV reference (144-147) | Program.cs:10 |

- appsettings.json ships placeholders (`REPLACE_WITH_...`); real values come from App Service
  settings injected by infra/modules/appservice.bicep.

### Infra outputs (infra/main.bicep)

- main.bicep:95 `spaAppName`, 98 `apiAppName`, 101 `spaHostName`, 104 `apiHostName`,
  107 `keyVaultUri`, 110 `appInsightsConnectionStringSecretUri`. `API_BASE_URL` (the SPA's
  `VITE_API_BASE_URL`) is derived from `apiHostName` and stored as the `vars.API_BASE_URL`
  GitHub Actions variable (per docs/configuration-contract.md:35).

### GitHub Actions workflow (.github/workflows/deploy-croesus.yml)

- Jobs: `build-spa` (lines 30-52, injects the four VITE_* vars at lines 42-46), `build-api`
  (54-66), `deploy` (68-103, `environment: production`, OIDC login, webapps-deploy for both),
  `evidence` (105+, runs post-deploy OBO evidence using a CI test service principal:
  `TEST_SP_CLIENT_ID/SECRET`, `TEST_USERNAME/PASSWORD` secrets — a ROPC-style token fetch).
- The `build-spa` env block (lines 42-46) is the single place a new `VITE_GRAPH_*` build var would
  be added for a bad-path build.

---

## 4. Exact insertion points for a bad-path (replay) demo

1. authConfig.ts (spa/src/authConfig.ts:26-34): add a SECOND request object next to `apiRequest`,
   e.g. `export const graphRequest: RedirectRequest = { scopes: [graphScope] };` where `graphScope`
   = `import.meta.env.VITE_GRAPH_SCOPE` (new var) or a literal Graph scope. Only needed for the
   "SPA directly holds a Graph token" variant; NOT needed for the "replay API token A to Graph" variant.
2. New token helper (mirror getApiToken.ts): a `getGraphTokenDirect()` calling
   `acquireTokenSilent/Popup(graphRequest)` — for variant (b). For variant (a), reuse `getApiToken`
   and just send token A to Graph.
3. api.ts (spa/src/api.ts:65-83): add `callGraphWrongWay(instance, account)`:
   - Variant (a) replay: `const token = await getApiToken(...)` then
     `fetch("https://graph.microsoft.com/v1.0/me", { headers: { Authorization: \`Bearer ${token}\` }})`.
     Expect Graph 401 (wrong aud) → the "replay rejected" evidence.
   - Variant (b) browser-holds-Graph: acquire graph token then call Graph (succeeds, but proves the
     anti-pattern of a Graph bearer credential living in the browser).
   Return a small result shape (status, aud-of-token, graph error body).
4. App.tsx (spa/src/App.tsx):
   - Add state (e.g. `replayResult`, `replayError`) next to lines 103-105.
   - Add a `handleCallApiWrong()` handler next to `handleCallApi` (lines 107-122).
   - Wire a "Call API the WRONG way" button inside `ContrastPanel` (lines 10-56) or in the
     `<AuthenticatedTemplate>` button row (lines 135-141).
5. Result panel: either extend `ContrastPanel` to render `replayResult`/`replayError`, or add a new
   `spa/src/components/ReplayPanel.tsx` (sibling of EvidencePanel.tsx) rendered from App.tsx when
   `replayResult` is set (mirror EvidencePanel rendering at App.tsx:147-149).
6. Env: add `VITE_GRAPH_SCOPE` (+ optional `VITE_GRAPH_BASE_URL`) to spa/.env.example and the
   build-spa env block (deploy-croesus.yml:42-46) — only if doing the direct-Graph-token variant.
7. API side: NO change required for the recommended replay demos (Graph rejects token A; the API
   rejects a Graph token via existing audience validation). If a "vulnerable echo endpoint" variant
   is desired, a NEW controller/action would be added and CORS in Program.cs:31-43 would need the
   method (e.g. POST) added — but that intentionally weakens the API and is not required to show
   the anti-pattern.

---

## 5. MSAL Graph-scope feasibility check

- Authority is the standard tenant authority `https://login.microsoftonline.com/<tenant>`
  (authConfig.ts:15). Graph is a Microsoft first-party resource reachable from this authority, so
  MSAL needs NO `knownAuthorities` entry to request a Graph scope. The config does not set
  `knownAuthorities` (only needed for B2C/custom authorities) — irrelevant here.
- Redirect URI = `window.location.origin` (authConfig.ts:16). Requesting a Graph scope uses the
  same redirect URI, so no redirect-URI change is needed.
- Cache is `sessionStorage` (authConfig.ts:18) — a directly-acquired Graph token would persist in
  sessionStorage (the exfiltration risk the contrast panel calls out).
- Therefore the SPA is fully CAPABLE of requesting a Graph scope today; the only reason it doesn't
  is that no Graph request object is defined. Admin consent for the SPA app reg to call Graph
  delegated `User.Read` may be required at runtime, but that's an Entra config concern, not a code
  blocker.

---

## 6. Unanswered questions / things to confirm before building the bad path

- [ ] Which bad-path variant is wanted: (a) replay the API token A to Graph (clean "rejected by
      downstream" demo, no Graph consent needed), (b) SPA directly acquires + holds a Graph token
      (shows the browser-bearer-credential anti-pattern, needs SPA Graph consent), or (c) a new
      intentionally-vulnerable API echo/forward endpoint (requires API + CORS changes)?
- [ ] Does the SPA app registration (06ef7c0a-...) have delegated `User.Read` on Graph + admin
      consent? Required only for variant (b). Not visible in repo code/config.
- [ ] Should the bad-path evidence be logged server-side (reuse OboClaimLogger) or stay purely
      client-side in the SPA? No server logging path exists for a pure client replay.
- [ ] If a POST-based variant is chosen, confirm willingness to widen CORS (Program.cs:31-43,
      currently GET-only) and add an endpoint — this materially changes the API's security surface.
