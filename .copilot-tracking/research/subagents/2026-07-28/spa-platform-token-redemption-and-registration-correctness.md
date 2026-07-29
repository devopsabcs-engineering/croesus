<!-- markdownlint-disable-file -->
# SPA-platform token redemption and registration correctness (Croesus / Central GPD)

Status: Complete
Date: 2026-07-28
Scope: Answer six research questions about Microsoft Entra `spa`-platform registrations, cross-origin token redemption, refresh-token lifetimes, confidential vs public clients, OBO enablement, and Token Protection status 1008 — decisively, with Microsoft Learn / RFC citations.

## Evidence base (the exported registrations)

Read from the workspace:

- assets/dev-dev.txt — `sp-CentralGPD-UAT-dev-fed`, appId `713d6ede-38a2-45f4-8982-89ea4fcf1a7f`
- assets/prod-prod.txt — `sp-CentralGPD-prod-fed`, appId `92dd40a3-f7c2-42ff-9303-fff5928e195a`

Both exports (verbatim from the JSON) show:

- `signInAudience: "AzureADMyOrg"` (single tenant).
- `spa.redirectUris` populated; `web.redirectUris: []`; `publicClient.redirectUris: []`.
  - dev: `https://spsfondation.dev.desjardins.com/affwebservices/tools/oidc-tool.html`, `https://pat-gpd-central.certif.desjardins.com/CentralWebApp/LogonSso.aspx`
  - prod: `https://gpd-central.desjardins.com/CentralWebApp/LogonSso.aspx`
- `keyCredentials: []`, `passwordCredentials: []` (no secret, no certificate).
- `api.oauth2PermissionScopes: []`, `appRoles: []`, `api.knownClientApplications: []`, `api.preAuthorizedApplications: []`, `identifierUris: []`.
- `requiredResourceAccess`: Microsoft Graph (`00000003-0000-0000-c000-000000000000`) delegated scope `User.Read` (`e1fe6dd8-ba31-4d61-89e7-88639da4683d`, `type: "Scope"`).
- `web.implicitGrantSettings.enableAccessTokenIssuance: false`; `enableIdTokenIssuance: true` (dev) / `false` (prod).
- `api.requestedAccessTokenVersion: null`.

---

## Q1 — Does a `spa`-platform redirect URI force cross-origin (Origin/CORS) token redemption, and does Entra reject server-side (no-Origin) redemption?

TL;DR verdict: YES. When the redirect URI used to request the token is registered under the `spa` platform node, Microsoft Entra requires the `/oauth2/v2.0/token` redemption to be a cross-origin browser request carrying an `Origin` header. A server-side redemption with no `Origin` header is rejected. The exact error is AADSTS9002327.

Verbatim error text (Microsoft error lookup service):

> Error Code: 9002327
> Message: Tokens issued for the 'Single-Page Application' client-type may only be redeemed via cross-origin requests.

Source: https://login.microsoftonline.com/error?code=9002327

Supporting Microsoft Learn text (OAuth 2.0 authorization code flow reference):

> "Applications can't use a `spa` redirect URI with non-SPA flows, for example, native applications or client credential flows. To ensure security and best practices, the Microsoft identity platform returns an error if you attempt to use a `spa` redirect URI without an `Origin` header. Similarly, the Microsoft identity platform also prevents the use of client credentials in all flows in the presence of an `Origin` header, to ensure that secrets aren't used from within the browser."

> "Single page apps may receive an `invalid_request` error indicating that cross-origin token redemption is permitted only for the 'Single-Page Application' client-type. This indicates that the redirect URI used to request the token has not been marked as a `spa` redirect URI."

Source: https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow

Interpretation: The mechanism is symmetric and enforced at the token endpoint:
- `spa` redirect URI + NO `Origin` header → rejected (AADSTS9002327). This is exactly what a genuine server-to-server call (e.g., an AWS backend calling `login.microsoftonline.com/{tenant}/oauth2/v2.0/token`) looks like — no browser, no `Origin`.
- A confidential (`web`) redemption that presents a client secret/assertion WHILE carrying an `Origin` header is also blocked ("prevents the use of client credentials … in the presence of an `Origin` header").

So the platform node is not cosmetic — it changes token-endpoint behavior. `spa` = must be redeemed from the browser (CORS/`Origin`); `web` = must be redeemed server-side with a credential and without an `Origin` header.

---

## Q2 — SPA-platform token/refresh-token lifetime behaviors vs the `web` (confidential) platform

TL;DR verdict: SPA-platform redirect URIs get a fixed, non-extendable 24-hour refresh-token lifetime; `web`/native refresh tokens have no fixed lifetime (subject to normal inactivity/CA rules). This is a platform-node-driven behavioral difference.

Verbatim Microsoft Learn text (auth code flow reference):

> "Single page apps get a token with a 24-hour lifetime, requiring a new authentication every day."

> "For refresh tokens sent to a redirect URI registered as `spa`, the refresh token expires after 24 hours. Additional refresh tokens acquired using the initial refresh token carries over that expiration time, so apps must be prepared to re-run the authorization code flow using an interactive authentication to get a new refresh token every 24 hours."

> "Refresh tokens for web apps and native apps don't have specified lifetimes."

Source: https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow

Corroborating error-code text (reference-error-codes):

> AADSTS700084 — "The refresh token was issued to a single page app (SPA), and therefore has a fixed, limited lifetime of {time}, which can't be extended. It is now expired and a new sign in request must be sent by the SPA to the sign in page."

> AADSTS700082 — "ExpiredOrRevokedGrantInactiveToken - The refresh token has expired due to inactivity …" (the generic web/native inactivity path — i.e., NOT a fixed 24h cap).

Source: https://learn.microsoft.com/en-us/entra/identity-platform/reference-error-codes

Interpretation: The SPA 24-hour cap is a distinct, observable side effect of the `spa` registration and is tied to browser third-party-cookie privacy behavior (silent iframe renewal vs top-level navigation). A confidential `web` client that holds a refresh token does not inherit this fixed 24h cap. This gives a second, independent behavioral fingerprint (besides AADSTS9002327) for distinguishing the two registration types.

---

## Q3 — Correct platform registration for a SERVER-SIDE (confidential) code redemption

TL;DR verdict: The correct registration is the `web` platform WITH a credential (client secret or, preferably, a certificate). PKCE is still supported and now recommended even for confidential clients. Public clients (which is what `spa` is) MUST NOT present a secret/certificate at the token endpoint.

Microsoft Learn (auth code flow reference), verbatim:

> "Use the auth code flow paired with Proof Key for Code Exchange (PKCE) and OpenID Connect (OIDC) to get access tokens and ID tokens in these types of apps: Single-page web application (SPA); Standard (server-based) web application; Desktop and mobile apps."

> "All confidential clients have a choice of using client secrets or certificate credentials. … For best security, we recommend using certificate credentials. Public clients, which include native applications and single page apps, must not use secrets or certificates when redeeming an authorization code."

> `client_secret` — "required for confidential web apps … Don't use the application secret in a native app or single page app because a `client_secret` can't be reliably stored on devices or web pages. It's required for web apps and web APIs, which can store the `client_secret` securely on the server side."

> `code_challenge` (PKCE) — "This parameter is now recommended for all application types, both public and confidential clients, and required by the Microsoft identity platform for single page apps using the authorization code flow."

Source: https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow

Corroborating error-code text (public client presenting a credential is rejected):

> AADSTS700025 — "InvalidClientPublicClientWithCredential - Client is public so neither 'client_assertion' nor 'client_secret' should be presented."

Source: https://learn.microsoft.com/en-us/entra/identity-platform/reference-error-codes

RFC grounding for public vs confidential and PKCE-for-all:

- RFC 6749 §2.1 (Client Types) defines `confidential` clients ("capable of maintaining the confidentiality of their credentials") vs `public` clients ("incapable of maintaining the confidentiality of their credentials"). A server-side backend that safely holds a secret is a confidential client. Source: https://datatracker.ietf.org/doc/html/rfc6749#section-2.1
- RFC 7636 (PKCE) defines the `code_challenge`/`code_verifier` mechanism used with the authorization code grant. Source: https://datatracker.ietf.org/doc/html/rfc7636
- RFC 9700 (OAuth 2.0 Security Best Current Practice) §2.1.1 states that clients using the authorization code grant SHOULD/ MUST use PKCE (PKCE is recommended for ALL clients, public and confidential, as CSRF/code-injection protection — it is not a substitute for client authentication). Source: https://datatracker.ietf.org/doc/html/rfc9700

Interpretation: "Server-side mandatory `/token` redemption" is, by definition, a confidential-client motion. The canonical Entra registration for it is: `web` platform redirect URI(s) + at least one `passwordCredentials` (secret) or `keyCredentials` (certificate), optionally still sending PKCE `code_verifier`. Certificate credential is preferred over shared secret.

---

## Q4 — Is a `spa`-platform, public, single-tenant, delegated-`User.Read`-only registration CORRECT for the vendor's stated "server-side mandatory `/token` redemption"?

TL;DR verdict: As written, the registration is INTERNALLY INCONSISTENT with a genuine no-Origin server-side redemption. The exports describe a PUBLIC single-page-application client. A real server-side redemption (no `Origin` header, no client credential) against these registrations would fail with AADSTS9002327. The registration is only correct if the code is actually redeemed FROM THE BROWSER (MSAL.js) with an `Origin` header — in which case the vendor's phrase "server-side mandatory `/token` redemption" is imprecise, not the registration.

Concrete correctness problems (provable from the exports alone):

1. Redirect URIs live ONLY under `spa.redirectUris`; `web.redirectUris` is empty. Per Q1, that forces cross-origin (browser, `Origin` header) redemption and rejects no-Origin server calls (AADSTS9002327).
2. No credential exists: `keyCredentials: []` and `passwordCredentials: []`. A confidential server-side redemption normally presents `client_secret`/`client_assertion`; there is none here. (And if the server DID present one against these apps, AADSTS700025 would reject it as a public client — Q3.)
3. `signInAudience: AzureADMyOrg` (single tenant) — consistent with an internal Desjardins app; not itself a defect, but relevant to how the token endpoint/tenant is addressed.
4. Delegated Microsoft Graph `User.Read` only, no exposed API/appRoles — this is a plain sign-in + read-own-profile client, not a resource server and not a middle tier.
5. The redirect targets are server-rendered surfaces: `.aspx` (ASP.NET Web Forms) and `affwebservices/.../oidc-tool.html` (CA SiteMinder / Broadcom SSO). A server-rendered landing page is where a genuine confidential (`web`) redemption would occur — which is exactly what the `spa` node contradicts. Note the SiteMinder URL is literally an `oidc-tool.html` static page, which is browser-loadable and could host MSAL.js.

Two internally consistent interpretations (be objective):

- Interpretation A — Browser redemption (registration is consistent; vendor wording is loose).
  The `.aspx` / `oidc-tool.html` page loads MSAL.js (or equivalent) in the browser, runs auth-code-with-PKCE, and the browser POSTs to `/token` WITH an `Origin` header. The `spa` platform + empty credentials + `User.Read`-only are all correct for this. "Server-side" is then a misnomer for "our web app's page drives it," or refers to a later, separate exchange. The 24-hour SPA refresh-token cap (Q2) would be observable. This interpretation is fully supported by the exported fields.

- Interpretation B — Genuine server redemption (registration is a mismatch).
  A Croesus AWS backend receives the `code` and POSTs to `/token` server-to-server (no `Origin` header). Against a `spa`-only registration this FAILS with AADSTS9002327 (Q1). To make it "work," the backend would have to spoof/inject an `Origin` header to impersonate a browser — a fragile, non-supported workaround that also cannot present a client secret (the app has none, and a public client presenting one is rejected per AADSTS700025). Under this interpretation the registration SHOULD be `web` platform + a certificate/secret credential (Q3), and the current `spa` registration is incorrect.

What is provable vs what needs a captured request:

- Provable from exports: platform is `spa`, client is public (no credential), single tenant, delegated `User.Read` only, redirect targets are server-rendered pages. Therefore a no-Origin server redemption CANNOT succeed against these apps as configured (Q1), and a credentialed server redemption CANNOT succeed either (public client, AADSTS700025).
- NOT provable from exports (requires a captured `/token` request or Entra sign-in log): whether the actual redemption carries an `Origin` header (browser) or not (server), the presence/absence of `code_verifier`, and whether any AADSTS9002327 occurs. That single captured request (or the `Cross-tenant access type` / client app / `Origin` evidence in the sign-in log) is the decisive artifact that separates Interpretation A from B.

Bottom line for Q4: The registration is correct ONLY for a browser-driven SPA redemption (Interpretation A). It is NOT correct for a literal, no-Origin, server-side confidential redemption (Interpretation B) — that would need a `web` platform registration with a credential. The exports are consistent with A and inconsistent with a plain reading of the vendor's "server-side mandatory `/token`" claim under B.

---

## Q5 — Does the registration contain anything that would enable On-Behalf-Of (OBO)?

TL;DR verdict: NO. Nothing in either export enables OBO. There is no exposed API scope, no app role, no client credential, and no confidential middle-tier posture.

Field-level evidence (both dev and prod):

- `api.oauth2PermissionScopes: []` → the app exposes NO API/scope, so no downstream client can obtain a token whose audience is this app (a precondition for OBO's incoming user token).
- `appRoles: []` → no application/app roles.
- `keyCredentials: []` and `passwordCredentials: []` → no secret/certificate. OBO requires the middle tier to authenticate as a confidential client (`urn:ietf:params:oauth:grant-type:jwt-bearer`); with no credential, OBO is impossible.
- `api.knownClientApplications: []`, `api.preAuthorizedApplications: []`, `identifierUris: []` → no middle-tier/known-client wiring, no App ID URI to be an audience.
- `requiredResourceAccess` is delegated Graph `User.Read` only, `type: "Scope"` → a plain delegated sign-in client, not a resource server.

Conclusion: This is a leaf public client that signs a user in and reads their own profile. OBO is not just absent — it is structurally impossible with this registration. This is consistent with the vendor's stated "NO OBO."

---

## Q6 — Token Protection status 1008 ("Unbound"): what it means and what it does not prove

TL;DR verdict: CONFIRMED. `signInSessionStatusCode` 1008 means the request is unbound because the client isn't integrated with the platform broker (WAM). It is a device/session-binding status, NOT proof of access-token replay. Token Protection supports native apps only (not browser/SPA), and protects EXO/SPO/Teams (plus AVD/Windows 365 on Windows) — NOT Microsoft Graph.

Verbatim Microsoft Learn text (Token Protection deployment guide — Windows), status codes for an Unbound request:

> "Unbound: the request wasn't using bound protocols. Possible `statusCodes` when request is unbound are:
> 1002: The request is unbound due to the lack of Microsoft Entra ID device state.
> 1003: The request is unbound because the Microsoft Entra ID device state doesn't satisfy Conditional Access policy requirements for token protection. …
> 1005: The request is unbound for other unspecified reasons.
> 1006: The request is unbound because the OS version is unsupported.
> 1008: The request is unbound because the client isn't integrated with the platform broker, such as Windows Account Manager (WAM)."

Source: https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows

Native-apps-only + supported-resources (concept page), verbatim:

> "Token Protection currently supports native applications only. Browser-based applications are not supported."

> Supported resources: "Exchange Online; SharePoint Online; Microsoft Teams. On Windows, enforcement is also supported for: Azure Virtual Desktop; Windows 365."

Source: https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection

Corroboration from the deployment guide (application list is all native desktop clients: Outlook, Teams, OneDrive, Word/Excel/PowerPoint, Edge profile sign-in, etc.), and: "Token Protection currently supports native applications only. Browser-based applications are not supported."

Source: https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows

Interpretation (confirms the prior finding):
- 1008 is a binding-state classification of a sign-in request under a Token Protection CA policy, describing that the client did not obtain a device-bound token via WAM. It describes token BINDING absence, not evidence that a token was stolen and replayed.
- Because Token Protection is native-apps-only and does not cover Microsoft Graph, a browser/SPA client calling Microsoft Graph (`User.Read`) would not be in scope for Token Protection binding at all — so an "Unbound / 1008" status against such a flow is expected/benign, not an indicator of compromise.

---

## Registration correctness verdict (summary)

| Aspect | Exported registration | Correct for genuine server-side (no-Origin) redemption? |
| --- | --- | --- |
| Platform node | `spa` only (`web`/`publicClient` empty) | NO — should be `web` |
| Client type | Public (no `keyCredentials`/`passwordCredentials`) | NO — confidential needs a secret/cert |
| Token endpoint behavior | Requires `Origin`/CORS; rejects no-Origin with AADSTS9002327 | NO — server call has no `Origin` |
| Refresh-token lifetime | Fixed 24h (SPA cap) | Atypical for a server/daemon posture |
| Exposed API / app roles | None | N/A (not a resource server) |
| Graph access | Delegated `User.Read` only | Consistent with sign-in only |
| OBO enablement | None possible | N/A (vendor says no OBO) |
| Single sign-in audience | `AzureADMyOrg` | Fine (internal app) |

Verdict: The registration is a correct, coherent PUBLIC single-page-application client. It is NOT a correct registration for a literal server-side (no-Origin) confidential `/token` redemption. Either (A) the redemption really happens in the browser with an `Origin` header (registration correct, vendor wording imprecise), or (B) a real backend redeems it and the `spa` registration is a mismatch that would fail AADSTS9002327 absent an `Origin`-header spoof. The exports are consistent with A and cannot, by themselves, prove B.

---

## How the mock app should model this (falsifiable assertions)

To let a test prove/falsify SPA-vs-web redemption behavior, model both registrations and assert on the token-endpoint contract:

1. SPA-node negative test (no-Origin redemption fails): Configure an Entra app with a `spa` redirect URI only, no credential. From a server context (curl/HttpClient, NO `Origin` header), POST a valid `authorization_code` + `code_verifier` to `/{tenant}/oauth2/v2.0/token`. Assert: HTTP 400 with `error=invalid_grant`/`invalid_request` and `error_description` containing `AADSTS9002327` and the phrase "Tokens issued for the 'Single-Page Application' client-type may only be redeemed via cross-origin requests." (Proves the exported registration cannot service a genuine server-side redemption.)

2. SPA-node positive test (browser/Origin redemption succeeds): Same app, but send the `/token` POST WITH an `Origin` header matching the `spa` redirect origin (as a browser fetch would). Assert: 200 + `access_token` (+ `refresh_token` only if `offline_access` requested). This is Interpretation A. If the vendor's real flow passes THIS assertion, "server-side mandatory /token" is imprecise wording.

3. Web-node control (confidential redemption succeeds server-side): Configure a separate app with a `web` redirect URI + a certificate (`keyCredentials`) or secret. From a server (no `Origin`), POST `code` + `code_verifier` + `client_assertion`/`client_secret`. Assert: 200 + tokens. This is the CORRECT shape for Interpretation B and the recommended fix if a real backend must redeem.

4. Public-client-with-credential negative control: Against the `spa`/public app, present a `client_secret`. Assert: HTTP 400 with `AADSTS700025` ("Client is public so neither 'client_assertion' nor 'client_secret' should be presented."). Proves the exported app cannot be credentialed as-is.

5. Refresh-token lifetime fingerprint: For the `spa` app, capture the issued refresh token and assert an ~24h non-extendable lifetime (or assert that a renewal past 24h yields `AADSTS700084`). For the `web`/native control, assert no fixed 24h cap. Distinguishes the two platform nodes behaviorally.

6. OBO impossibility assertion: Assert `api.oauth2PermissionScopes == []` and `keyCredentials == [] && passwordCredentials == []` for the exported apps → a static check that no OBO middle-tier token exchange (`grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`) can target or be issued by these apps.

7. Token Protection non-signal assertion (documentation-level, not a live call): Assert in test docs that a browser/SPA→Graph flow is out of Token Protection scope (native-apps-only; resources EXO/SPO/Teams, not Graph), so an observed `signInSessionStatusCode: 1008` is expected and is not evidence of access-token replay.

The single most decisive real-world artifact (beyond the exports) is a captured `/token` request or the Entra sign-in log entry showing whether the redemption carried an `Origin` header. That one artifact resolves Interpretation A vs B.

---

## References

- Microsoft identity platform and OAuth 2.0 authorization code flow — https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow
- Microsoft Entra authentication & authorization error codes (AADSTS9002327, 700084, 700082, 700025) — https://learn.microsoft.com/en-us/entra/identity-platform/reference-error-codes
- Entra error lookup, AADSTS9002327 verbatim — https://login.microsoftonline.com/error?code=9002327
- How Token Protection enhances Conditional Access (native-apps-only; EXO/SPO/Teams) — https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection
- Token Protection deployment guide — Windows (status 1008 = not integrated with platform broker/WAM) — https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows
- RFC 6749 §2.1 Client Types (public vs confidential) — https://datatracker.ietf.org/doc/html/rfc6749#section-2.1
- RFC 7636 Proof Key for Code Exchange (PKCE) — https://datatracker.ietf.org/doc/html/rfc7636
- RFC 9700 OAuth 2.0 Security Best Current Practice (PKCE for all clients) — https://datatracker.ietf.org/doc/html/rfc9700

## Clarifying questions (need input / a captured artifact)

1. Can the vendor provide ONE captured `/oauth2/v2.0/token` request (headers included, secrets redacted) or the corresponding Entra sign-in log entry? Presence/absence of the `Origin` header is the single fact that resolves Interpretation A vs B in Q4.
2. Is the AWS backend actually calling `/token`, or is it receiving an already-issued access token from the browser and merely validating/forwarding it? "Server-side mandatory /token redemption" could describe either; they have very different registration implications.
3. Is there a SECOND app registration (not in these exports) — e.g., a confidential `web` app or a resource/API app — that the backend uses? The `spa` apps here cannot be the confidential redeemer.
