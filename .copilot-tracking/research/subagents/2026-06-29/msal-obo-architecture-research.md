# MSAL On-Behalf-Of (OBO) Architecture Research — Croesus / GPD Central

> Status: Complete
> Date: 2026-06-29
> Subagent research document: .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md

## Research Topics / Questions

1. Exact OBO token exchange (jwt-bearer grant): request shape, assertion, and why it is NOT token replay (would not trigger Token Protection "unbound"/1008).
2. Recommended MSAL libraries + minimal config: SPA (@azure/msal-browser + @azure/msal-react) and middle-tier API (Node @azure/msal-node vs ASP.NET Core Microsoft.Identity.Web). Recommend ONE backend.
3. Token Protection / token binding: "bound (0)" vs "unbound (1008)"; how confidential-client OBO yields legitimately-issued downstream tokens.
4. Runnable minimal code snippets: SPA msalConfig + acquireTokenSilent; backend OBO exchange; backend Graph /me call.
5. Best practices: certificates over secrets, audience/scope validation, demonstrably bound not replayed.

---

## Executive Summary (key technical facts)

- **OBO is a token EXCHANGE, not a relay.** The middle-tier API does not forward the user's access token to Microsoft Graph. Instead it presents that token to the **Entra token endpoint** as a signed `assertion` under the OAuth2 `urn:ietf:params:oauth:grant-type:jwt-bearer` grant with `requested_token_use=on_behalf_of`, authenticating itself as a **confidential client**. Entra validates the inbound token + the client's credential and **mints a brand-new access token (token B) whose `aud` is Microsoft Graph**. ([OBO flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow), updated 2025-01-04)
- **Why this is NOT replay:** in token replay, the *same* bearer token (audience = API A) is reused/sent somewhere it was not minted for. In OBO, the original token is consumed as proof-of-user-delegation at the token endpoint; what goes to Graph is a **freshly issued, Graph-audience token** legitimately created by Entra. Microsoft explicitly **warns against relaying the user's access token** to anywhere other than its intended audience, listing "inability to satisfy token binding" as a risk — OBO is the prescribed alternative. ([OBO flow — "DO NOT send access tokens…"](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))
- **Recommended backend for a minimal, correct demo: ASP.NET Core + Microsoft.Identity.Web.** A single fluent chain `AddMicrosoftIdentityWebApi(...).EnableTokenAcquisitionToCallDownstreamApi().AddMicrosoftGraph(...).AddInMemoryTokenCaches()` validates the inbound token AND performs the OBO exchange + Graph call with almost no hand-written OAuth code, removing the chance of accidentally building a replay. ([web API calling APIs](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-web-api-call-api-overview), updated 2024-07-19)
- **Bound vs unbound** is a Token Protection (Conditional Access) concept: only **device-bound** sign-in session tokens (PRTs cryptographically tied to the device, often via TPM) are accepted; **bearer** refresh tokens usable from any device are rejected. A correct confidential-client OBO does not "rebind" anything at the device layer — it produces **legitimately issued downstream tokens** rather than replayed bearer artifacts, which is the architectural property the Croesus mock app must demonstrate. ([Token Protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection), updated 2026-03-24; [Protecting tokens](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id), updated 2025-04-24)
- **Use a certificate (private_key_jwt), not a client secret**, for the confidential client. Both are supported in OBO; the certificate path replaces `client_secret` with `client_assertion_type` + `client_assertion` (a JWT signed by the cert, `alg=PS256`). ([certificate-credentials](https://learn.microsoft.com/en-us/entra/identity-platform/certificate-credentials), updated 2025-01-04)

---

## 1. The exact OBO token exchange (and why it is not replay)

### 1.1 Protocol flow (Microsoft Learn, authoritative)

Source: [Microsoft identity platform and OAuth2.0 On-Behalf-Of flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow) (ms.date 2025-01-04, page updated 2026-06-15).

1. SPA signs the user in (auth code + PKCE) and acquires **token A** — an access token whose `aud` is **API A** (the middle tier).
2. SPA calls API A with token A in `Authorization: Bearer`.
3. API A **authenticates to the Entra token endpoint** (with its own client credential) and requests a token for **API B** (Microsoft Graph), passing token A as the `assertion`.
4. Entra **validates API A's credentials AND token A**, then **issues token B** (audience = Graph) back to API A.
5. API A sets token B in the `Authorization` header of its request to Graph.

> Key constraint: token A's `aud` claim **must** be API A. "Applications can't redeem a token for a different app (for example, if a client sends an API a token meant for Microsoft Graph, the API can't redeem it using OBO. It should instead reject the token)." (OBO flow, *Middle-tier access token request* table.)

### 1.2 The raw request — shared-secret form

```http
POST /<tenant>/oauth2/v2.0/token HTTP/1.1
Host: login.microsoftonline.com
Content-Type: application/x-www-form-urlencoded

grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer
&client_id=<middle-tier-app-client-id>
&client_secret=<middle-tier-secret>
&assertion=<token A — the user's access token presented to API A>
&scope=https://graph.microsoft.com/user.read+offline_access
&requested_token_use=on_behalf_of
```

Required parameters (OBO flow doc):

| Parameter | Required value / meaning |
| --- | --- |
| `grant_type` | `urn:ietf:params:oauth:grant-type:jwt-bearer` |
| `client_id` | Middle-tier app (client) ID |
| `client_secret` | Middle-tier secret (or replaced by cert assertion — see 1.3) |
| `assertion` | The access token sent to the middle tier; its `aud` MUST be the middle-tier app |
| `scope` | Space-separated downstream scopes (e.g. Graph `user.read offline_access`) |
| `requested_token_use` | MUST be `on_behalf_of` |

### 1.3 The raw request — certificate (private_key_jwt) form

Replaces `client_secret` with two parameters:

```http
grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer
&client_id=<middle-tier-app-client-id>
&client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer
&client_assertion=<JWT signed by the app's certificate private key>
&assertion=<token A>
&requested_token_use=on_behalf_of
&scope=https://graph.microsoft.com/user.read+offline_access
```

### 1.4 Success response — proof of a NEW token

```json
{
  "token_type": "Bearer",
  "scope": "https://graph.microsoft.com/user.read",
  "expires_in": 3269,
  "access_token": "eyJhbGciO...",     // <-- token B: aud = Microsoft Graph, NEWLY issued by Entra
  "refresh_token": "OAQABAAAA..."     // only if offline_access requested
}
```

The doc notes token B is a **v1.0-formatted token for Microsoft Graph** because the format is based on the *resource*, regardless of which endpoint requested it — additional evidence that Entra **minted a new resource-specific token**, not a pass-through of token A.

### 1.5 Why OBO is NOT "token replay" (the 1008 distinction)

| Aspect | Token REPLAY (the bug to avoid) | OBO EXCHANGE (correct) |
| --- | --- | --- |
| What hits Graph | The **same** token the SPA gave the API (`aud` = API A) | A **new** token (`aud` = Graph) minted by Entra |
| Who issued the Graph-bound token | Nobody — it was never issued for Graph | Entra token endpoint, after validating the API's confidential-client credential |
| Confidential-client auth | None — backend just forwards bytes | Required (`client_secret` or cert `client_assertion`) |
| `requested_token_use=on_behalf_of` | Absent | Present |
| Audience validity at Graph | Token's `aud` ≠ Graph → invalid / wrong-audience | Token's `aud` = Graph → valid |
| Entra's view of the call | An artifact being reused outside its intended audience (replay-shaped) | A legitimate delegated token issuance event |

Microsoft's own warning in the OBO doc frames the anti-pattern precisely: *"DO NOT send access tokens that were issued to the middle tier to anywhere except the intended audience for the token,"* citing risks including **"Inability to satisfy token binding and Conditional Access scenarios."** A backend that simply replays the user's interactive token to Graph is exactly this anti-pattern; a real OBO exchange is the prescribed fix. Because OBO produces a **legitimately issued, correctly-audienced token through a credentialed confidential client**, it does not present as a replayed/unbound bearer artifact (the condition Token Protection flags as status **unbound / code 1008** in Entra sign-in logs).

> Citation caveat: the numeric **"bound = 0" / "unbound = 1008"** values are surfaced in **Entra sign-in log Token Protection evaluation detail** ("Token Protection — Sign-in Session Token" / sign-in session token status). The conceptual definitions (device-bound PRT accepted vs bearer rejected) are documented at the Token Protection and Protecting-Tokens Learn pages cited in §3; the specific integer codes are an operational artifact of the sign-in log UI rather than a dedicated conceptual Learn article. Treat the numbers as sign-in-log status codes, not a documented OBO API value.

---

## 2. Recommended MSAL libraries + minimal config

### 2.1 SPA — `@azure/msal-browser` + `@azure/msal-react`

Pattern (Learn, [Acquire a token (SPA)](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-spa-acquire-token), updated 2025-05-12):

- Sign in with `loginРedirect` / `loginPopup` (PublicClientApplication / `MsalProvider`).
- Always try **`acquireTokenSilent`** first for the **middle-tier API scope** (e.g. `api://<api-client-id>/access_as_user`), fall back to `acquireTokenPopup` / `acquireTokenRedirect` on `InteractionRequiredAuthError`.
- Critical: the SPA requests a scope **for the API**, NOT a Graph scope. The SPA must never hold a Graph token — that is what forces the OBO boundary and prevents the replay shortcut.

### 2.2 Backend option A — Node.js / Express + `@azure/msal-node`

- Validate the inbound bearer token (e.g. `passport-azure-ad` BearerStrategy or jose/jwks).
- Exchange via `ConfidentialClientApplication.acquireTokenOnBehalfOf({ oboAssertion: <incoming user token>, scopes: ["https://graph.microsoft.com/User.Read"] })`.
- Pros: same language as the SPA; explicit, easy to *show* the exchange step in a demo. Cons: you hand-wire token validation, scope checks, caching, and the Graph call → more surface area to accidentally implement a replay.

### 2.3 Backend option B — ASP.NET Core + `Microsoft.Identity.Web` (RECOMMENDED)

Source: [Configure a web API that calls web APIs](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-web-api-call-api-overview) + [Get a token…](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-web-api-call-api-acquire-token).

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(Configuration.GetSection("AzureAd"))   // validates inbound token (issuer, audience, signature)
    .EnableTokenAcquisitionToCallDownstreamApi()                       // turns on OBO machinery
    .AddMicrosoftGraph(Configuration.GetSection("GraphBeta"))          // GraphServiceClient does the OBO + /me call
    .AddInMemoryTokenCaches();
```

The framework performs the OBO exchange automatically when the controller (decorated `[Authorize]` + `[RequiredScope("access_as_user")]`) injects `GraphServiceClient` or `IAuthorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync(scopes)`.

### 2.4 Recommendation + justification

**Recommend ASP.NET Core + Microsoft.Identity.Web for the Croesus mock app.** Rationale:

1. **Correct-by-construction:** the single fluent chain both validates the inbound API token and performs OBO; there is no place to "just forward" the token, so the demo cannot accidentally model the replay bug it is meant to disprove.
2. **Minimal code:** no hand-written `/token` POST, JWKS validation, or caching — the smallest amount of bespoke OAuth to maintain.
3. **First-class certificate + Key Vault support** via the `ClientCredentials` config array (`SourceType: KeyVault`), directly enabling the "cert over secret" best practice (§5) with zero code change.
4. **Built-in audience/scope enforcement** (`RequiredScope`, audience validation) — the validation properties the app must *prove* are present.

> If team familiarity is strictly Node/TypeScript, Option A is acceptable, but the demo must explicitly show `acquireTokenOnBehalfOf` and assert that the SPA never receives a Graph-scoped token.

---

## 3. Token Protection / token binding — bound (0) vs unbound (1008)

Sources: [How Token Protection enhances Conditional Access](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection) (2026-03-24); [Protecting Tokens in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id) (2025-04-24); [Understanding Tokens](https://learn.microsoft.com/en-us/entra/identity/devices/concept-tokens-microsoft-entra-id) (2025-05-01).

- **Token Protection** is a Conditional Access *session control* that reduces token-replay by ensuring **only device-bound sign-in session tokens (PRTs)** are accepted. When a device registers with Entra, a PRT is issued and **cryptographically bound to that device** (client secret stored in TPM on Windows). Bearer refresh tokens — usable from any device — are **automatically rejected**.
- **Bound (status 0):** the sign-in session token presented is cryptographically tied to the device it was issued to; replay from another device fails.
- **Unbound (status 1008):** the token is a portable **bearer** artifact with no device binding — the exact thing replay attacks exploit, and what Token Protection blocks. This is the status the Croesus app must avoid producing in its downstream call path.
- **How correct OBO relates:** OBO operates at the **app-session / delegated-issuance** layer, not the device-PRT layer. It does not "device-bind" the Graph token, but it guarantees the downstream token is **legitimately issued by Entra to the correct audience through a credentialed confidential client**, instead of a replayed bearer token presented outside its intended audience. The "unbound/1008" signal arises when a sign-in *session* token is replayed as a portable bearer; a confidential-client OBO exchange is a distinct, sanctioned issuance event and therefore does not present as that replayed-bearer condition.
- **Platform note:** Token Protection enforcement today is GA on **Windows native apps** for Exchange/SharePoint/Teams; **browser-based apps are not yet supported** for enforcement. So for a SPA front end the practical, demonstrable guarantee is the **OBO architecture** (no Graph token in the browser; credentialed exchange in the backend), reinforced by network/CAE controls.

---

## 4. Concrete minimal code snippets

### 4.1 SPA — `msalConfig` + `acquireTokenSilent` for the API scope

```ts
// authConfig.ts
import { Configuration, PublicClientApplication } from "@azure/msal-browser";

export const msalConfig: Configuration = {
  auth: {
    clientId: "SPA_CLIENT_ID",
    authority: "https://login.microsoftonline.com/TENANT_ID",
    redirectUri: "http://localhost:3000",
  },
  cache: { cacheLocation: "sessionStorage", storeAuthStateInCookie: false },
};

export const apiRequest = {
  // NOTE: scope is for the MIDDLE-TIER API, NOT Microsoft Graph.
  scopes: ["api://API_CLIENT_ID/access_as_user"],
};

export const pca = new PublicClientApplication(msalConfig);
```

```tsx
// useApiToken.ts  (React)
import { useMsal } from "@azure/msal-react";
import { InteractionRequiredAuthError } from "@azure/msal-browser";
import { apiRequest } from "./authConfig";

export async function getApiToken(instance, account) {
  try {
    const res = await instance.acquireTokenSilent({ ...apiRequest, account });
    return res.accessToken; // token A — aud = API_CLIENT_ID
  } catch (e) {
    if (e instanceof InteractionRequiredAuthError) {
      const res = await instance.acquireTokenPopup(apiRequest);
      return res.accessToken;
    }
    throw e;
  }
}
// Call backend:  fetch("/api/me", { headers: { Authorization: `Bearer ${token}` }})
```

### 4.2 Backend OBO exchange — Node `@azure/msal-node`

```ts
import { ConfidentialClientApplication } from "@azure/msal-node";

const cca = new ConfidentialClientApplication({
  auth: {
    clientId: "API_CLIENT_ID",
    authority: "https://login.microsoftonline.com/TENANT_ID",
    // Prefer a certificate (see §5); clientSecret shown for brevity only.
    clientSecret: process.env.API_CLIENT_SECRET,
  },
});

// incomingUserToken = bearer token from the SPA (aud = API_CLIENT_ID), already validated.
async function graphTokenOnBehalfOf(incomingUserToken: string) {
  const result = await cca.acquireTokenOnBehalfOf({
    oboAssertion: incomingUserToken,
    scopes: ["https://graph.microsoft.com/User.Read"],
  });
  return result!.accessToken; // token B — aud = Microsoft Graph
}
```

### 4.3 Backend OBO + Graph `/me` — ASP.NET Core `Microsoft.Identity.Web` (recommended)

```csharp
[Authorize]
[RequiredScope("access_as_user")]
[ApiController]
[Route("api")]
public class MeController : ControllerBase
{
    private readonly GraphServiceClient _graph;
    public MeController(GraphServiceClient graph) => _graph = graph;

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        // OBO exchange + Graph call happen inside the SDK; no manual token handling.
        var me = await _graph.Me.GetAsync();
        return Ok(new { me?.DisplayName, me?.UserPrincipalName });
    }
}
```

Equivalent "show the exchange explicitly" variant (still no raw HTTP):

```csharp
string[] graphScopes = { "User.Read" };
string authHeader = await _authHeaderProvider
    .CreateAuthorizationHeaderForUserAsync(graphScopes); // performs OBO, returns "Bearer <token B>"

var http = new HttpClient();
http.DefaultRequestHeaders.Add("Authorization", authHeader);
var json = await http.GetStringAsync("https://graph.microsoft.com/v1.0/me");
```

### 4.4 Backend calling Graph `/me` with the OBO token — raw HTTP (any stack)

```http
GET /v1.0/me HTTP/1.1
Host: graph.microsoft.com
Authorization: Bearer <token B — aud = https://graph.microsoft.com>
```

---

## 5. Best practices

### 5.1 Certificate over client secret (private_key_jwt)

- OBO supports both secret and certificate; Microsoft documents the certificate path as a drop-in replacement of `client_secret` with `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer` + a `client_assertion` JWT signed by the app's cert. ([certificate-credentials](https://learn.microsoft.com/en-us/entra/identity-platform/certificate-credentials))
- Assertion specifics: header `alg=PS256`, `typ=JWT`, `x5t#S256` thumbprint; claims `aud = https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token`, `iss = sub = {clientId}`, short `exp` (5–10 min), unique `jti`. PSS padding for signature.
- In `Microsoft.Identity.Web`, configure via `ClientCredentials` with `SourceType: KeyVault` (cert stored in Azure Key Vault) — no code change, secret never on disk.
- For the Croesus mock, using a cert (ideally Key-Vault-backed) demonstrates a hardened confidential client and avoids long-lived shared secrets.

### 5.2 Audience / scope validation on the API

- The API MUST validate the inbound token's **audience = its own app ID/URI**, a trusted **issuer**, and signature. `AddMicrosoftIdentityWebApi` does this automatically; in Node, configure BearerStrategy `audience`/`issuer`/`validateIssuer:true`.
- Enforce the **delegated scope** (e.g. `access_as_user`) per endpoint (`[RequiredScope]` / manual `scp` check). The OBO doc stresses the API must **reject** a token whose `aud` is not itself (e.g. a token meant for Graph) — never attempt OBO with it.
- Consent: pre-authorize the SPA on the API (or use `knownClientApplications` + `.default` combined consent) and grant the middle tier delegated Graph `User.Read`. ([OBO flow — Gaining consent](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))

### 5.3 Making the flow demonstrably "bound, not replayed"

Demonstration assertions the mock app should surface:

1. **SPA never holds a Graph token** — its only requested scope is `api://API_CLIENT_ID/access_as_user`. (Inspect the SPA token: `aud` = API, not Graph.)
2. **Two distinct tokens exist** — log/inspect token A (`aud` = API) at the backend boundary, and token B (`aud` = `https://graph.microsoft.com`) after the exchange. Different `aud`, different issuance = proof of exchange, not relay.
3. **Confidential-client credential is required** — show the exchange fails without the secret/cert, proving Entra issued token B only after authenticating the API.
4. **`requested_token_use=on_behalf_of`** is present on the wire (visible in Node raw form; implicit in Microsoft.Identity.Web).
5. **Negative control** — optionally include a "wrong way" code path that forwards token A directly to Graph and show Graph rejects it (wrong audience), making the OBO-vs-replay contrast concrete.
6. Layer **network/CAE controls** for app-session protection since browser-based Token Protection enforcement is not yet supported.

---

## References (Microsoft Learn, with dates)

- OAuth2.0 On-Behalf-Of flow — https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow (ms.date 2025-01-04; page updated 2026-06-15). Grant type, assertion, requested_token_use, shared-secret & cert request forms, success/error responses, "DO NOT relay tokens" warning, consent.
- Configure a web API that calls web APIs — https://learn.microsoft.com/en-us/entra/identity-platform/scenario-web-api-call-api-overview (ms.date 2024-07-19). `EnableTokenAcquisitionToCallDownstreamApi`, `AddMicrosoftGraph`, `AddDownstreamApi`, `ClientCredentials`/KeyVault, Node/Java/Python OBO.
- Get a token for a web API that calls web APIs — https://learn.microsoft.com/en-us/entra/identity-platform/scenario-web-api-call-api-acquire-token (ms.date 2025-03-21). `IAuthorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync`, Python `acquire_token_on_behalf_of`.
- Acquire a token to call a web API (SPA) — https://learn.microsoft.com/en-us/entra/identity-platform/scenario-spa-acquire-token (ms.date 2025-05-12). `acquireTokenSilent` → `acquireTokenPopup`/`Redirect`, React/MSAL examples.
- Certificate credentials — https://learn.microsoft.com/en-us/entra/identity-platform/certificate-credentials (ms.date 2025-01-04). private_key_jwt, PS256, client_assertion format, registration.
- How Token Protection enhances Conditional Access — https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection (ms.date 2026-03-24). Device-bound PRT accepted, bearer rejected; browser apps unsupported for enforcement.
- Protecting Tokens in Microsoft Entra ID — https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id (ms.date 2025-04-24). PRT TPM binding, enforce-Token-Protection, network/CAE replay defenses.
- Understanding Tokens in Microsoft Entra ID — https://learn.microsoft.com/en-us/entra/identity/devices/concept-tokens-microsoft-entra-id (ms.date 2025-05-01). Sign-in session (refresh) vs app-session (access) tokens; revocability/lifetimes.
- (Archived sample, illustrative) Node OBO raw request — https://github.com/Azure-Samples/ms-identity-nodejs-webapi-onbehalfof-azurefunctions/blob/master/Function/MyHttpTrigger/index.js. Shows literal `grant_type=jwt-bearer`, `assertion`, `requested_token_use=on_behalf_of` POST.

---

## Clarifying Questions

1. **Backend language constraint?** Is the Croesus team committed to Node/TypeScript end-to-end, or is a .NET middle tier acceptable? The recommendation (ASP.NET Core + Microsoft.Identity.Web) is strongest if .NET is allowed; if Node is mandated, the demo must add explicit OBO-vs-replay assertions.
2. **Tenant/CA reality:** Will the demo run in a tenant where Token Protection / Conditional Access is actually enforced (so "unbound/1008" can be observed in sign-in logs), or is the goal purely to model the *architecture* that avoids producing replayed tokens? This affects whether to include live sign-in-log screenshots.
3. **Credential storage:** Is Azure Key Vault available for the certificate, or should the demo use a local PFX for simplicity?
4. **Graph scope set:** Is `User.Read` (`/me`) sufficient for the proof, or are additional delegated Graph scopes in scope for Croesus / GPD Central?

---

## Recommended next research (not completed this session)

- [ ] Exact Entra **sign-in log schema** field that emits Token Protection "Sign-in Session Token" status with the literal `0`/`1008` codes (KQL `SigninLogs` / `tokenProtectionStatusCode`) to cite an authoritative source for the numbers.
- [ ] `@azure/msal-node` **reference doc / GitHub** for the precise `acquireTokenOnBehalfOf` parameter object (`oboAssertion` vs `assertion`) and current version semantics (the Learn Node OBO deep-link 404'd; the archived sample uses raw HTTP).
- [ ] App-registration wiring specifics for the mock: SPA redirect URIs, API **exposed scope** (`access_as_user`), `knownClientApplications`, and pre-authorized-application config for seamless combined consent.
- [ ] Whether to add **CAE** (Continuous Access Evaluation) to the middle tier so the downstream token honors near-real-time revocation — strengthens the "not just a static bearer" story.
- [ ] Validation tooling: a scripted check that asserts token A `aud` = API and token B `aud` = Graph (e.g. decode + compare) for an automated "bound not replayed" test.
