<!-- markdownlint-disable-file -->

# Croesus and Desjardins identity evidence research

## Research scope

This research reconstructs the Croesus and Desjardins identity chronology, tests the claims against the repository implementation, and distinguishes demonstrated behavior from assumptions and simulations.

Questions under investigation:

* What exactly did Mathieu Santerre and Olivier Leblanc claim, and in what order?
* What flow does the repository implement for `/token`, OBO, replay, issuer, audience, scopes, app roles, and downstream APIs?
* Which assumptions are proved by tests or live evidence, and which are simulated?
* What does `assets/latest-info/image.png` support?
* Which contradictions and ambiguities remain?
* What is the smallest discriminating live evidence needed?

## Evidence status rubric

* **Source claim**: a statement made in the supplied customer correspondence
* **Repository fact**: behavior directly established by source code or configuration
* **Automated simulation**: behavior established only under test doubles, synthetic tokens, or mocked downstream services
* **Live evidence**: behavior established from an external system's telemetry or response
* **Inference**: a conclusion that depends on one or more unverified premises

## Executive findings

The repository does not establish that Croesus replayed an access token. It establishes a narrower set of facts:

1. Desjardins observed a second, non-interactive sign-in from Croesus AWS egress and associated it with `Token Protection` sign-in-session status `Unbound (statusCode 1008)` (source claim in `assets/latest-info/email-thread-with-croesus.md:223-236` and `assets/latest-info/email-thread-with-croesus.md:613-633`).
2. Olivier states that Central uses OAuth authorization code with PKCE, does not use OBO, and performs the mandatory code redemption at the Azure `/token` endpoint from a Croesus server (source claim in `assets/latest-info/email-thread-with-croesus.md:1-5`). This is protocol-plausible. Microsoft's authorization-code documentation says the code is redeemed by `POST` to `/token` with `grant_type=authorization_code`; PKCE adds the matching `code_verifier`. A `/token` call is not specific to OBO.
3. The three supplied Croesus application-object exports are single-tenant SPA registrations with delegated Microsoft Graph `User.Read`, no exposed API scope, no app roles, and no secret or certificate (`assets/dev-dev.txt:18-46`, `assets/dev-dev.txt:57-70`, `assets/dev-prod.txt:18-46`, `assets/dev-prod.txt:57-70`, `assets/prod-prod.txt:18-46`, and `assets/prod-prod.txt:57-70`). These three objects cannot act as the confidential OBO middle tier implemented by the mock. This does not prove token replay and does not exclude an undisclosed backend registration.
4. The `1008` value means the sign-in session is unbound. The Microsoft Graph schema describes `bound` requests as passing the Token Protection session control and exposes `unbound` as another status. It does not define `1008` as proof that an access token was reused. Token Protection enforcement currently supports native applications, not browser applications, and only a limited resource set. Therefore, `1008` is evidence about session binding, not a protocol trace proving replay or OBO.
5. The mock repository implements a real OBO-shaped good path and two deliberately different negative controls. It does not reproduce the unknown Croesus implementation:
	* The good path invokes Microsoft.Identity.Web token acquisition for Graph after validating an API-audienced inbound token (`api/Program.cs:16-29`, `api/Controllers/MeController.cs:48-89`).
	* Tier 1 sends an API-audienced token to Graph and expects audience rejection (`spa/src/api.ts:157-214`). This is an audience-mismatch test, not token replay evidence.
	* Tier 2a acquires a valid Graph bearer token in the SPA, forwards it to the API, and has the API present it to Graph (`spa/src/api.ts:265-302`, `api/Controllers/ReplayController.cs:45-118`). This simulates bearer-token reuse from another process. It does not establish what Croesus does and cannot itself emit or prove `1008`.
6. The seven focused .NET tests pass, but they use synthetic HS256 tokens, disable issuer validation, avoid Entra, and stub Graph with a canned `401` (`api/Tests/NegativeControlTests.cs:34-65`, `api/Tests/NegativeControlTests.cs:68-117`, `api/Tests/ReplayEndpointTests.cs:21-30`, and `api/Tests/ReplayEndpointTests.cs:136-220`). They prove local routing, audience configuration, endpoint gating, and redaction. They do not prove live OBO, live replay, Token Protection, Conditional Access, issuer behavior, or Croesus behavior.
7. The smallest discriminating evidence is one correlated transaction containing the `/token` request shape and the token identity used for the next Graph call. The decisive fields are `grant_type`, presence of `code` plus `code_verifier` versus `assertion` plus `requested_token_use=on_behalf_of`, the client ID and credential type, requested resource/scope, and safe hashes or `jti`/`iat`/`aud` projections of the inbound and downstream tokens. Network location alone cannot distinguish authorization-code redemption from replay.

The current README's categorical replay conclusion is stronger than the repository evidence supports (`README.md:6-9`, `README.md:53-63`, and `README.md:86-90`). The correct current status is **unresolved between ordinary server-side authorization-code redemption and a later token-reuse operation**. The former is directly asserted by Croesus; the latter remains a Desjardins/Microsoft interpretation that needs token-level or request-level corroboration.

## Customer chronology and claims

### 13 July 2026

Stéphane Girard forwards Microsoft's original escalation to Croesus. The escalation says Desjardins observed a second non-interactive sign-in from the Croesus AWS backend, interprets it as a replayed user token to Microsoft Graph rather than OBO, identifies `Unbound (statusCode 1008)`, states the Conditional Access block is correct, and asks Croesus for the authoritative server-side flow, app registration, token audiences and issuance identifiers, and AWS egress ranges (`assets/latest-info/email-thread-with-croesus.md:600-668`).

The message simultaneously states a conclusion and asks for the evidence needed to establish that conclusion. The sentence at `assets/latest-info/email-thread-with-croesus.md:625-633` is therefore a claim based on sign-in-log interpretation, not a token-level demonstration included in this repository.

### 14 to 15 July 2026

Elisabeth states that Desjardins chose a more secure connection posture, that Microsoft sees nothing abnormal in Desjardins's controls, and that Croesus should review Microsoft's response; Annie and Stéphane arrange a joint Croesus, Microsoft, and Desjardins meeting (`assets/latest-info/email-thread-with-croesus.md:404-466`, `assets/latest-info/email-thread-with-croesus.md:333-423`). These messages add governance context but no protocol evidence.

### 20 July 2026

Olivier says the issue had been identified earlier as differing production and nonproduction AWS egress IPs plus Desjardins not authorizing the nonproduction IPs. He says an API call to public Azure authentication infrastructure exits through Croesus's Internet gateway because no VPN route exists. He supplies:

* Production: `3.97.32.113`, `35.182.44.169`
* Nonproduction: `3.99.119.124`, `35.183.224.214`

Source: `assets/latest-info/email-thread-with-croesus.md:283-310`.

This supports an AWS-originated call to Azure and environment-specific egress. It does not identify the OAuth grant, token audience, or downstream use.

### 22 July 2026

Mathieu states that none of the Croesus IPs are defined in Desjardins Conditional Access policies. He claims production works only because Desjardins devices are compliant when the Croesus AWS server reuses tokens redirected to it, and that Croesus uses those tokens to authenticate to the tenant under the user's identity. He cites `Unbound (statusCode 1008)`. He attributes nonproduction failure to devices being compliant in only one tenant and to nonproduction access being limited to the Desjardins controlled network (`assets/latest-info/email-thread-with-croesus.md:223-236`).

Mathieu's message establishes Desjardins's causal theory:

1. Browser authentication creates user tokens.
2. Croesus receives those tokens.
3. Croesus reuses them from AWS to authenticate to Entra or Graph.
4. Production device-compliance claims let the operation succeed.
5. Nonproduction device and location policy blocks it.

The supplied source does not include the raw sign-in rows, token identifiers, request body, or Conditional Access evaluation needed to verify steps 2 and 3.

### 23 July 2026

Olivier replies that Mathieu's explanation changes his understanding and says he will send it to the authentication-service teams. He asks whether the same behavior occurs for Central and Conseiller (`assets/latest-info/email-thread-with-croesus.md:165-187`). This does not concede replay; it acknowledges a new interpretation and asks whether two products share it.

### 27 July 2026

Elisabeth asks Croesus whether its authentication service has completed a new analysis (`assets/latest-info/email-thread-with-croesus.md:31-78`).

### Latest Olivier response, after 27 July and before Mathieu's 28 July request

Olivier reports additional details from Central:

* Central uses "OAuth + PKCE."
* OBO is not used.
* A server-side Azure `/token` call is mandatory and exits through Croesus public IPs.
* He does not understand why that call is identified as replay.
* He asks whether OIDC is expected, whether `/token` should run behind a Desjardins proxy, and whether Croesus IPs should be trusted.

Source: `assets/latest-info/email-thread-with-croesus.md:1-5`.

The copied email has no visible sent-date header. Its placement above the 27 July message and Mathieu's request on 28 July provide relative chronology, but an exact timestamp is not present in the repository.

### 28 July 2026 Mathieu request

Mathieu asks Emmanuel to schedule a meeting and help draft a response. He links conceptually to Microsoft's app sign-in flow and OIDC documentation, questions why `/token` would be mandatory without OBO, notes that refresh-token work has not begun, and asks whether `/token` causes the same Conditional Access behavior as OBO (`assets/latest-info/mathieu-santerre.md:1-10`).

The key technical misunderstanding is that `/token` is shared by several grants. The endpoint alone cannot identify OBO. The screenshot and Microsoft documentation explicitly say it redeems an authorization code, refresh token, or client credential. OBO is another specific token-endpoint request shape.

## Repository implementation

### Mock good path

The repository's corrected architecture is a mock, not a copy of Croesus production.

* `spa/src/authConfig.ts:15-38` configures a tenant-specific MSAL public client, session storage, and an API-only login/token request.
* `spa/src/getApiToken.ts:14-30` uses `acquireTokenSilent` and falls back to `acquireTokenPopup`. MSAL handles authorization-code plus PKCE. The source does not manually post to `/token`.
* `spa/src/api.ts:59-80` places the API token in `Authorization: Bearer` for `GET /api/me`.
* `api/Program.cs:16-29` configures Microsoft.Identity.Web API token validation, downstream token acquisition, Microsoft Graph, and an in-memory token cache.
* `api/Controllers/MeController.cs:48-65` performs a defensive audience check in addition to authentication middleware.
* `api/Controllers/MeController.cs:67-89` records selected inbound claims, invokes `GetAuthenticationResultForUserAsync` for Graph `User.Read`, and then calls Graph `/me`.
* `api/Controllers/MeController.cs:90-109` records the *intended* OBO request shape and selected MSAL result metadata.
* `api/Telemetry/OboClaimLogger.cs:35-76` emits an `OboExchange` event and structured log fields.

The mock's app-registration provisioning deliberately creates two registrations. The API gets `api://<id>`, `access_as_user`, SPA preauthorization, a Key Vault certificate, and delegated Graph `User.Read`; the SPA gets the API delegated permission (`scripts/provision-app-registrations.sh:104-207`, `scripts/provision-app-registrations.sh:209-264`). Tier 2 separately adds SPA Graph `User.Read` (`scripts/provision-app-registrations.sh:266-296`).

Infrastructure supplies the API's single-tenant identity settings, audience, Base64-encoded PKCS#12 certificate through a Key Vault reference, CORS origin, and disabled replay gate (`infra/modules/appservice.bicep:101-162`). The API is VNet-integrated with route-all outbound behavior (`infra/modules/appservice.bicep:93-105`).

### Mock negative controls

Tier 1 is not a same-token replay against the token's intended audience. It intentionally presents an API-audienced token to Graph, which should reject it because the resource is wrong (`spa/src/api.ts:157-214`). It demonstrates resource audience enforcement only.

Tier 2a acquires two tokens:

1. An API token authenticates the request to `POST /api/replay`.
2. A Graph token is sent in the JSON body.

The API places the forwarded Graph token in a new Bearer request to fixed Graph `/me` (`spa/src/api.ts:265-302`, `api/Controllers/ReplayController.cs:45-89`). This is a real bearer-token forwarding shape if run live. A valid Graph bearer token may be accepted from the backend because ordinary bearer tokens are designed to be presented by their holder. Acceptance alone does not prove a vulnerability, lack of audience binding, or Token Protection failure. Rejection status alone also does not prove the reason was token binding.

Both replay gates default off. The API controller is removed from MVC when `Demo:EnableReplay` is false (`api/Program.cs:55-68`), and the UI appears only when `VITE_ENABLE_REPLAY_DEMO` is exactly `true` (`spa/src/App.tsx:16-19`, `spa/src/App.tsx:160-174`, and `spa/src/App.tsx:206-210`). The normal workflow build does not pass either replay SPA variable (`.github/workflows/deploy-croesus.yml:38-49`), while the replay-lab job changes only the API app setting (`.github/workflows/deploy-croesus.yml:242-256`). As written, the replay-lab job does not rebuild the SPA with `VITE_ENABLE_REPLAY_DEMO=true`, so its UI control remains absent unless another build supplied that variable.

### Real Croesus registration evidence

The three application exports consistently show:

* `signInAudience = AzureADMyOrg`
* SPA redirect URIs
* no `identifierUris`
* no `oauth2PermissionScopes`
* no `preAuthorizedApplications`
* no `appRoles`
* no `keyCredentials`
* no `passwordCredentials`
* delegated Microsoft Graph `User.Read`

Sources: `assets/dev-dev.txt:18-46`, `assets/dev-dev.txt:57-70`, `assets/dev-dev.txt:86-92`; `assets/dev-prod.txt:18-46`, `assets/dev-prod.txt:57-70`, `assets/dev-prod.txt:86-91`; and `assets/prod-prod.txt:18-46`, `assets/prod-prod.txt:57-70`, `assets/prod-prod.txt:86-91`.

These facts support a public-client authorization-code/PKCE design that directly requests Graph `User.Read`. They rule out these application objects being the confidential API registration in the repository's OBO design. They do not answer:

* whether the code and PKCE verifier are redeemed in the browser or on a Croesus server;
* whether a separate confidential backend registration exists;
* whether the resulting Graph token is used only for the intended Graph call or copied from another leg;
* whether Central and Conseiller use the same flow;
* which exact token or session artifact produced the `1008` row.

## Assumption inventory

| Topic | Current repository assumption or claim | Evidence assessment |
| --- | --- | --- |
| `/token` purpose | Mathieu treats `/token` as unexpected unless OBO or refresh is used (`assets/latest-info/mathieu-santerre.md:6-10`). | Incorrect as a general protocol premise. `/token` also redeems authorization codes. |
| `/token` network origin | Olivier says Central's server redeems at `/token` and therefore Azure sees Croesus public egress (`assets/latest-info/email-thread-with-croesus.md:1-5`). | Plausible source claim, consistent with authorization-code redemption by a server. Needs request trace. |
| OBO | Olivier says OBO is not used (`assets/latest-info/email-thread-with-croesus.md:3-4`). | Direct vendor statement, not independently verified. |
| Replay | Mathieu and the Microsoft escalation say Croesus reuses a user token from AWS (`assets/latest-info/email-thread-with-croesus.md:229-236`, `assets/latest-info/email-thread-with-croesus.md:621-633`). | Interpretation, not demonstrated by included token/request evidence. |
| `1008` meaning | README equates unbound `1008` with replay (`README.md:6-15`, `README.md:53-55`). | Overstated. `1008` supports an unbound sign-in session, not the exact application operation that caused it. |
| Conditional Access | The CA block is correct by design (`README.md:6-9`, customer escalation at `assets/latest-info/email-thread-with-croesus.md:633-633`). | Likely for the configured policy outcome, but the exact failed policy, grant controls, and AADSTS code are absent. |
| IP allowlisting | Olivier initially attributes failure to untrusted nonproduction egress (`assets/latest-info/email-thread-with-croesus.md:303-310`). | Network facts are supplied; whether allowlisting is the correct security remedy depends on grant and policy evidence. |
| Proxy location | Olivier asks whether `/token` should run on a Desjardins proxy (`assets/latest-info/email-thread-with-croesus.md:4-5`). | No protocol requirement supports this. Moving code redemption changes source IP but does not determine whether the grant is correct. |
| Issuer | Mock API expects a configured tenant and Microsoft.Identity.Web validates issuer, audience, signature, and lifetime (`api/Program.cs:16-29`; configuration in `infra/modules/appservice.bicep:112-126`). | True for the mock configuration. Real Croesus token issuer and validation settings are not supplied. Tests explicitly disable issuer validation (`api/Tests/NegativeControlTests.cs:103-115`). |
| Audience | Mock assumes leg 1 is API-audienced and leg 2 is Graph-audienced (`api/Controllers/MeController.cs:48-59`, `api/Controllers/MeController.cs:99-108`). | Leg 1 is read from claims; leg 2 `aud` is a hard-coded `https://graph.microsoft.com` label, not decoded from token B. A successful Graph call would corroborate resource suitability, but the tests never make it. |
| Fresh `jti` and `iat` | README and docs say token B has a new `jti` and `iat` (`README.md:112-126`, `docs/evidence-narrative.md:20-30`, `docs/obo-demo-guide.md:137-145`). | Not emitted by current code. `MeController` explicitly does not decode Graph token B and records no leg-2 `jti` or `iat` (`api/Controllers/MeController.cs:99-108`). |
| OBO credential | Mock uses an API certificate sourced from Key Vault (`scripts/provision-app-registrations.sh:209-254`, `infra/modules/appservice.bicep:128-143`). | Configured intent is clear. Live successful loading is not covered by unit tests. |
| Credential telemetry | Docs say evidence records certificate thumbprint (`docs/evidence-narrative.md:36-38`, `docs/obo-demo-guide.md:137-145`). | Current logger receives `credentialSource` and `credentialName`; infrastructure uses `Base64Encoded`, while `MeController` looks for `KeyVaultCertificateName`, so name/thumbprint is absent (`api/Controllers/MeController.cs:90-97`, `infra/modules/appservice.bicep:128-143`). |
| Scopes | Real exports request delegated Graph `User.Read`; mock SPA requests `access_as_user`; mock API requests Graph `User.Read`; gated replay SPA also requests Graph `User.Read`. | Established by exports and code. No app roles are involved. |
| App roles | README groups "app roles/scopes" conceptually, but the real exports and mock provisioning use delegated scopes, not app roles. | `appRoles` is empty in all real exports; mock creates no app roles. |
| Downstream Graph behavior | README/docs assume API-token-to-Graph returns `401`; replay endpoint interprets `401` as binding rejection and success as lack of caller binding (`spa/src/api.ts:196-213`, `api/Controllers/ReplayController.cs:122-139`). | `401` for wrong audience is a valid expectation. Tier 2 interpretations are non-exclusive: status can reflect expiry, consent, CA, claims challenge, token type, or other causes. |
| OBO removes CA block | Customer escalation and README imply a fresh OBO token means the block disappears (`assets/latest-info/email-thread-with-croesus.md:633-633`, `README.md:53-63`). | Not guaranteed. OBO is still subject to downstream Conditional Access and claims challenges; outcome depends on policy and client/resource support. |
| Token Protection scope | Some README text implies broad replay enforcement while `docs/evidence-narrative.md:43-51`, `docs/obo-demo-guide.md:176-187`, and `scripts/evidence-kql.kusto:58-91` correctly narrow enforcement to native clients and supported resources. | Internal contradiction. Official current docs say browser-based apps are not supported. |
| Correlation | Evidence queries assume interactive and noninteractive rows can be joined by `CorrelationId` plus user (`scripts/evidence-kql.kusto:93-122`). | Best-effort only, as the script itself notes. It does not prove token identity. |
| Product equivalence | Olivier asks whether Central and Conseiller show the same behavior (`assets/latest-info/email-thread-with-croesus.md:181-181`). | Unresolved. Repository evidence is labeled Central/GPD and cannot be generalized to Conseiller. |

## Test proof assessment

The focused test run on 28 July 2026 passed all seven tests.

| Test or script | What it proves | What it does not prove |
| --- | --- | --- |
| `GraphAudienceToken_PresentedToApi_IsRejectedWith401` (`api/Tests/NegativeControlTests.cs:34-46`) | The test host rejects a locally signed token whose `aud` is not the configured API audience. | Real Entra signature, issuer, CA, Graph token format, or Croesus behavior. |
| `NoToken_PresentedToApi_IsRejectedWith401` (`api/Tests/NegativeControlTests.cs:48-56`) | Authentication is required. | Any OAuth flow detail. |
| `TokenA_PresentedDirectlyToGraph_WouldBeRejected_BecauseAudienceIsNotGraph` (`api/Tests/NegativeControlTests.cs:58-65`) | Only that two string constants differ. | It does not call Graph and does not observe `401`. The method name and comments overstate the assertion. |
| Negative-control test host (`api/Tests/NegativeControlTests.cs:68-117`) | Real middleware/routing can be exercised under a local trust anchor. | It uses synthetic HS256 tokens, an empty OIDC configuration, `ValidateIssuer=false`, and no Entra call. |
| Replay-disabled test (`api/Tests/ReplayEndpointTests.cs:46-59`) | The controller route is absent when the gate is false. | Live deployment gate state. |
| Replay unauthenticated and wrong-audience tests (`api/Tests/ReplayEndpointTests.cs:61-69`, `api/Tests/ReplayEndpointTests.cs:100-114`) | Local endpoint auth behavior. | Real issuer, CA, Graph, or replay outcome. |
| Replay redaction test (`api/Tests/ReplayEndpointTests.cs:71-98`) | Raw forwarded JWT is absent from response and captured formatted logs under the tested patterns. | Application Insights ingestion, all future token formats, or security of request-body handling outside logs. |
| Stub Graph handler (`api/Tests/ReplayEndpointTests.cs:136-220`) | Deterministic controller behavior when downstream returns a canned `401`. | Microsoft Graph's actual decision or reason. |
| `scripts/smoke-test.sh:55-116` | If run live and returning `200`, it shows ROPC token acquisition, API acceptance, OBO-path completion, and a successful Graph `/me` call. | Browser PKCE, MFA/device CA context, leg-2 token `jti`/`iat`, or Croesus production. Repository contains no captured output proving it ran. |
| `scripts/negative-test.sh:54-117` | If run live, Graph-token-to-API and API-token-to-Graph audience rejection. | OBO freshness, replay, `1008`, or browser/device policy. It uses client credentials and ROPC, not the customer flow. |
| Workflow evidence job (`.github/workflows/deploy-croesus.yml:123-197`) | Automates optional ROPC checks and Log Analytics query. | ROPC checks default off. The query filters `AppDisplayName == "Croesus API"`, while provisioning defaults to `Croesus GPD Central API (mock)`, so it may miss API rows. No result artifact is stored in the repository. |
| Tier 2 lab (`.github/workflows/deploy-croesus.yml:199-324`) | Provisions a report-only supported-resource Token Protection policy, enables API replay route, and queries logs. | It does not execute the SPA replay action and does not rebuild the gated UI. A query returning old unrelated `1008` rows would not prove relation to the replay endpoint. |

No automated test invokes `MeController.Get` through a successful OBO exchange. The dependencies `ITokenAcquisition` and `GraphServiceClient` are not mocked for a success-path unit test, and no integration test asserts an `OboExchange` event. Thus, the code is OBO-shaped, but the checked-in test suite does not prove the good path executes.

## Image evidence

`assets/latest-info/image.png` is a screenshot of the Microsoft Learn "OIDC endpoint overview" table. The highlighted Token row states:

* Endpoint: Token
* URL path: `/oauth/v2.0/token`
* Method: `POST`
* Purpose: "Redeems an authorization code, refresh token, or client credential for tokens."
* Details link text: "OAuth 2.0 auth code flow"

The screenshot also shows Discovery, Authorize, UserInfo, JWKS, and Logout rows and the tenant-scoped authority `https://login.microsoftonline.com/{tenant}/v2.0`.

The image supports exactly these conclusions:

1. `/token` is part of the OIDC/OAuth endpoint set.
2. Authorization-code redemption is an ordinary reason to call `/token`.
3. Refresh is not the only non-OBO reason to call `/token`.
4. The endpoint is overloaded across grant types, so observing its URL cannot identify the grant.

The image does not support any of these conclusions:

* that Croesus uses OBO;
* that Croesus replays a token;
* that Croesus uses a refresh token or client credentials;
* whether the call should originate at Croesus, Desjardins, a browser, or a proxy;
* the request's `client_id`, credential, code, verifier, assertion, scope, or audience;
* why Conditional Access blocked the observed sign-in;
* whether `1008` was caused by this `/token` call.

The screenshot is consistent with and materially strengthens Olivier's explanation. It directly answers Mathieu's question about why `/token` is required before refresh and without OBO.

## External links and relevance

### Links embedded in repository sources

* [Token Protection 1008 evidence wiki](https://github.com/devopsabcs-engineering/croesus/wiki/Token-Protection-1008-Evidence), from `README.md:41`. Potentially decisive live evidence, but the repository does not contain the linked screenshots or query output. It must be inspected separately before relying on the claim.
* [Live demo state wiki](https://github.com/devopsabcs-engineering/croesus/wiki/Live-Demo-State), from `README.md:210`. Relevant to mock lab state, not the real Croesus flow. Not embedded locally.
* [Microsoft Token Protection deployment guide](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows), from `README.md:217`. Highly relevant. It states Token Protection currently supports native applications only and lists supported resources.
* `https://login.microsoftonline.com/organizations/v2.0/adminconsent`, from `docs/obo-demo-guide.md:252-259`. Relevant only to the guide's hypothetical multitenant design, not evidence about current Croesus behavior.
* `https://spsfondation.dev.desjardins.com/affwebservices/tools/oidc-tool.html`, `https://pat-gpd-central.certif.desjardins.com/CentralWebApp/LogonSso.aspx`, and `https://gpd-central.desjardins.com/CentralWebApp/LogonSso.aspx`, from `assets/app-registration-analysis-findings.md:54-56` and the application exports. These are redirect URIs and establish environment registration shape. They do not expose token grant details.
* `https://login.microsoftonline.com/MVTDEVDesjardins.onmicrosoft.com/v2.0/.well-known/openid-configuration` and `https://login.microsoftonline.com/mvtdesjardins.onmicrosoft.com/v2.0/.well-known/openid-configuration`, from `assets/app-registration-verification.md:46-53`. Relevant for tenant metadata and issuer discovery, not replay determination.

`assets/latest-info/mathieu-santerre.md:3-8` contains rendered link titles truncated with an ellipsis, not embedded URLs. The likely documents are Microsoft's app sign-in flow and OIDC protocol pages, but the repository does not preserve their exact target URLs. `assets/latest-info/image.png` visibly corresponds to the official OIDC endpoint overview.

### First-party protocol references used to assess the claims

* [OAuth 2.0 authorization code flow: redeem a code](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-auth-code-flow#redeem-a-code-for-an-access-token) establishes `POST /token`, `grant_type=authorization_code`, `code`, and PKCE `code_verifier`; it also distinguishes public clients from confidential web apps.
* [OAuth 2.0 On-Behalf-Of flow: middle-tier request](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow#middle-tier-access-token-request) establishes `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, inbound access-token `assertion`, `requested_token_use=on_behalf_of`, downstream `scope`, and a confidential-client credential.
* [OpenID Connect endpoint overview](https://learn.microsoft.com/entra/identity-platform/v2-protocols-oidc#oidc-endpoint-overview) matches the supplied screenshot and confirms `/token` redeems authorization codes, refresh tokens, or client credentials.
* [Token Protection overview](https://learn.microsoft.com/entra/identity/conditional-access/concept-token-protection#overview) states that Token Protection validates device-bound sign-in session tokens for supported applications and currently supports native applications only, not browser-based applications.
* [Token Protection supported resources](https://learn.microsoft.com/entra/identity/conditional-access/concept-token-protection#supported-resources) lists Exchange Online, SharePoint Online, Teams, and, on Windows, Azure Virtual Desktop and Windows 365.
* [tokenProtectionStatusDetails schema](https://learn.microsoft.com/graph/api/resources/tokenprotectionstatusdetails?view=graph-rest-beta) defines `none`, `bound`, `unbound`, and `unknownFutureValue`, but does not equate `unbound` or code `1008` with a proven application access-token replay.

## Contradictions and ambiguities

### Customer-level contradictions

* Mathieu says the AWS server reuses redirected tokens; Olivier says Central performs server-side authorization-code plus PKCE redemption and no OBO. Both can produce Azure-visible server traffic. Only request and token identity evidence distinguishes them.
* Olivier's 20 July explanation assumes nonproduction IP allowlisting is the missing condition. Mathieu says no Croesus IPs are allowlisted in either environment and attributes production success to compliant-device claims. The exact CA policy evaluation is absent, so neither causal model is fully established.
* Olivier asks whether Central and Conseiller behave the same. The repository analyzes Central/GPD and contains no separate Conseiller evidence.

### Repository narrative contradictions

* `README.md:6-9` says raw sign-in logs confirm replay, while `docs/evidence-narrative.md:43-51` correctly says `1008` should not be used as OBO proof and is not expected for the browser/custom-API shape.
* `README.md:21-41` says a browser sign-in to the mock API produced `1008`, while `docs/obo-demo-guide.md:176-187` and `scripts/evidence-kql.kusto:58-91` say the 1008 exhibit requires a native client and supported Microsoft 365 resource and never appears for the Croesus OBO path. This may reflect broader diagnostic logging than enforcement support, but the local files do not reconcile it.
* `README.md:86-90` infers "no credential + no exposed API scope" means the second sign-in can only be replay. The inference excludes ordinary authorization-code redemption for a directly requested Graph token and excludes an undisclosed backend registration.
* `docs/evidence-narrative.md:24-30`, `docs/obo-demo-guide.md:137-145`, and `README.md:136-143` claim leg-2 `jti` and `iat` are logged. `api/Controllers/MeController.cs:99-108` explicitly does not decode token B and emits neither value.
* `docs/evidence-narrative.md:36-38` and `docs/obo-demo-guide.md:137-145` say certificate thumbprint is logged. Current code logs a source and optional name, not thumbprint; the infrastructure uses `Base64Encoded`, not `KeyVaultCertificateName` (`api/Controllers/MeController.cs:90-97`, `infra/modules/appservice.bicep:128-143`).
* `api/Controllers/MeController.cs:139-139` says a distinct audience proves exchange. The `leg2.aud` value is a constant assigned by code, not a decoded claim. A successful Microsoft.Identity.Web acquisition plus Graph call is strong mock runtime evidence, but the returned evidence object alone is not cryptographic proof.
* `api/Controllers/ReplayController.cs:122-139` equates successful Graph acceptance with lack of audience binding and describes `401` as expected when the token is "bound." A Graph-audienced bearer token can be validly accepted regardless of which process presents it; `401` or `403` has multiple possible causes. The interpretation is not discriminating.
* `docs/configuration-contract.md:34-34` maps API scope to `AzureAd:Scopes`, but the API source uses `[RequiredScope("access_as_user")]` and infrastructure does not set `AzureAd__Scopes`. This documentation mapping is not the active enforcement mechanism.
* `README.md:147-148` says the deploy workflow provisions Bicep infrastructure, while `docs/obo-demo-guide.md:84-105` and the workflow show infrastructure is deployed out of band and the workflow deploys code only.

### Evidence ambiguities

* "Second sign-in" can mean authorization-code redemption, silent token acquisition, refresh-token redemption, OBO, or a resource call causing claims evaluation. App/resource, `IsInteractive`, source IP, and `1008` do not alone reveal the request body.
* A noninteractive Graph sign-in under the user's identity is compatible with delegated authorization-code output and with OBO output. It does not prove the same token was used twice.
* Device-compliance and location details may explain Conditional Access outcome without explaining token grant mechanics.
* `UniqueTokenIdentifier`, `SessionId`, and `CorrelationId` can aid correlation, but identical session context is not automatically identical access-token bytes.
* The application exports are application objects, not complete service-principal grant state, sign-in logs, backend source, or vendor architecture.

## Smallest discriminating live evidence

One controlled Central sign-in in nonproduction is sufficient if Croesus and Desjardins capture both sides with a shared correlation marker.

### Croesus-side capture

Capture metadata only, not raw secrets or tokens:

1. The exact token endpoint URL and timestamp.
2. `client_id` and redirect URI.
3. `grant_type`.
4. Presence of `code` and `code_verifier`.
5. Presence of `assertion` and `requested_token_use`.
6. Presence and type of client authentication (none, secret, certificate assertion).
7. Requested `scope` or resource.
8. For every access token involved, a one-way SHA-256 hash plus safe projections of `aud`, `iss`, `jti`, `iat`, `azp`/`appid`, `scp`/`roles`, and `cnf` presence.
9. The same projections and hash for the Bearer token actually sent to Microsoft Graph.
10. The Graph request URL, response status, `request-id`, and `client-request-id`.

The decisive outcomes are:

| Observed request | Interpretation |
| --- | --- |
| `grant_type=authorization_code`, with `code` and matching PKCE `code_verifier`, no `assertion`, followed by use of the newly returned Graph token | Supports Olivier. Ordinary server-side authorization-code redemption, not OBO and not replay merely because it originates in AWS. |
| `grant_type=jwt-bearer`, `requested_token_use=on_behalf_of`, inbound access-token `assertion`, confidential-client authentication, downstream Graph scope, and a different output token hash/`jti` | OBO. |
| No new token response, or Graph receives the exact same token hash/`jti` that was issued or presented on a prior leg for another context | Supports token reuse/replay. |
| `grant_type=refresh_token` | Silent renewal, distinct from both initial code redemption and OBO. |
| `grant_type=client_credentials` | App-only call, not delegated user OBO. |

### Desjardins-side capture

Export the corresponding interactive and noninteractive sign-in records with:

* `Id`, `CorrelationId`, `SessionId`, `UniqueTokenIdentifier`
* `CreatedDateTime`, `IsInteractive`, source IP
* application ID/name and resource ID/name
* authentication protocol and client credential type if exposed
* incoming token type
* device ID, trust type, compliance status
* Conditional Access status, policy name, grant/session controls, report-only result
* `TokenProtectionStatusDetails`
* failure code and reason

This capture should be tied to the Croesus timestamp and Graph request ID. It can explain CA outcome and corroborate resource/client identity. It cannot substitute for grant-body and token-hash evidence.

No proxy change, IP allowlist, app-registration redesign, or Token Protection enforcement should be selected as the protocol remedy before this one transaction is captured. Those changes alter policy or topology and could mask the discriminating signal.

## Unresolved questions

* What is the exact sent timestamp of Olivier's latest response?
* Does "OAuth + PKCE" mean authorization code redemption occurs at the Croesus backend, or does a browser SPA redeem and later send a token to Croesus?
* Which process retains the PKCE `code_verifier`, and how is it bound to the authorization request?
* Which of the three exported app IDs appears as `client_id` on the AWS `/token` request?
* Is there a separate Croesus confidential-client or service-principal registration not included in the exports?
* Is the token requested directly for Microsoft Graph `User.Read`, OIDC UserInfo, a Croesus API, or another resource?
* Which token or sign-in session artifact is `Unbound (1008)`, and on which app/resource row?
* What exact CA policy and grant/session control blocks nonproduction, and what AADSTS code is returned?
* Why does production pass if no Croesus production egress IP is trusted? Is device compliance the actual satisfied control, or is a different policy in scope?
* Does Conseiller use the same app registrations, redirect architecture, grant, and AWS service as Central?
* Are the wiki screenshots and live artifacts current, and do they show the real customer rows or only the mock tenant?
* Were the ROPC smoke and live negative scripts ever run successfully? No output is checked into the repository.
* Can the Graph token format in this tenant be safely projected by a trusted component to compare `jti`/hashes, or must token identity be established through MSAL/Entra diagnostic identifiers?

## Recommended next research

* Obtain the single correlated Croesus/Desjardins transaction described above. This is the highest-value next step.
* Retrieve the full raw sign-in records underlying Mathieu's claim, including CA policy evaluation and token-protection details.
* Inspect the two linked wiki pages and archive the relevant screenshots/query output into the evidence package with timestamps.
* Inventory all application and service-principal objects involved by `client_id`, including credentials, delegated grants, and owners, rather than assuming the three SPA exports are exhaustive.
* Ask Croesus to provide a sequence diagram separately for Central and Conseiller.
* Validate the actual redirect and code-redemption topology, especially which component holds the PKCE verifier.
* After the real flow is classified, evaluate the minimum CA remedy. Treat IP trust, cross-tenant device trust, and OBO redesign as separate options with different threat models.
* Correct repository evidence language before using the demo with the customer: remove the categorical replay conclusion, stop claiming leg-2 `jti`/`iat` and certificate thumbprint are logged, label Tier 1 as wrong-audience rejection, and make Tier 2 interpretations status-neutral.
* Add a mock success-path integration test that verifies `MeController` invokes token acquisition and Graph, while clearly labeling it a mock test rather than customer evidence.

## Sources reviewed

Required sources:

* `assets/latest-info/mathieu-santerre.md:1-10`
* `assets/latest-info/email-thread-with-croesus.md:1-5`, `assets/latest-info/email-thread-with-croesus.md:165-187`, `assets/latest-info/email-thread-with-croesus.md:223-236`, `assets/latest-info/email-thread-with-croesus.md:283-310`, and `assets/latest-info/email-thread-with-croesus.md:600-668`
* `assets/latest-info/image.png`
* `README.md:1-70`, `README.md:80-235`
* `docs/configuration-contract.md:18-72`
* `docs/evidence-narrative.md:18-61`
* `docs/obo-demo-guide.md:18-265`
* `api/Program.cs:1-169`
* `api/Controllers/ReplayController.cs:1-191`
* `api/Telemetry/OboClaimLogger.cs:1-123`
* `api/Tests/NegativeControlTests.cs:1-155`
* `api/Tests/ReplayEndpointTests.cs:1-247`
* `spa/src/api.ts:1-302`
* `spa/src/getApiToken.ts:1-30`
* `spa/src/getGraphToken.ts:1-33`
* `spa/src/components/ReplayAttemptPanel.tsx:1-240`

Relevant nearby implementation and evidence sources:

* `api/Controllers/MeController.cs:1-176`
* `api/appsettings.json:1-29`
* `spa/src/authConfig.ts:1-48`
* `spa/src/App.tsx:1-214`
* `assets/dev-dev.txt:1-92`
* `assets/dev-prod.txt:1-91`
* `assets/prod-prod.txt:1-91`
* `assets/app-registration-analysis-findings.md:1-199`
* `assets/croesus-escalation-packet.md:1-65`
* `scripts/provision-app-registrations.sh:1-314`
* `scripts/verify-app-registrations.sh:1-145`
* `scripts/smoke-test.sh:1-135`
* `scripts/negative-test.sh:1-124`
* `scripts/evidence-kql.kusto:1-122`
* `scripts/provision-ca-policy.sh:1-138`
* `infra/main.bicep:1-128`
* `infra/modules/appservice.bicep:1-179`
* `infra/modules/monitoring.bicep:1-80`
* `.github/workflows/deploy-croesus.yml:1-324`