<!-- markdownlint-disable-file -->
# Research: Entra app registration shape for a BFF, sign-in logs, Conditional Access, token binding, and "token replay"

**Date:** 2026-09-22
**Status:** Complete (with open items listed at the end)
**Workspace:** c:\src\GitHub\devopsabcs-engineering\croesus
**Research mode:** RESEARCH ONLY — no files modified outside `.copilot-tracking/research/`

---

## 0. Research topics and questions

1. Correct Entra app registration shape for a Backend-for-Frontend (BFF).
2. Entra sign-in logs: what gets logged per leg, which schema fields matter, table names, ready-to-run KQL.
3. Conditional Access behaviour: public client vs confidential client, the OBO leg, CAE, Token Protection.
4. "Token replay" — precise definitions and the detections that map to each.
5. How to set up the non-prod tenant demo (app registration, CA policy, diagnostic settings, KQL).
6. In-app evidence surface — what a sample app can safely display.

---

## 1. Repo context — the exact recorded wording

### 1.1 The original "replay" + "unbound" claim (the wording that started the engagement)

**File:** `assets/latest-info/email-thread-with-croesus.md`
**Line:** 625
**Speaker:** Microsoft, in the outreach email to the Croesus engineering team

> "Our investigation of the Entra sign-in logs indicates that this second hop presents a replayed user token to Microsoft Graph rather than performing a standards-compliant OAuth 2.0 On-Behalf-Of (OBO) token exchange. The sign-in is recorded with a Token Protection sign-in-session status of Unbound (statusCode 1008), meaning the token is not bound to the originating device or platform."

### 1.2 The Desjardins-side statement of the same observation (French, original)

**File:** `assets/latest-info/email-thread-with-croesus.md`
**Line:** 231
**Speaker:** Mathieu Santerre (Desjardins)

> "…votre serveur Croesus ne se contente pas de valider les tokens pour octroyer l'accès au client, il les réutilise pour s'authentifier lui aussi à notre Tenant Azure, sous l'identité du client (Token Protection sign-in-session status of Unbound (statusCode 1008))."

### 1.3 The vendor's response (they do not recognise the replay characterisation)

**File:** `assets/latest-info/email-thread-with-croesus.md`
**Line:** 4
**Speaker:** Olivier Leblanc (Croesus)

> "Un appel côté serveur vers le /token de Azure est donc obligatoire est va sortir depuis les IPs publiques Croesus vers Azure. Je ne sais pas pourquoi cet appel est donc identifié comme une tentative de replay."

### 1.4 The corrected, current position held in this repo

**File:** `assets/croesus-escalation-packet.md`
**Line:** 60

> "The blocked event shows Token Protection status "unbound" (code 1008). That status means the client is **not integrated with the platform broker** (Windows Account Manager) — a device- and session-binding signal — and does **not** by itself prove that an access token was replayed, nor does it identify the OAuth grant. The grant remains unclassified from a captured request, and we are not asserting one."

**File:** `assets/croesus-3way-session-findings.md`
**Line:** 239

> "Do **not** treat `Unbound (1008)` as replay evidence. It is a device and session binding status, it does not identify a grant, and Token Protection does not cover Microsoft Graph."

**File:** `docs/evidence-narrative.md`
**Line:** 47

> "Token Protection token binding, and therefore the 1008 "unbound" signal, applies to native-application clients reaching specific resources: Exchange Online, SharePoint Online, and Teams. It does not fire for a browser-based SPA calling a custom API that then calls Microsoft Graph."

**File:** `assets/app-registration-analysis-findings.md`
**Line:** 141 (the raw sign-in log evidence table row)

> `| Token Protection status | **bound (code 0)** | **unbound (code 1008)** |`

### 1.5 Other repo anchors used in this research

* `assets/app-registration-analysis-findings.md` lines 27, 29, 104, 126 — the original (now superseded) categorical "token replay" framing, and the OBO-is-structurally-impossible finding (no secret, no certificate, no exposed API scope on any of the three exports).
* `assets/croesus-3way-session-findings.md` — findings F1–F11, consequences A1–A10, routes R1–R9, and Experiments A and B.
* `assets/dev-dev.txt`, `assets/dev-prod.txt`, `assets/prod-prod.txt` — the three app-registration exports (SPA platform only, `AzureADMyOrg`, Graph `User.Read` delegated only, no credentials, no exposed API).
* `scripts/evidence-kql.kusto` — the five existing KQL queries, including the honest caveat that sign-in logs "do NOT and cannot prove that identical bearer-token bytes were reused across requests".
* `scripts/provision-app-registrations.sh` — the working reference for `az ad app create`, `identifier-uris`, `oauth2PermissionScopes`, `knownClientApplications`, `preAuthorizedApplications`, Key Vault certificate credential, and admin consent.

**Important correction this research surfaces (see §3.4 / §4.4):** the statement that Token Protection "applies to native applications only" is now **out of date**. As of the current Learn docs, Token Protection is in **Preview for browser-based web applications** on Windows and macOS, scoped to Azure Resource Manager. It still does not cover Microsoft Graph, so the repo's core conclusion (1008 is not replay evidence for this flow) still holds — but the "native-only" phrasing should be softened.

---

## 2. Q1 — Correct Entra app registration shape for a BFF

### 2.1 The governing principle

A BFF is a **confidential client**. Entra classifies a client by *who redeems the authorization code and holds the credential*, not by the rendering model of the UI. Microsoft states the SPA case explicitly in the OBO doc:

> "In the case of Single-page apps (SPAs), they should pass an access token to a middle-tier confidential client to perform OBO flows instead."
> — [Microsoft identity platform and OAuth2.0 On-Behalf-Of flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)

The same doc carries the strongest Microsoft statement against the "relay the token back to the browser" pattern, and it names exactly the two properties the Croesus escalation is about:

> "**DO NOT** send access tokens that were issued to the middle tier to anywhere except the intended audience for the token. … Security risks of relaying access tokens from a middle-tier resource to a client … include: Increased risk of token interception over compromised SSL/TLS channels. **Inability to satisfy token binding and Conditional Access scenarios requiring claim step-up** (for example, MFA, Sign-in Frequency). **Incompatibility with admin-configured device-based policies** (for example, MDM, location-based policies)."
> — [OBO flow — Middle-tier access token request](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)

### 2.2 Platform configuration — `web`, not `spa`, not public client

| Setting | Correct value for a BFF | Source |
| --- | --- | --- |
| Platform / redirect URI node | `web` (`replyUrlsWithType[].type == "Web"`) | [App manifest — replyUrlsWithType](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest) |
| Redirect URI | `https://<host>/signin-oidc` | ASP.NET Core OIDC convention; registered under `web.redirectUris` |
| Front-channel logout URL | `https://<host>/signout-oidc` | `web.logoutUrl` in the Graph application resource; `logoutUrl` in the legacy manifest |
| Post-logout redirect | `https://<host>/signout-callback-oidc` | Registered as an additional `web.redirectUris` entry in ASP.NET Core |
| `allowPublicClient` | `false` | [App manifest — allowPublicClient](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest) |
| `isFallbackPublicClient` (Graph name for the same property) | `null` or `false` | Same; `publicClient` → `allowPublicClient` rename table in the same doc |
| `oauth2AllowImplicitFlow` (Graph: `web.implicitGrantSettings.enableAccessTokenIssuance`) | `false` | [App manifest — oauth2AllowImplicitFlow](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest) |
| `oauth2AllowIdTokenImplicitFlow` (Graph: `web.implicitGrantSettings.enableIdTokenIssuance`) | `false` | [App manifest — oauth2AllowIdTokenImplicitFlow](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest) |
| `signInAudience` | `AzureADMyOrg` for a single-tenant demo | [App manifest — signInAudience](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest) |

**Why `allowPublicClient` must be `false`.** Learn:

> "Specifies the fallback application type. Microsoft Entra ID infers the application type from the replyUrlsWithType by default. There are certain scenarios where Microsoft Entra ID can't determine the client app type. … If this value is set to true the fallback application type is set as public client, such as an installed app running on a mobile device. **The default value is false, which means the fallback application type is confidential client such as web app.**"
> — [reference-app-manifest](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest)

A public-client fallback lets Entra accept a credential-less token request against this `client_id`. That is exactly the surface a BFF must not expose. Note that `scripts/provision-app-registrations.sh` deliberately sets `"isFallbackPublicClient": true` on the **SPA** registration — correct for that registration, wrong for a BFF.

**Why implicit must be off.** Learn is explicit and repeats it for both flags:

> "We, however, discourage the use of implicit grant even in SPAs and recommend using the [authorization code flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow) with PKCE."
> — [reference-app-manifest](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest)

This maps directly to finding **V3** in `assets/app-registration-analysis-findings.md` (dev-dev has implicit ID-token issuance ON).

### 2.3 Credentials — secret vs certificate vs Federated Identity Credential

Entra accepts three client-authentication forms on the `/token` POST for a confidential client:

1. **Client secret** — `client_secret` form field. The OBO doc's "First case: Access token request with a shared secret" shows the exact shape. Cheapest to prove; the weakest to operate.
2. **Certificate** — `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer` plus a signed `client_assertion`. The OBO doc's "Second case: Access token request with a certificate".
   — Both cases: [v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
3. **Federated Identity Credential (Workload Identity Federation)** — no stored credential at all.

**Recommended for an App Service-hosted BFF: Managed Identity + Federated Identity Credential.** Learn names this exact scenario as supported:

> "Workloads running on Azure compute platforms using app identities. First assign a user-assigned managed identity to your Azure VM or App Service. Then, [configure a trust relationship between your app and the user-assigned identity]."
> — [Workload Identity Federation — Supported scenarios](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation)

Why it is the recommendation:

> "These credentials pose a security risk and have to be stored securely and rotated regularly. You also run the risk of service downtime if the credentials expire. … You eliminate the maintenance burden of manually managing credentials and eliminates the risk of leaking secrets or having certificates expire."
> — [Workload Identity Federation](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation)

Two constraints to record:

* "Microsoft Entra ID issued tokens may not be used for federated identity flows. The federated identity credentials flow does not support tokens issued by Microsoft Entra ID." — this is why the *app-registration-trusts-a-user-assigned-MI* variant is the supported App Service path, rather than a raw Entra token exchange.
* "The Federated Identity Credential `issuer`, `subject`, and `audience` values must case-sensitively match the corresponding `issuer`, `subject` and `audience` values contained in the token being sent to Microsoft Entra ID by the external IdP."

**Relevance to Croesus.** Route R5 in `assets/croesus-3way-session-findings.md` already concluded that AWS-hosted IIS on .NET Framework 4.5.2 cannot use WIF without introducing a supported external OIDC workload-token source. That conclusion is still correct for *their* hosting. For **our demo BFF on App Service**, MI + FIC is the right shape and is worth demonstrating as the destination on the credential ladder.

### 2.4 `requestedAccessTokenVersion` (v1 vs v2)

> "Specifies the access token version expected by the resource. This parameter changes the version and format of the JWT produced independent of the endpoint or client used to request the access token. The endpoint used, v1.0 or v2.0, is chosen by the client and only impacts the version of id_tokens. **Resources need to explicitly configure `requestedAccessTokenVersion` to indicate the supported access token format.** Possible values … are 1, 2, or null. If the value is null, this parameter defaults to 1."
> — [reference-app-manifest — requestedAccessTokenVersion](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest)

Why it matters for the evidence surface:

* **v1 tokens** carry `appid` and `appidacr`; **v2 tokens** carry `azp` and `azpacr` instead. `acr` is v1-only. ([access-token-claims-reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference))
* **v1 `aud` is not stable** — "in v1 access tokens it can be emitted in various ways - any appID URI, with or without a trailing slash, and the client ID of the resource. This randomization can be hard to code against when performing token validation." v2 `aud` "is always the client ID of the API". ([optional-claims-reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference), [access-token-claims-reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference))

Because the headline proof in `docs/evidence-narrative.md` is **audience binding**, set `requestedAccessTokenVersion: 2` on the API so `aud` is deterministically the API's client ID. If you must stay on v1, use the `aud` optional claim with `additionalProperties: ["use_guid"]`.

Also note: the OBO doc warns that the *downstream* token's format is chosen by the downstream resource, not by you — "This access token is a v1.0-formatted token for Microsoft Graph. This is because the token format is based on the **resource** being accessed."

### 2.5 API permissions, exposed scope, `knownClientApplications`, `preAuthorizedApplications`

**Delegated vs application.** For a BFF calling a downstream API *as the user*, the permission must be **delegated**. OBO is delegated-only:

> "It only uses delegated *scopes* and not application *roles*. *Roles* remain attached to the principal (the user) and never to the application operating on the user's behalf."
> — [v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)

**Exposed scope on the API.** `api://<appid>/<scope>` — e.g. `api://<API_CLIENT_ID>/access_as_user`. Learn recommends the `api://<appId>` identifier-URI form. ([reference-app-manifest — identifierUris](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest))

**`knownClientApplications`.**

> "Used for bundling consent if you have a solution that contains two parts: a client app and a custom web API app. If you enter the appID of the client app into this value, the user will only have to consent once to the client app. Microsoft Entra ID will know that consenting to the client means implicitly consenting to the web API. … Both the client and the web API app must be registered in the same tenant."
> — [reference-app-manifest — knownClientApplications](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest)

**`preAuthorizedApplications`.**

> "Resources can indicate that a given application always has permission to receive certain scopes. This is useful to make connections between a front-end client and a back-end resource more seamless. … Any such application can request these permissions in an OBO flow and receive them without the user providing consent."
> — [v2-oauth2-on-behalf-of-flow — Preauthorized applications](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)

Manifest form (note the legacy key is `permissionIds`, the Graph v1.0 key is `delegatedPermissionIds` — `scripts/provision-app-registrations.sh` uses the Graph form correctly):

```json
"preAuthorizedApplications": [
  { "appId": "<SPA_OR_BFF_CLIENT_ID>", "permissionIds": ["<SCOPE_GUID>"] }
]
```

**Ordering gotcha already learned in this repo:** Graph validates `preAuthorizedApplications.delegatedPermissionIds` against scopes that already exist on the app, so the scope must be PATCHed and committed **before** the `preAuthorizedApplications` PATCH. See the comment in `scripts/provision-app-registrations.sh` section 3.

**`.default` and combined consent caution:**

> "While it's valid to use `scope=openid https://resource/.default` in combined consent flows involving known client applications, you must **not** combine `.default` with other delegated scopes like `User.Read`, `Mail.Read`, `profile`, or `User.ReadWrite.All` in the same request. This will result in `AADSTS70011` errors."
> — [v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)

### 2.6 Separate registration for the API vs a single combined registration

Microsoft documents both and does not mandate two:

> "**Use of a single application.** In some scenarios, you could only have a single pairing of middle-tier and front-end client. In this scenario, you could find it easier to make this a single application, negating the need for a middle-tier application altogether. To authenticate between the front-end and the web API, you can use cookies, an id_token, or an access token requested for the application itself."
> — [v2-oauth2-on-behalf-of-flow — Use of a single application](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)

Recommendation for this engagement: **two registrations** (client + API), because the entire evidentiary argument in `docs/evidence-narrative.md` rests on two *distinct audiences*. A single combined registration collapses `aud` and removes the headline proof. The existing demo already does this correctly.

### 2.7 OBO requirements on the middle tier — the hard constraints

From [v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow):

* **Confidential client.** Must send `client_secret` or `client_assertion`.
* **Audience match.** "This token must have an audience (`aud`) claim of the app making this OBO request (the app denoted by the `client-id` field). **Applications can't redeem a token for a different app** (for example, if a client sends an API a token meant for Microsoft Graph, the API can't redeem it using OBO. It should instead reject the token)." — This *is* the audience boundary the demo proves.
* **`grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`** and **`requested_token_use=on_behalf_of`**.
* **No app-only tokens.** "the OBO flow only works for user principals."
* **No custom signing keys.** "applications with custom signing keys can't be used as middle-tier APIs in the OBO flow. This includes enterprise applications configured for single sign-on." — Worth flagging: all three Croesus exports set `acceptMappedClaims: true` (per `assets/app-registration-analysis-findings.md` §1), which is the claims-mapping-without-custom-signing-key path; it is not the same thing as a custom signing key, but it is adjacent and should be checked before asserting OBO feasibility.
* **No wildcard reply URLs** if an id_token from implicit is used for OBO.

### 2.8 `groupMembershipClaims`, optional claims, `tokenEncryptionKeyId`

**`groupMembershipClaims`** — valid values `"None"`, `"SecurityGroup"`, `"ApplicationGroup"`, `"DirectoryRole"`, `"All"`. ([reference-app-manifest](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest)) For a BFF demo, prefer `"None"` or `"ApplicationGroup"` — `"All"` risks the groups-overage claim (150 for SAML, 200 for JWT) which forces a Graph call and muddies the evidence. ([access-token-claims-reference — Groups overage claim](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference))

**Optional claims worth turning on for the evidence surface** ([optional-claims-reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference)):

| Claim | What it proves | Note |
| --- | --- | --- |
| `idtyp` | app-only vs app+user token | "The value is `app` when the token is an app-only token. This claim is the most accurate way for an API to determine if a token is an app token or an app+user token." By default emitted **only for app-only tokens** — add `additionalProperties: ["include_user_token"]` to get it on user tokens too. |
| `xms_cc` | client declared CAE capability | "A value of `cp1` in the access token is the authoritative way to identify that a client application is capable of handling a claims challenge." Presence is controlled by the **resource**, not the client. |
| `acct` | member vs guest | `0` = member of the tenant, `1` = guest. Directly relevant to route R6 (B2B guest re-shaping). |
| `auth_time` | when the user last authenticated | Useful for sign-in-frequency / step-up narratives. |
| `acrs` | Auth Context IDs the bearer may exercise | Pairs with `xms_cc` for step-up demos. |

**`tokenEncryptionKeyId`.** Not present in the legacy `reference-app-manifest` attribute list retrieved for this research. It is a property on the Graph `application` resource pointing at a `keyCredentials` entry with `usage: "Encrypt"`, causing Entra to issue the SAML token encrypted to that key. It is **SAML-token encryption**, not JWT access-token encryption, and is therefore **not relevant to an OIDC/OAuth BFF**. *(Open item O3 — verify against the Graph `application` resource page rather than the legacy manifest page.)*

### 2.9 Consolidated target shape

See §6.1 for the runnable `az` / Graph bodies.

---

## 3. Q2 — Entra sign-in logs: what gets logged, per leg

### 3.1 The four log types

> "There are four types of logs in the sign-in logs preview: Interactive user sign-ins, Non-interactive user sign-ins, Service principal sign-ins, Managed identity sign-ins."
> — [Sign-in logs in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-ins)

The `signInEventTypes` enum values are `interactiveUser`, `nonInteractiveUser`, `servicePrincipal`, `managedIdentity`. ([concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details))

### 3.2 Which leg of a BFF flow lands where

| Leg | Log type | Azure Monitor table | Basis |
| --- | --- | --- | --- |
| User's interactive auth-code login (`/authorize`, credential entry, MFA) | Interactive user sign-in | `SigninLogs` | "Sign-in logs are generated when users provide their username and password on a Microsoft Entra sign-in screen or when they pass an MFA challenge." ([concept-diagnostic-settings-logs-options](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-diagnostic-settings-logs-options)) |
| BFF's `/token` redemption of the authorization code | **Non-interactive** user sign-in | `AADNonInteractiveUserSignInLogs` | "A client uses an OAuth 2.0 authorization code to get an access token and refresh token." ([concept-noninteractive-sign-ins](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-noninteractive-sign-ins)) |
| OBO exchange (BFF → downstream API/Graph) | **Non-interactive** user sign-in | `AADNonInteractiveUserSignInLogs` | Delegated, on behalf of a user, no auth factor supplied. Confirmed empirically by the existing Query 2 in `scripts/evidence-kql.kusto`. |
| Refresh-token renewal | **Non-interactive** user sign-in | `AADNonInteractiveUserSignInLogs` | "A client app uses an OAuth 2.0 refresh token to get an access token." ([concept-noninteractive-sign-ins](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-noninteractive-sign-ins)) |
| App-only / client-credentials call (no user) | Service principal sign-in | `AADServicePrincipalSignInLogs` | "certificates or client secrets are used for authentication" ([concept-diagnostic-settings-logs-options](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-diagnostic-settings-logs-options)) |
| Azure MI acquiring a token (e.g. the BFF's own MI, or the FIC exchange) | Managed identity sign-in | `AADManagedIdentitySignInLogs` | "similar insights as the service principal sign-in logs, but for managed identities, where Azure manages the secrets" ([concept-diagnostic-settings-logs-options](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-diagnostic-settings-logs-options)) |

### 3.3 The single most consequential detail for the Croesus case

> "**The IP address of non-interactive sign-ins performed by [confidential clients](https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-applications) doesn't match the actual source IP of where the refresh token request is coming from. Instead, it shows the original IP used for the original token issuance.**"
> — [Non-interactive sign-in logs — Special considerations](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-noninteractive-sign-ins)

This is a direct, citable answer to the whole Desjardins/Croesus dispute:

* Today, with a **public-client (`spa`) registration**, the second leg surfaces the **AWS egress IP** (`3.97.32.113`, `3.99.119.124` — per `assets/app-registration-analysis-findings.md` §4) and fails IP/device-shaped Conditional Access in the Dev tenant.
* If the backend authenticates as a **confidential client**, Entra records **the original sign-in IP** on that non-interactive leg, not the AWS egress. The "vendor's cloud IP appears in our tenant's sign-in log" symptom that drives routes R1/R3 largely disappears as a *logging* artefact.
* Caveat to state honestly: this is a **logging behaviour**, not a network-path change. It does not itself satisfy a compliant-device grant (the leg still has no device context), and it must not be presented as a security control. It does, however, remove the misleading evidence that is currently driving an IP-allowlist conversation.

### 3.4 Schema fields that matter for the proof

All from [Azure Monitor Logs reference — SigninLogs](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/signinlogs) unless noted.

| Field (Azure Monitor) | Graph name | Meaning / why it matters here |
| --- | --- | --- |
| `CorrelationId` | `correlationId` | "The identifier that's sent from the client when sign-in is initiated." Groups legs of one sign-in session. **Caveat from Learn:** "The value is based on parameters passed by a client, so Microsoft Entra ID can't guarantee its accuracy" ([concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details)) — and "This value's presence in multiple logs doesn't indicate the ability to join logs across services" ([reference-azure-monitor-sign-ins-log-schema](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/reference-azure-monitor-sign-ins-log-schema)). This is exactly why `scripts/evidence-kql.kusto` Query 5 labels the join BEST-EFFORT and also pins `UserPrincipalName`. |
| `OriginalRequestId` | `originalRequestId` | "The request identifier of the first request in the authentication sequence." The most reliable way to tie a multi-request authentication together. |
| `UniqueTokenIdentifier` | `uniqueTokenIdentifier` | "A unique base64 encoded request identifier used to track tokens issued by Azure AD as they are redeemed at resource providers." **This is the field that links a sign-in row to a specific issued token.** See §7.4 for the `uti` relationship. |
| `IncomingTokenType` | `incomingTokenType` | "The type of token utilized to signIn (examples: primary refresh token, saml assertion)." Distinguishes a PRT-backed SSO from a refresh-token grant from a code redemption. |
| `TokenIssuerType` | `tokenIssuerType` | `AzureAD`, `ADFederationServices`, `AzureADBackupAuth`, `ADFederationServicesMFAAdapter`, `NPSExtension`. Pairs with the **Token issuer anomaly** risk detection. |
| `AuthenticationRequirement` | `authenticationRequirement` | "the highest level of authentication needed through all the sign-in steps" — `singleFactorAuthentication` / `multiFactorAuthentication`. |
| `AuthenticationProtocol` | `authenticationProtocol` | "none, oAuth2, ropc, wsFederation, saml20, deviceCode". |
| `ClientCredentialType` | `clientCredentialType` | "The type of client credential used. Examples include client assertion, client secret, etc." **This is the field that proves the BFF authenticated as a confidential client** — the single most direct log-side answer to escalation question Q7. |
| `AppliedConditionalAccessPolicies` / `ConditionalAccessPolicies` | `appliedConditionalAccessPolicies` | "A separate entry is created for each policy." Note the naming trap: "The section is called *applied* Conditional Access policies; however, policies that were *not* applied also appear in this section." ([reference-azure-monitor-sign-ins-log-schema](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/reference-azure-monitor-sign-ins-log-schema)) |
| `ConditionalAccessStatus` | `conditionalAccessStatus` | `success` / `failure` / `notApplied`. Semantics matter: "Even though a Conditional Access policy might not apply, if it was evaluated, the Conditional Access status shows *Success*." ([concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details)) |
| `ResourceDisplayName` / `ResourceIdentity` / `ResourceServicePrincipalId` | — | The audience of the leg. **This is the headline proof field** for `docs/evidence-narrative.md`. Filter on immutable `ResourceIdentity`, not display name. |
| `CrossTenantAccessType` | `crossTenantAccessType` | `none`, `b2bCollaboration`, `b2bDirectConnect`, `microsoftSupport`, `serviceProvider`, `passthrough`. Directly relevant to route R6. |
| `OriginalTransferMethod` | `originalTransferMethod` | "Transfer method used to initiate a session throughout all subsequent requests." |
| `FederatedCredentialId` | `federatedCredentialId` | Populated when a Federated Identity Credential was used — the log-side proof that MI+FIC worked. |
| `ServicePrincipalId` / `ServicePrincipalName` | — | "populated when you are signing in using an application." |
| `AppId` / `AppDisplayName` | `appId` | The client. Always filter on `AppId`. |
| `SessionId` / `ClientSessionId` | `sessionId` | "Id of the session that was generated during the signIn." Both legs in `assets/app-registration-analysis-findings.md` §4 shared SessionId `007bc799-…`. |
| `DeviceDetail` | `deviceDetail` | `deviceId`, `OS`, `browser`, compliance, trust type. Empty on a genuine server-side leg. |
| `IsInteractive` | `isInteractive` | `true` = user supplied a factor. |
| `SignInIdentifier` / `SignInIdentifierType` | `signInIdentifier` | "The identification that the user provided to sign in." Types: `userPrincipalName`, `phoneNumber`, `proxyAddress`, `qrCode`, `onPremisesUserPrincipalName`. |
| `TokenProtectionStatusDetails` | `tokenProtectionStatusDetails` | `{ "signInSessionStatus": "bound|unbound", "signInSessionStatusCode": <code> }` — see §4.4. |
| `RiskEventTypes_V2`, `RiskLevelDuringSignIn`, `RiskState`, `RiskDetail` | — | ID Protection signals; see §5.5. |
| `IPAddressFromResourceProvider` | — | "The IP address a user used to reach a resource provider … This value is often null." Relevant to the CAE split-path problem. |
| `TimeGenerated` vs `CreatedDateTime` | — | `TimeGenerated` = ingestion time into Log Analytics. `CreatedDateTime` = when Entra processed the authentication. "The difference … is caused by the time it takes for the sign-in event to be processed and sent to Log Analytics." ([concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details)) **Use `CreatedDateTime` for ordering legs; use `TimeGenerated` for the query time filter.** |

### 3.5 Log Analytics table names

`SigninLogs` · `AADNonInteractiveUserSignInLogs` · `AADServicePrincipalSignInLogs` · `AADManagedIdentitySignInLogs`

Diagnostic-setting category names (what you pass to `--logs`): `SignInLogs`, `NonInteractiveUserSignInLogs`, `ServicePrincipalSignInLogs`, `ManagedIdentitySignInLogs` — plus `AuditLogs`, `RiskyUsers`, `UserRiskEvents` for the risk story. ([concept-diagnostic-settings-logs-options](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-diagnostic-settings-logs-options), [id-protection-guide-analyze](https://learn.microsoft.com/en-us/entra/architecture/id-protection-guide-analyze))

### 3.6 KQL — correlate all legs of one BFF sign-in

Run each block on its own; Log Analytics executes one query at a time.

**Query A — every leg of one sign-in, all four tables, unioned and ordered.**

```kusto
// All legs of one BFF sign-in, correlated by CorrelationId.
// Substitute <CORRELATION_ID>. Widen ago() if legs fall outside the window.
let window = 1d;
let corr   = "<CORRELATION_ID>";
let interactiveLeg =
    SigninLogs
    | where TimeGenerated > ago(window) and CorrelationId == corr
    | extend LogType = "1-Interactive";
let nonInteractiveLegs =
    AADNonInteractiveUserSignInLogs
    | where TimeGenerated > ago(window) and CorrelationId == corr
    | extend LogType = "2-NonInteractive";
let spLegs =
    AADServicePrincipalSignInLogs
    | where TimeGenerated > ago(window) and CorrelationId == corr
    | extend LogType = "3-ServicePrincipal", IsInteractive = false,
             UserPrincipalName = "", SignInIdentifier = "";
let miLegs =
    AADManagedIdentitySignInLogs
    | where TimeGenerated > ago(window) and CorrelationId == corr
    | extend LogType = "4-ManagedIdentity", IsInteractive = false,
             UserPrincipalName = "", SignInIdentifier = "";
union isfuzzy=true interactiveLeg, nonInteractiveLegs, spLegs, miLegs
| project CreatedDateTime, TimeGenerated, LogType,
          AppId, AppDisplayName,
          ResourceIdentity, ResourceDisplayName,
          UserPrincipalName, IsInteractive,
          IncomingTokenType, AuthenticationProtocol, ClientCredentialType,
          AuthenticationRequirement, ConditionalAccessStatus,
          IPAddress, UniqueTokenIdentifier, OriginalRequestId,
          SessionId, CorrelationId,
          ResultType, ResultDescription
| sort by CreatedDateTime asc
```

**Query B — discover the CorrelationId for a recent BFF sign-in.**

```kusto
// Find candidate sign-in sessions for the BFF client in the last hour.
let bffAppId = "<BFF_CLIENT_ID>";
SigninLogs
| where TimeGenerated > ago(1h)
| where AppId == bffAppId
| project CreatedDateTime, CorrelationId, UserPrincipalName, IPAddress,
          ResourceDisplayName, ResultType, ConditionalAccessStatus
| sort by CreatedDateTime desc
| take 20
```

**Query C — the audience-boundary proof (two legs, two distinct resources, one session).**

```kusto
// Proves the OBO exchange issued a SECOND token for a DIFFERENT audience.
let window   = 1d;
let bffAppId = "<BFF_CLIENT_ID>";   // confidential client / middle tier
let graphSp  = "00000003-0000-0000-c000-000000000000";  // Microsoft Graph, well-known
AADNonInteractiveUserSignInLogs
| where TimeGenerated > ago(window)
| where AppId == bffAppId or ResourceIdentity == graphSp
| summarize
      Legs            = count(),
      Resources       = make_set(ResourceDisplayName),
      ResourceIds     = make_set(ResourceIdentity),
      TokenIds        = make_set(UniqueTokenIdentifier),
      CredentialTypes = make_set(ClientCredentialType),
      FirstLeg        = min(CreatedDateTime),
      LastLeg         = max(CreatedDateTime)
  by CorrelationId, UserPrincipalName
| where array_length(ResourceIds) > 1     // <-- the proof: >1 distinct audience
| sort by LastLeg desc
```

**Query D — applied Conditional Access policies, per leg.**

```kusto
// Expands appliedConditionalAccessPolicies for every leg of one session.
// Remember: policies that did NOT apply also appear here (result == "notApplied").
let window = 1d;
let corr   = "<CORRELATION_ID>";
union isfuzzy=true
    (SigninLogs                       | where TimeGenerated > ago(window) | extend LogType="Interactive"),
    (AADNonInteractiveUserSignInLogs  | where TimeGenerated > ago(window) | extend LogType="NonInteractive")
| where CorrelationId == corr
| mv-expand ca = todynamic(ConditionalAccessPolicies)
| project CreatedDateTime, LogType, AppDisplayName, ResourceDisplayName,
          ConditionalAccessStatus,
          CaPolicyName      = tostring(ca.displayName),
          CaPolicyId        = tostring(ca.id),
          CaResult          = tostring(ca.result),  // success | failure | notApplied | reportOnlySuccess | reportOnlyFailure | reportOnlyNotApplied
          EnforcedGrant     = tostring(ca.enforcedGrantControls),
          EnforcedSession   = tostring(ca.enforcedSessionControls),
          GrantNotSatisfied = tostring(ca.conditionsNotSatisfied)
| sort by CreatedDateTime asc, CaPolicyName asc
```

**Query E — report-only policy impact across a whole demo run.**

```kusto
// Summarises report-only CA outcomes for the BFF app over the last day.
let bffAppId = "<BFF_CLIENT_ID>";
union isfuzzy=true
    (SigninLogs                       | where TimeGenerated > ago(1d)),
    (AADNonInteractiveUserSignInLogs  | where TimeGenerated > ago(1d))
| where AppId == bffAppId
| mv-expand ca = todynamic(ConditionalAccessPolicies)
| where tostring(ca.result) startswith "reportOnly"
| summarize Events = count(), Users = dcount(UserPrincipalName)
  by CaPolicyName = tostring(ca.displayName), CaResult = tostring(ca.result)
| sort by CaPolicyName asc, Events desc
```

**Query F — confidential-client evidence (escalation Q7, answered from logs).**

```kusto
// Shows which credential type the client presented on each non-interactive leg.
// Public client -> ClientCredentialType is empty/none. Confidential client ->
// "clientSecret" / "clientAssertion" / a certificate-based value.
let bffAppId = "<BFF_CLIENT_ID>";
AADNonInteractiveUserSignInLogs
| where TimeGenerated > ago(1d)
| where AppId == bffAppId
| summarize Legs = count(), Sample = any(CorrelationId)
  by ClientCredentialType, AuthenticationProtocol, IncomingTokenType,
     ResourceDisplayName, FederatedCredentialId
| sort by Legs desc
```

---

## 4. Q3 — Conditional Access behaviour

### 4.1 Public-client SPA vs confidential-client BFF: why the CA surface differs

Four concrete differences, each citable:

1. **The token custody boundary.** A public-client SPA holds the access token and refresh token in the browser. A BFF holds them server-side; the browser holds only a cookie. Microsoft's own warning against relaying middle-tier tokens to a client names the consequence directly: "Inability to satisfy token binding and Conditional Access scenarios requiring claim step-up … Incompatibility with admin-configured device-based policies." ([v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))
2. **Refresh-token lifetime.** A `spa`-platform client's refresh tokens are capped at 24 hours and cannot slide; a `web` confidential client is not capped that way. Recorded in this repo as consequence **A5** (`assets/croesus-3way-session-findings.md`). *(Open item O4 — confirm the current Learn page for the SPA 24-hour RT cap.)*
3. **What the log shows.** Per §3.3, the confidential-client non-interactive leg records the *original* sign-in IP, not the calling host's egress IP. The public-client leg records the actual caller. Location-conditioned CA therefore evaluates a different input.
4. **Where the claims challenge can be handled.** A BFF is a single, server-side, code-controlled place to parse `WWW-Authenticate: Bearer error="insufficient_claims", claims=…` and re-drive an interactive authentication. A SPA can do it too, but every browser tab must implement it; a BFF implements it once.

### 4.2 Does CA apply to the OBO leg? Yes — and Microsoft documents the error shape

> "An error response is returned by the token endpoint when trying to acquire an access token for the downstream API, **if the downstream API has a Conditional Access policy (such as multifactor authentication) set**. The middle-tier service should surface this error to the client application so that the client application can provide the user interaction to satisfy the Conditional Access policy.
>
> To surface this error back to the client, the middle-tier service replies with **HTTP 401 Unauthorized and with a WWW-Authenticate HTTP header containing the error and the claim challenge**. The client must parse this header and acquire a new token from the token issuer, by presenting the claims challenge if one exists. **Clients shouldn't retry to access the middle-tier service using a cached access token.**"
> — [v2-oauth2-on-behalf-of-flow — Error response example](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)

The verbatim error body Learn shows:

```json
{
  "error": "interaction_required",
  "error_description": "AADSTS50079: Due to a configuration change made by your administrator, or because you moved to a new location, you must enroll in multifactor authentication to access 'aaaaaaaa-0000-1111-2222-bbbbbbbbbbbb'.\r\nTrace ID: 0000aaaa-11bb-cccc-dd22-eeeeee333333\r\nCorrelation ID: aaaa0000-bb11-2222-33cc-444444dddddd\r\nTimestamp: 2017-05-01 22:43:20Z",
  "error_codes": [50079],
  "timestamp": "2017-05-01 22:43:20Z",
  "trace_id": "0000aaaa-11bb-cccc-dd22-eeeeee333333",
  "correlation_id": "aaaa0000-bb11-2222-33cc-444444dddddd",
  "claims": "{\"access_token\":{\"polids\":{\"essential\":true,\"values\":[\"00aa00aa-bb11-cc22-dd33-44ee44ee44ee\"]}}}"
}
```

`AADSTS50076` is the sibling code ("due to a configuration change made by your administrator, you must use multi-factor authentication to access …") and arrives through the same `interaction_required` + `claims` mechanism. *(Open item O2 — the retrieved Learn page shows only `50079` verbatim; find the page that documents `50076` explicitly so both can be cited.)*

**Practical consequence for the BFF:** the correct BFF behaviour on an OBO `interaction_required` is *not* to retry and *not* to swallow the error. It is to propagate the `claims` blob outward — as a `WWW-Authenticate` header to an API caller, or as a `claims` parameter on a fresh `/authorize` redirect for a browser session.

### 4.3 Continuous Access Evaluation (CAE)

**What it is:**

> "The mechanism for this conversation is continuous access evaluation (CAE), an industry standard based on Open ID Continuous Access Evaluation Profile (CAEP). The goal for critical event evaluation is for response to be near real time, but latency of up to 15 minutes might be observed because of event propagation time; however, IP locations policy enforcement is instant."
> — [Continuous access evaluation in Microsoft Entra](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation)

**Critical events evaluated** (no CA policy required — available in any tenant): user deleted/disabled; password changed or reset; MFA enabled for the user; admin revoked all refresh tokens; high user risk detected by ID Protection.

**How a client declares CAE capability.** Two linked artefacts:

* Client side — declare the `cp1` client capability. MSAL: `.WithClientCapabilities(new [] {"cp1"})` (.NET), `clientCapabilities: ["CP1"]` (JS), `client_capabilities=["cp1"]` (Python). ([app-resilience-continuous-access-evaluation](https://learn.microsoft.com/en-us/entra/identity-platform/app-resilience-continuous-access-evaluation))
* Token side — the resource sees `xms_cc`. "A value of `cp1` in the access token is the authoritative way to identify that a client application is capable of handling a claims challenge." ([access-token-claims-reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference))

**The claims-challenge wire format:**

```console
HTTP 401; Unauthorized

Bearer authorization_uri="https://login.windows.net/common/oauth2/authorize",
  error="insufficient_claims",
  claims="eyJhY2Nlc3NfdG9rZW4iOnsibmJmIjp7ImVzc2VudGlhbCI6dHJ1ZSwgInZhbHVlIjoiMTYwNDEwNjY1MSJ9fX0="
```

— [app-resilience-continuous-access-evaluation](https://learn.microsoft.com/en-us/entra/identity-platform/app-resilience-continuous-access-evaluation)

**The commitment you take on by declaring `cp1`:**

> "if you declare your app CAE-ready, your application must handle the CAE claim challenge **for all resource APIs** that accept Microsoft Identity access tokens."
> — [app-resilience-continuous-access-evaluation](https://learn.microsoft.com/en-us/entra/identity-platform/app-resilience-continuous-access-evaluation)

**Why a BFF is the right place to handle it:** one server-side code path owns token acquisition for every downstream call, so `cp1` can be declared once and the `WWW-Authenticate` parsing implemented once. A SPA-plus-many-callers topology has to implement it everywhere, and browser JavaScript is the least trustworthy place to hold the refresh token that the challenge must be replayed against.

**Token lifetime consequence:** "Token lifetime increases to long-lived, up to 28 hours, in CAE sessions. … If you aren't using CAE-capable clients, your default access token lifetime remains 1 hour." ([concept-continuous-access-evaluation](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation))

**CAE limitations that bite this scenario:**

* CAE only sees **IP-based named locations** — not country/region conditions, not MFA trusted IPs.
* The **split-path / shared-egress exception**: when Entra sees an allowed egress IP but the resource provider sees a different one, "Microsoft Entra interprets that the client continues to be in an allowed location and should be granted access. Therefore, Microsoft Entra issues a one-hour token that suspends IP address checks at the resource until token expiration." The doc explicitly warns: "**Don't add non dedicated or nonenumerable egress IPs … into Trusted Named Location Conditional Access rules as it can weaken security.**" — This is a direct, citable argument against route **R3** (AWS named location) in `assets/croesus-3way-session-findings.md`, and a qualified caution on route **R1** (VPN/proxy egress) unless the egress set is dedicated and enumerable for *both* Entra and resource-provider traffic.
* "**CAE doesn't support Guest user accounts.**" — a further cost on route **R6** (B2B guest re-shaping).
* Named-location scale limit: >5,000 IP ranges across all location policies disables real-time location enforcement.

### 4.4 Token Protection / token binding — what "unbound" actually means

**Definition:**

> "Token Protection is a Conditional Access session control that attempts to reduce token replay attacks by ensuring only device bound sign-in session tokens, like Primary Refresh Tokens (PRTs), are accepted by Microsoft Entra ID when applications request access to protected resources. When a user registers a supported device with Microsoft Entra, a PRT is issued and cryptographically bound to that device. This binding ensures that even if a threat actor steals the token, it can't be used from another device."
> — [How Token Protection Enhances Conditional Access Policies](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)

**What makes a bearer token "unbound".** A bearer token is, by definition in [RFC 6750](https://www.rfc-editor.org/rfc/rfc6750.txt), a credential whose *possession alone* authorises use. Nothing in the token cryptographically ties it to the client, the device, the TLS channel, or the request. Anyone who obtains the bytes can replay them. The `Unbound (1008)` status does not mean "this specific token was stolen"; it means "the sign-in session token in play was **not** one of the device-bound kinds Token Protection recognises, because the client is not integrated with the platform broker."

**The full status-code table** ([Token Protection deployment guide — Web apps (Preview)](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-web-apps), corroborated by [Windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows) and [Apple](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-apple) guides):

| Code | Meaning | Action |
| --- | --- | --- |
| 1002 | Unbound — lack of Microsoft Entra ID device state | User must register or join the device |
| 1003 | Unbound — device not registered with secure credentials (legacy registration) | Windows: unsupported registration type. macOS: one-time upgrade (self-remediable) |
| 1004 (macOS) | Unbound — device registration isn't hardware-backed | One-time upgrade (self-remediable) |
| 1005 | Unbound — unspecified | Investigate with the correlation ID |
| 1006 | Unbound — OS version unsupported | Upgrade OS |
| 1007 | Unbound — not hardware-backed; signed-in user isn't the registered device owner | Re-register |
| **1008** | **Unbound — "the client isn't integrated with the platform broker, such as Windows Account Manager (WAM)"** | n/a for a server-side or browser client |

The log shape:

```json
"tokenProtectionStatusDetails": {
  "signInSessionStatus": "bound | unbound",
  "signInSessionStatusCode": 1008
}
```

**Scope — and the correction to this repo's current wording.**

> **Platform availability**
>
> | Platform | Native applications | Browser-based applications |
> | --- | --- | --- |
> | Windows | Generally Available | **Preview for supported web apps that access Azure Resource Manager** |
> | iOS / iPadOS | Generally Available | Not supported |
> | macOS | Generally Available | **Preview for supported web apps that access Azure Resource Manager** |
>
> **Supported resources**: for native applications — Exchange Online, SharePoint Online, Microsoft Teams (plus Azure Virtual Desktop and Windows 365 on Windows). For browser-based applications in preview — Azure Resource Manager only, configured as the **Windows Azure Service Management API** resource.
> — [concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)

So: **Microsoft Graph is still not a Token-Protection-covered resource**, and a browser → custom API → Graph flow is still out of scope. The repo's conclusion holds. But `docs/evidence-narrative.md` line 47 and `assets/croesus-3way-session-findings.md` line 239 should be reworded from "native-application clients only" to "native applications (GA) and, in preview, browser-based web apps reaching Azure Resource Manager — and in no case Microsoft Graph."

**Requires a PRT:** "Token Protection in Conditional Access requires the use of PRTs. Scenarios such as the use of unregistered devices aren't available as those devices don't have a PRT." And: "Entra Token Protection only applies to the user who signed into the device."

**Deployment guidance (matches what `scripts/provision-ca-policy.sh` already does):**

> "Start with a pilot group of users and expand over time. Create a Conditional Access policy in report-only mode before enforcing token protection. **Capture both interactive and non-interactive sign-in logs.** Analyze these logs long enough to cover normal application use."
> — [concept-token-protection — Deployment](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)

### 4.5 Proof-of-Possession and DPoP — the current state

This is the question the "unbound token" phrase is really pointing at, so it deserves precision.

**What PoP is:**

> "Bearer tokens are the norm in modern identity flows; however they are vulnerable to being stolen from token caches. Proof-of-Possession (PoP) tokens, as described by [RFC 7800](https://tools.ietf.org/html/rfc7800), mitigate this threat. PoP tokens are bound to the client machine, via a public/private PoP key. The PoP public key is injected into the token by the token issuer (Entra ID) and the client also signs the token using the private PoP key. A fully formed PoP token has two digital signatures — one from the token issuer and one from the client."
> — [Proof-of-Possession (PoP) tokens (MSAL.NET)](https://learn.microsoft.com/en-us/entra/msal/dotnet/advanced/proof-of-possession-tokens)

**The protocol roadmap, verbatim:**

> "There are several PoP protocols and variations. **The Microsoft Entra ID infrastructure aims to supports two types:**
>
> 1. [mTLS POP - RFC 8705](https://datatracker.ietf.org/doc/html/rfc8705). Intended for service to service communication, e.g. a workloads fetching secrets from Azure KeyVault.
> 2. [DPOP - RFC 9449](https://datatracker.ietf.org/doc/html/rfc9449). Intended for public client applications.
>
> … **Legacy support for PoP SHR.** Microsoft provided support for PoP via Signed HTTP Request (SHR). … **This protocol is being phased out and replaced with DPOP.**"
> — [Proof-of-Possession (PoP) tokens — PoP Variants](https://learn.microsoft.com/en-us/entra/msal/dotnet/advanced/proof-of-possession-tokens#pop-variants)

**What is actually shippable today:**

* **Public clients on Windows via the WAM broker.** "PoP on public client flows can be achieved with the use of the Windows broker (WAM). … Currently, PoP tokens are available on Windows 10 and above, as well as Windows Server 2019 and above. Use `IsProofOfPossessionSupportedByClient()` to check if PoP is supported by the client." ([proof-of-possession-tokens — Usage](https://learn.microsoft.com/en-us/entra/msal/dotnet/advanced/proof-of-possession-tokens#usage))
* **Browser SPAs via MSAL.js** with `authenticationScheme: 'pop'`, but only "Once you have determined the authorization service **and resource server** support access token binding." ([Acquiring access tokens protected with Proof-of-Possession](https://learn.microsoft.com/en-us/entra/msal/javascript/browser/access-token-proof-of-possession))
* **Specific first-party resources that opted in** — e.g. Entra ID Governance custom extensions to Logic Apps default to PoP ([custom-extension-security](https://learn.microsoft.com/en-us/entra/id-governance/custom-extension-security#ensure-proof-of-possession-pop-usage)); Azure DevOps is rolling out device-bound web tokens ([Using device bound Entra tokens in Azure DevOps](https://learn.microsoft.com/en-us/azure/devops/release-notes/roadmap/2025/proof-of-possession)).
* **SHR sidecar scenario** for agent workloads ([Scenario: Signed HTTP requests (SHR)](https://learn.microsoft.com/en-us/entra/msidweb/agent-id-sdk/scenarios/signed-http-request)).

**Verdict for this scenario: PARTIAL, and not usable today.** There is **no** general-purpose DPoP or PoP option for a server-side confidential-client BFF acquiring a delegated token for Microsoft Graph. Graph does not advertise DPoP/PoP acceptance for that flow, the DPoP path is positioned for *public* clients, and the mTLS PoP path is positioned for service-to-service where you control both ends. **A BFF's downstream token is, and for now remains, an unbound bearer token.** That must be stated plainly rather than implied away.

### 4.6 How the BFF pattern mitigates the unbound-bearer risk *without* token binding

This is the substantive answer, and it is a reduction in *exposure*, not a change in *token type*.

1. **The token never reaches the browser.** No XSS, no malicious extension, no `localStorage`/`sessionStorage` read, no DevTools copy-out, no shoulder-surf of a network trace can exfiltrate an access token that only ever exists in server memory. This removes the single largest real-world access-token theft surface.
2. **The browser holds a cookie, and a cookie can be hardened in ways a token cannot.** `HttpOnly` (no JS read), `Secure` (TLS only), `SameSite=Lax|Strict` (CSRF reduction), `__Host-` prefix (origin-locked, path-locked), short lifetime, server-side session revocation. A stolen access token cannot be revoked before expiry; a server-side session can be killed instantly. Note the honest caveat already recorded in `docs/evidence-narrative.md`: revoking a delegated grant "does not invalidate Graph access tokens that were already issued."
3. **The blast radius of a stolen cookie is smaller than that of a stolen token.** A cookie is scoped to one origin and is only useful against the BFF, which can apply its own checks (IP, user agent, session binding, re-auth for sensitive operations). A stolen bearer access token is usable directly against Graph from anywhere.
4. **Refresh tokens stay server-side and are not capped at 24 hours** (consequence A5, `assets/croesus-3way-session-findings.md`).
5. **CA and CAE get a competent counterparty.** Per §4.1 and §4.3 — one place to declare `cp1`, one place to parse a claims challenge, one place to re-drive interaction.
6. **The network-based controls Microsoft actually recommends become applicable.** Because the BFF is a known, addressable workload, you can put Global Secure Access / compliant-network or a dedicated enumerable egress in front of it: "Network-based policies prevent sign-in session artifacts (such as refresh tokens) from being replayed outside of designated networks, effectively thwarting token theft and replay attacks that exfiltrate sign-in sessions beyond your organizational boundary." ([Protecting Tokens in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id))

What the BFF pattern does **not** do: it does not make the token a PoP token, and it does not protect against compromise of the BFF host itself. Say both.

---

## 5. Q4 — "Token replay": four distinct things

The word "replay" has been doing four jobs in this engagement. Separating them is most of the work.

### 5.1 OIDC `nonce` replay protection on the id_token

* **What it defends:** an attacker capturing an id_token from one authentication and injecting it into a different session.
* **Mechanism:** the client generates a `nonce`, sends it on `/authorize`, and rejects any id_token whose `nonce` claim does not match.
* **Which layer addresses it in a BFF:** the OIDC middleware in the BFF (ASP.NET Core does this by default). The browser never sees the id_token at all.
* **Sign-in log evidence:** none directly. `nonce` validation happens in the relying party, not at the STS. A mismatch shows up as an application error, not a sign-in log row.

### 5.2 Authorization-code single-use + PKCE

* **What it defends:** interception of the authorization code (in a redirect, a referrer header, a log, or a malicious app registered for the same custom scheme).
* **Mechanism:** codes are single-use and short-lived; PKCE (`code_challenge` / `code_verifier`) binds the redemption to the party that started the flow. Microsoft recommends PKCE for **both** public and confidential authorization-code clients. ([v2-oauth2-auth-code-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow))
* **Which layer addresses it in a BFF:** the BFF generates and holds the verifier server-side. This is exactly escalation question **Q15** ("which component creates and retains the PKCE verifier") in `assets/croesus-escalation-packet.md`.
* **Sign-in log evidence:** a *successful* redemption appears as one non-interactive row with `AuthenticationProtocol == "oAuth2"`. A *second* redemption of the same code fails at the token endpoint. Importantly for this case: **a `spa`-platform code redeemed server-side without an `Origin` header is rejected with `AADSTS9002327`** ("Tokens issued for the 'Single-Page Application' client-type may only be redeemed via cross-origin requests") — the behaviour Experiment A in `assets/croesus-3way-session-findings.md` is designed to probe.

### 5.3 Refresh-token replay detection and rotation

* **What it defends:** a stolen refresh token used to mint new access tokens indefinitely.
* **Mechanism:** refresh tokens rotate on use; Entra revokes on the critical events CAE watches (password change/reset, admin revoke-all, high user risk, account disable). Revocation propagates to CAE-aware resources in near real time. ([concept-continuous-access-evaluation](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation))
* **Which layer addresses it in a BFF:** the refresh token never leaves the server. The 24-hour SPA cap does not apply.
* **Sign-in log evidence:** each renewal is a **non-interactive** row (`AADNonInteractiveUserSignInLogs`) with `IncomingTokenType` indicating a refresh token. **Remember §3.3:** for a confidential client, the `IPAddress` on that row is the *original* issuance IP, not the caller's.

### 5.4 Access-token replay from a stolen token — the "unbound" problem

* **What it is:** the irreducible property of bearer tokens (§4.4). Not fixable by nonce, PKCE, or rotation.
* **Microsoft's answers, in order of strength:**
  1. **Token Protection CA** — "only refresh tokens which are cryptographically bound to the device are used. Bearer refresh tokens, which can be used from any device, are automatically rejected. This method provides the highest level of security for protecting sign-in sessions." Constrained by platform/resource scope (§4.4).
  2. **PRT + device binding** — "Primary Refresh Tokens (PRTs) are protected with a cryptographically secure tie between the PRT and the device (client secret) to which the PRT is issued. On Windows devices, the client secret is securely stored on platform-specific hardware such as Trusted Platform Modules (TPM)."
  3. **Network-based enforcement** — Global Secure Access compliant-network check, or location-based CA on dedicated enumerable egress.
  4. **PoP / DPoP** — not available for this flow today (§4.5).
  — all from [Protecting Tokens in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id)
* **Which layer addresses it in a BFF:** exposure reduction, not elimination (§4.6).
* **Sign-in log evidence:** `TokenProtectionStatusDetails` (only where Token Protection is in scope), plus the ID Protection detections in §5.5, plus `UniqueTokenIdentifier` reuse patterns. **Honest limit, already recorded in `scripts/evidence-kql.kusto`:** sign-in logs "do NOT and cannot prove that identical bearer-token bytes were reused across requests — bearer-byte continuity is not recorded in sign-in logs."

### 5.5 ID Protection risk detections relevant to replay

All from [What are risk detections?](https://learn.microsoft.com/en-us/entra/id-protection/concept-identity-protection-risks).

| Detection | `riskEventType` | Timing | License | Verbatim relevance |
| --- | --- | --- | --- | --- |
| **Anomalous Token** (sign-in and user) | `anomalousToken` | Real-time or offline | P2 | "abnormal characteristics in the token, such as an unusual lifetime or a token played from an unfamiliar location. This detection covers 'Session Tokens' and 'Refresh Tokens.' If the location, application, IP address, User Agent, or other characteristics are unexpected for the user, the administrator should consider this risk as **an indicator of potential token replay**." Caveat: "historically tuned to incur more noise … there's still a higher than normal chance that some of the sessions flagged by this detection are false positives at low and medium risk levels." |
| **Token issuer anomaly** | `tokenIssuerAnomaly` | Offline | P2 | "the **SAML** token issuer for the associated SAML token is potentially compromised. The claims included in the token are unusual or match known attacker patterns." **SAML-specific** — does not apply to the OIDC/OAuth BFF path. |
| **Unfamiliar sign-in properties** | `unfamiliarFeatures` | Real-time | P2 | "properties … can include IP, ASN, location, device, browser, and tenant IP subnet." And critically: "**Unfamiliar sign-in properties can be detected on both interactive and non-interactive sign-ins. When this detection is detected on non-interactive sign-ins, it deserves increased scrutiny due to the risk of token replay attacks.**" — This is the single most on-point detection for the Croesus second-leg observation. |
| **Impossible travel** | `mcasImpossibleTravel` | Offline | P2 + Defender for Cloud Apps | "activities … originating from geographically distant locations within a time period shorter than the time it takes to travel from the first location to the second." |
| **Atypical travel** | `unlikelyTravel` | Offline | P2 | The Entra-native sibling of impossible travel. |
| **Attacker in the Middle** (user risk) | `attackerinTheMiddle` | Offline | M365 E5 + EMS E5 | "triggered when an authentication session is linked to a malicious reverse proxy. … the adversary can intercept the user's credentials, including tokens issued to the user." |
| **Possible attempt to access Primary Refresh Token (PRT)** | `attemptedPrtAccess` | Offline | P2 + MDCA | Requires MDE deployment. |

**KQL to surface these against the BFF:**

```kusto
// Replay-relevant risk detections on any leg touching the BFF or its API.
let apps = dynamic(["<BFF_CLIENT_ID>", "<API_CLIENT_ID>"]);
union isfuzzy=true
    (SigninLogs                      | where TimeGenerated > ago(7d) | extend LogType="Interactive"),
    (AADNonInteractiveUserSignInLogs | where TimeGenerated > ago(7d) | extend LogType="NonInteractive")
| where AppId in (apps)
| where isnotempty(RiskEventTypes_V2) and RiskEventTypes_V2 !in ("[]", "")
| project CreatedDateTime, LogType, AppDisplayName, ResourceDisplayName,
          UserPrincipalName, IPAddress, IsInteractive,
          RiskEventTypes_V2, RiskLevelDuringSignIn, RiskState, RiskDetail,
          CorrelationId, UniqueTokenIdentifier
| sort by CreatedDateTime desc
```

### 5.6 Summary table — replay class → BFF layer → log evidence

| Replay class | Defence | Where it lives in a BFF | Sign-in log evidence |
| --- | --- | --- | --- |
| id_token replay | OIDC `nonce` | BFF OIDC middleware; browser never sees the id_token | None (RP-side validation) |
| Authorization-code replay | Single-use code + PKCE | BFF holds `code_verifier` server-side | One `oAuth2` non-interactive redemption row; `AADSTS9002327` if a `spa` code is redeemed server-side |
| Refresh-token replay | Rotation + CAE revocation | Refresh token never leaves the server | Non-interactive renewal rows; confidential-client rows show the **original** IP |
| Access-token replay (unbound bearer) | Token Protection / PRT / network / (future) DPoP | Not eliminated; exposure reduced — token never reaches the browser | `TokenProtectionStatusDetails` where in scope; `anomalousToken`, `unfamiliarFeatures` on non-interactive legs |

---

## 6. Q5 — Setting up the non-prod tenant demo

### 6.1 Create the BFF app registration correctly

**Option A — Graph `POST /applications` (authoritative, one call).**

```http
POST https://graph.microsoft.com/v1.0/applications
Content-Type: application/json

{
  "displayName": "Croesus BFF (demo)",
  "signInAudience": "AzureADMyOrg",
  "web": {
    "redirectUris": [
      "https://croesus-bff.azurewebsites.net/signin-oidc",
      "https://croesus-bff.azurewebsites.net/signout-callback-oidc"
    ],
    "logoutUrl": "https://croesus-bff.azurewebsites.net/signout-oidc",
    "implicitGrantSettings": {
      "enableAccessTokenIssuance": false,
      "enableIdTokenIssuance": false
    }
  },
  "isFallbackPublicClient": false,
  "groupMembershipClaims": "None",
  "optionalClaims": {
    "idToken": [
      { "name": "acct",      "essential": false },
      { "name": "auth_time", "essential": false }
    ],
    "accessToken": [
      { "name": "idtyp",  "essential": false, "additionalProperties": ["include_user_token"] },
      { "name": "xms_cc", "essential": false },
      { "name": "acct",   "essential": false }
    ]
  },
  "requiredResourceAccess": [
    {
      "resourceAppId": "<API_CLIENT_ID>",
      "resourceAccess": [
        { "id": "<ACCESS_AS_USER_SCOPE_GUID>", "type": "Scope" }
      ]
    }
  ]
}
```

**Option B — `az` CLI, matching the idempotent style already used in `scripts/provision-app-registrations.sh`.**

```bash
#!/usr/bin/env bash
set -euo pipefail

BFF_DISPLAY_NAME="Croesus BFF (demo)"
BFF_HOST="https://croesus-bff.azurewebsites.net"
SIGN_IN_AUDIENCE="AzureADMyOrg"

# 1) Look up or create (idempotent).
BFF_ID="$(az ad app list --display-name "$BFF_DISPLAY_NAME" --query "[0].appId" -o tsv || true)"
if [[ -z "$BFF_ID" ]]; then
  BFF_ID="$(az ad app create \
    --display-name "$BFF_DISPLAY_NAME" \
    --sign-in-audience "$SIGN_IN_AUDIENCE" \
    --query appId -o tsv)"
fi
az ad sp show --id "$BFF_ID" >/dev/null 2>&1 || az ad sp create --id "$BFF_ID" >/dev/null
BFF_OBJ="$(az ad app show --id "$BFF_ID" --query id -o tsv)"

# 2) PATCH the authoritative shape. PATCH is idempotent; re-runs converge.
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/$BFF_OBJ" \
  --headers "Content-Type=application/json" \
  --body "$(cat <<JSON
{
  "web": {
    "redirectUris": [
      "${BFF_HOST}/signin-oidc",
      "${BFF_HOST}/signout-callback-oidc"
    ],
    "logoutUrl": "${BFF_HOST}/signout-oidc",
    "implicitGrantSettings": {
      "enableAccessTokenIssuance": false,
      "enableIdTokenIssuance": false
    }
  },
  "spa": { "redirectUris": [] },
  "publicClient": { "redirectUris": [] },
  "isFallbackPublicClient": false,
  "groupMembershipClaims": "None",
  "optionalClaims": {
    "accessToken": [
      { "name": "idtyp",  "essential": false, "additionalProperties": ["include_user_token"] },
      { "name": "xms_cc", "essential": false },
      { "name": "acct",   "essential": false }
    ]
  }
}
JSON
)" >/dev/null

echo "bff_client_id=$BFF_ID"
```

**API registration addition (on top of what `scripts/provision-app-registrations.sh` already does):** set the access-token version so `aud` is deterministic.

```bash
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/$API_OBJ" \
  --headers "Content-Type=application/json" \
  --body '{ "api": { "requestedAccessTokenVersion": 2 } }' >/dev/null
```

**Managed Identity + Federated Identity Credential (the recommended App Service credential).**

```bash
# 1) User-assigned MI on the App Service.
MI_JSON="$(az identity create -g "$RG" -n "croesus-bff-mi" -o json)"
MI_CLIENT_ID="$(echo "$MI_JSON"    | jq -r .clientId)"
MI_PRINCIPAL_ID="$(echo "$MI_JSON" | jq -r .principalId)"
MI_TENANT_ID="$(echo "$MI_JSON"    | jq -r .tenantId)"
az webapp identity assign -g "$RG" -n "$BFF_APP_NAME" \
  --identities "$(echo "$MI_JSON" | jq -r .id)" >/dev/null

# 2) Trust that MI from the BFF app registration (no stored secret).
#    issuer/subject/audience MUST match the MI token case-sensitively.
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/applications/$BFF_OBJ/federatedIdentityCredentials" \
  --headers "Content-Type=application/json" \
  --body "$(cat <<JSON
{
  "name": "croesus-bff-mi-fic",
  "issuer": "https://login.microsoftonline.com/${MI_TENANT_ID}/v2.0",
  "subject": "${MI_PRINCIPAL_ID}",
  "audiences": ["api://AzureADTokenExchange"],
  "description": "App Service user-assigned MI acts as the BFF confidential client"
}
JSON
)" >/dev/null
```

> Caution to verify at deploy time: the FIC `subject` for the *app-trusts-managed-identity* pattern is the MI's **service principal object id** (`principalId`). Confirm against [Configure an application to trust a managed identity](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity) before running. *(Open item O7.)*

### 6.2 Create a report-only Conditional Access policy targeting the BFF

**Licensing:** Conditional Access requires Microsoft Entra ID P1 (P2 for risk-based conditions). Token Protection is a P1 capability ([concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)), and this repo already states it at `docs/obo-demo-guide.md` line 188. **The demo tenant must have P1 or both the policy creation and the `1008` exhibit fail.**

**Permissions:** `Policy.Read.All` + `Policy.ReadWrite.ConditionalAccess`; least-privilege roles are **Conditional Access Administrator** or **Security Administrator**. ([Create conditionalAccessPolicy](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies))

**`state` values:** `enabled`, `disabled`, `enabledForReportingButNotEnforced`. Use the last one for report-only.

**Policy 1 — report-only, require compliant device for the BFF app (reproduces the Desjardins Dev-tenant shape safely).**

```http
POST https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies
Content-Type: application/json

{
  "displayName": "DEMO - Croesus BFF - require compliant device (REPORT ONLY)",
  "state": "enabledForReportingButNotEnforced",
  "conditions": {
    "clientAppTypes": ["all"],
    "applications": {
      "includeApplications": ["<BFF_CLIENT_ID>"]
    },
    "users": {
      "includeUsers": ["<DEMO_TEST_USER_OBJECT_ID>"],
      "excludeUsers": ["<BREAK_GLASS_USER_OBJECT_ID>"]
    }
  },
  "grantControls": {
    "operator": "OR",
    "builtInControls": ["compliantDevice"]
  }
}
```

**Policy 2 — report-only, Token Protection session control (the labelled Tier 2b `1008` exhibit).** The resource must be a Token-Protection-supported one; Exchange Online (`00000002-0000-0ff1-ce00-000000000000`) is what `scripts/provision-ca-policy.sh` already uses.

```http
POST https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies
Content-Type: application/json

{
  "displayName": "DEMO - Token Protection on EXO - native clients (REPORT ONLY)",
  "state": "enabledForReportingButNotEnforced",
  "conditions": {
    "clientAppTypes": ["mobileAppsAndDesktopClients"],
    "applications": {
      "includeApplications": ["00000002-0000-0ff1-ce00-000000000000"]
    },
    "users": {
      "includeUsers": ["<DEMO_TEST_USER_OBJECT_ID>"],
      "excludeUsers": ["<BREAK_GLASS_USER_OBJECT_ID>"]
    }
  },
  "sessionControls": {
    "secureSignInSession": { "isEnabled": true }
  }
}
```

**`az` wrapper (records the policy id for reversible teardown, matching `scripts/teardown-ca-policy.sh`):**

```bash
POLICY_ID="$(az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies" \
  --headers "Content-Type=application/json" \
  --body @ca-policy.json \
  --query id -o tsv)"
echo "ca_policy_id=$POLICY_ID"
```

> Verify the `sessionControls` property name for Token Protection (`secureSignInSession`) against the current Graph `conditionalAccessSessionControls` resource before running — `scripts/provision-ca-policy.sh` in this repo is the working reference. *(Open item O6.)*

### 6.3 Stream all four sign-in log categories to Log Analytics

Entra tenant diagnostics are a **tenant-level** resource, not a subscription resource. The `--resource` value is the fixed `microsoft.aadiam` provider path:

```bash
#!/usr/bin/env bash
set -euo pipefail

RG="<RESOURCE_GROUP>"
WORKSPACE_NAME="<LOG_ANALYTICS_WORKSPACE_NAME>"

WORKSPACE_ID="$(az monitor log-analytics workspace show \
  -g "$RG" -n "$WORKSPACE_NAME" --query id -o tsv)"

az monitor diagnostic-settings create \
  --name "croesus-demo-entra-signins" \
  --resource "/providers/microsoft.aadiam" \
  --resource-type "microsoft.aadiam/diagnosticSettings" \
  --workspace "$WORKSPACE_ID" \
  --logs '[
    {"category":"SignInLogs",                    "enabled":true},
    {"category":"NonInteractiveUserSignInLogs",  "enabled":true},
    {"category":"ServicePrincipalSignInLogs",    "enabled":true},
    {"category":"ManagedIdentitySignInLogs",     "enabled":true},
    {"category":"AuditLogs",                     "enabled":true},
    {"category":"RiskyUsers",                    "enabled":true},
    {"category":"UserRiskEvents",                "enabled":true}
  ]'
```

Requires at least the **Security Administrator** role for general Entra diagnostic settings. ([howto-configure-diagnostic-settings](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/howto-configure-diagnostic-settings))

> The `az monitor diagnostic-settings create` path for tenant-level `microsoft.aadiam` diagnostics is a known-awkward CLI surface and has historically required either the portal or a REST `PUT` to `https://management.azure.com/providers/microsoft.aadiam/diagnosticSettings/{name}?api-version=2017-04-01-preview`. **Verify this exact `az` invocation before relying on it.** *(Open item O5.)*

### 6.4 Latency — how long before logs appear

Three distinct latencies, all from [Log latency in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/reference-log-latency):

| Situation | Documented delay |
| --- | --- |
| First-time routing of activity logs to a Log Analytics workspace | "you should expect a delay of **up to three days** before the logs appear in the workspace" |
| New storage account or SIEM destination | "a delay of **24 hours** before reporting data appears in those tools" |
| Upgrade from free to P1/P2 | "roughly **24 hours** … before all premium reporting features show data" |
| `signInActivity` (last sign-in) property in Graph | "might take **up to 24 hours** to update" |

The Entra diagnostic-settings how-to repeats the first-time figure: "It might take up to **three days** for the logs to start appearing in the destination." ([howto-configure-diagnostic-settings](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/howto-configure-diagnostic-settings))

**Practical demo consequence:** enable diagnostic settings **at least three days before** any live demo. For same-day iteration, read the sign-in logs in the Entra admin center (portal-side propagation is typically minutes — an adjacent Learn page notes "The sign-in logs may take 5-10 minutes to propagate", [Troubleshoot Azure Sign-in Logs for Surface Hub](https://learn.microsoft.com/en-us/surface-hub/troubleshoot-azure-sign-in-logs-for-surface-hub)) or query Graph `auditLogs/signIns` directly, and treat Log Analytics as the durable archive rather than the live feedback loop.

### 6.5 Run the queries

Use §3.6 Queries A–F and the risk query in §5.5. Substitute `<BFF_CLIENT_ID>`, `<API_CLIENT_ID>`, `<CORRELATION_ID>`. Filter on immutable appIds, never display names — the discipline already stated at the top of `scripts/evidence-kql.kusto`.

---

## 7. Q6 — In-app evidence surface

### 7.1 Claims the BFF can legitimately decode and display (server-side)

All claim definitions from [Access token claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference) and [Optional claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference).

| Claim | What it proves in this demo |
| --- | --- |
| `aud` | **The headline proof.** "Identifies the intended audience of the token. In v2.0 tokens, this value is always the client ID of the API." Leg 1 `aud == API client id`; leg 2 `aud == Graph`. Two audiences ⇒ two tokens ⇒ not a replay. |
| `iss` | "Identifies the STS that constructs and returns the token, and the Microsoft Entra tenant of the authenticated user." Ends in `/v2.0` for v2 tokens. Proves tenancy and token version. |
| `tid` | The tenant the user signed in to. Proves the demo stayed intra-tenant. |
| `oid` | "The immutable identifier for the requestor." The stable user key; use this, not `upn`/`email`, for correlation. |
| `sub` | Pairwise per-application subject. Differs across client ids — itself a demonstration that leg 1 and leg 2 are different token audiences. |
| `scp` | Delegated scopes granted. Proves `access_as_user` on leg 1 and `User.Read` on leg 2 — i.e. delegated, not app-only. |
| `roles` | App roles. **Should be absent** on both legs. Presence would indicate an app-only/client-credentials token and therefore *not* OBO. |
| `azp` (v2) / `appid` (v1) | "The application ID of the client using the token." Proves which client the token was issued to. |
| `azpacr` (v2) / `appidacr` (v1) | **The confidential-client proof in the token itself.** "For a public client, the value is `0`. When you use the client ID and client secret, the value is `1`. When you use a client certificate for authentication, the value is `2`." This is the claim-side twin of the `ClientCredentialType` log field. |
| `idtyp` | "The value is `app` when the token is an app-only token. This claim is the most accurate way for an API to determine if a token is an app token or an app+user token." Requires `include_user_token` to appear on user tokens. |
| `nonce` (id_token) | OIDC replay protection (§5.1). |
| `at_hash` (id_token) | Binds the id_token to the access token returned in the same response — proves they were issued together. |
| `auth_time` | When the user last actually authenticated. Underpins sign-in-frequency and step-up narratives. |
| `amr` | "Identifies the authentication method of the subject of the token" — e.g. `["pwd","mfa"]`. Proves MFA was satisfied. |
| `acr` | v1 only. "A value of `0` … indicates the end-user authentication didn't meet the requirements of ISO/IEC 29115." Low value; prefer `amr` + `acrs`. |
| `acrs` | "the Auth Context IDs of the operations that the bearer is eligible to perform … can be used to trigger a demand for step-up authentication." |
| `xms_cc` | "A value of `cp1` in the access token is the authoritative way to identify that a client application is capable of handling a claims challenge." **Proves the BFF declared CAE capability.** |
| `uti` | "Token identifier claim, equivalent to `jti` in the JWT specification. Unique, per-token identifier that is case-sensitive." **Two different `uti` values across the two legs is the cleanest in-token proof of distinct issuance.** See §7.4. |
| `jti` | The JWT-spec name; Entra emits `uti` in its own tokens. `docs/evidence-narrative.md` correctly notes the demo evidences distinct issuance for token B through correlation id, source, and expiry rather than by decoding token B. |
| `iat` / `nbf` / `exp` | Issuance, validity start, expiry. Differing `iat` across the two legs corroborates distinct issuance. |
| `sid` | "Represents a unique identifier for a session and will be generated when a new session is established." |
| `ver` | `1.0` or `2.0`. Confirms the effect of `requestedAccessTokenVersion`. |

**Hard rule from Learn about decoding other people's tokens:**

> "Don't attempt to validate or read tokens for any API you don't own … Tokens for Microsoft services can use a special format that will not validate as a JWT, and may also be encrypted for consumer (Microsoft account) users. While reading tokens is a useful debugging and learning tool, do not take dependencies on this in your code."
> — [v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)

This is precisely why `docs/evidence-narrative.md` is right not to decode token B (the Graph-audienced token) and to evidence leg 2 structurally instead. Keep that discipline.

**Claims that must never be used for authorization:** `email`, `upn`, `preferred_username`, `name`. "Never use `email` or `upn` claim values to store or determine whether the user in an access token should have access to data. Mutable claim values like these can change over time, making them insecure and unreliable for authorization." ([optional-claims-reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference))

### 7.2 Correlation headers

| Header | Direction | Use |
| --- | --- | --- |
| `client-request-id` | Client → Entra / Graph | A GUID **you** generate per outbound call. Set it on the OBO `/token` POST and on the Graph call so your App Insights event and the server's logs share a key. |
| `request-id` | Graph → client (response) | Graph's server-side request id. Capture it on every Graph response. |
| `x-ms-request-id` | Entra / Azure services → client | The Azure-side request id on the response. |
| `x-ms-ests-server` | Entra → client (response) | Identifies the ESTS instance that served the token request; useful when opening a support case. |
| `WWW-Authenticate` | API → client (401) | Carries `error="insufficient_claims"` and the base64 `claims` blob (§4.3). |

The escalation packet already asks for exactly these: "the Graph URL, HTTP result, `request-id`, and `client-request-id`" (`assets/croesus-escalation-packet.md`, §3 evidence list).

**Capture pattern for the demo (safe — identifiers only, never token material):**

```text
OboExchange event properties:
  clientRequestId   = <GUID you generated>
  graphRequestId    = <request-id from the Graph response>
  msRequestId       = <x-ms-request-id from the Graph response>
  estsServer        = <x-ms-ests-server>
  legAud            = <aud claim of token A>
  legScp            = <scp claim of token A>
  legUti            = <uti claim of token A>
  credentialSource  = "KeyVaultCertificate" | "ManagedIdentityFIC"
  tokenBExpiresOn   = <expires_on from the OBO response>
  tokenBSource      = "IdentityProvider" | "Cache"
```

### 7.3 Security warning — what must never be displayed or logged

**Never emit, display, render, or log:**

* Raw access tokens (full or partial).
* Refresh tokens.
* Authorization codes and PKCE `code_verifier` values.
* Client secrets, `client_assertion` values, certificate private keys, or certificate thumbprints in a form usable for correlation-to-secret.
* Session cookies.
* Any token in a browser-visible DOM node, a console log, a query string, or a client-side telemetry payload.

**Supporting Microsoft guidance:**

* "Access tokens issued to the middle tier are intended for use *only* by that middle tier to communicate with the intended audience endpoint." ([v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))
* "**DO NOT** send access tokens that were issued to the middle tier to anywhere except the intended audience for the token." (same)
* "Don't attempt to validate or read tokens for any API you don't own." (same)
* "the certificate private key never leaves Key Vault, and no credential, private key, or token is ever echoed to stdout/stderr" — the repo's own policy, `scripts/provision-app-registrations.sh` header comment.
* "Please do **not** send raw authorization codes, PKCE verifiers, access or refresh tokens, client secrets, certificates, private keys, or cookies. Presence indicators and SHA-256 fingerprints are sufficient for every field above." — `assets/croesus-escalation-packet.md`, §3.

**What to show instead:** decoded *claims* (§7.1), presence indicators (`hasRefreshToken: true`), SHA-256 fingerprints where byte-identity must be compared, expiry timestamps, and correlation identifiers. `docs/evidence-narrative.md` already follows this: it records "the credential `SourceType` and the Key Vault certificate name, not a thumbprint."

A further caution specific to a *BFF demo page*: if the BFF renders decoded claims into HTML, that page is itself a claims-disclosure surface. Gate it behind authentication, scope it to the signed-in user's own token, never render another user's claims, and keep it off by default in any deployed environment (the same gating discipline `Demo:EnableReplay` already uses).

### 7.4 Does `uniqueTokenIdentifier` in the sign-in log equal the `uti` claim?

**Verdict: they describe the same underlying token identity, but they are NOT documented as byte-equal and you must not assert equality without empirical confirmation.**

The two definitions:

* Sign-in log: "`UniqueTokenIdentifier` — A unique **base64 encoded request identifier** used to track tokens issued by Azure AD as they are redeemed at resource providers." ([SigninLogs table reference](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/signinlogs)); and in the portal-facing doc: "**Unique token identifier:** A unique identifier for the token passed during the sign-in. This identifier is used to correlate the sign-in with the token request." ([concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details))
* Token claim: "`uti` — Token identifier claim, equivalent to `jti` in the JWT specification. Unique, per-token identifier that is case-sensitive." ([access-token-claims-reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference))

Both are "a unique identifier for the token". Neither Learn page states that they are the same string, and the log page's "base64 encoded **request** identifier" wording leaves room for it to be the request id rather than the token id, or a differently-encoded rendering of the same value.

Note also the adjacent field: "**Request ID:** An identifier that corresponds to an issued token. **If you're looking for sign-ins with a specific token, you need to extract the request ID from the token, first.**" ([concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details)) — this strongly implies the token carries the value that joins to the log, which is what `uti` is, but it names *Request ID* rather than *Unique token identifier*.

**Recommended empirical test in the demo tenant (cheap, decisive):**

1. Sign in once through the BFF.
2. Server-side, log the `uti` claim of token A (the API-audienced token) — the claim value only, never the token.
3. Wait for Log Analytics ingestion, then run:

```kusto
let utiFromToken = "<UTI_CLAIM_VALUE>";
union isfuzzy=true
    (SigninLogs                      | where TimeGenerated > ago(2d)),
    (AADNonInteractiveUserSignInLogs | where TimeGenerated > ago(2d))
| where UniqueTokenIdentifier =~ utiFromToken
     or OriginalRequestId      =~ utiFromToken
     or Id                     =~ utiFromToken
| project CreatedDateTime, AppDisplayName, ResourceDisplayName,
          UniqueTokenIdentifier, OriginalRequestId, Id, CorrelationId
```

4. Record which column matched. If `UniqueTokenIdentifier` matches, you have a demonstrated token-to-log-row join and the demo gains a genuinely strong exhibit. If it does not, fall back to `CorrelationId` + `UserPrincipalName` (as `scripts/evidence-kql.kusto` Query 5 already does) and say so honestly.

*(Open item O1 — this is the single highest-value unresolved question in this research.)*

---

## 8. Open items and unresolved questions

| # | Item | Why it matters | How to close |
| --- | --- | --- | --- |
| O1 | **`uniqueTokenIdentifier` == `uti`?** | Would give a direct token-to-sign-in-log join and materially strengthen the evidence pack | Run the §7.4 experiment in the demo tenant |
| O2 | `AADSTS50076` verbatim Learn citation | Both 50076 and 50079 were requested; only 50079 was found verbatim in the OBO doc | Check `https://login.microsoftonline.com/error` and the CA/MFA troubleshooting pages |
| O3 | `tokenEncryptionKeyId` authoritative definition | Asserted as SAML-only from general knowledge; not confirmed from the retrieved pages | Fetch the Graph [application resource type](https://learn.microsoft.com/en-us/graph/api/resources/application) page |
| O4 | SPA 24-hour refresh-token cap — current Learn citation | Consequence A5 is a load-bearing commercial argument to Croesus and currently rests on repo assertion, not a fetched citation | Fetch [Refresh tokens in the Microsoft identity platform](https://learn.microsoft.com/en-us/entra/identity-platform/refresh-tokens) |
| O5 | `az monitor diagnostic-settings create --resource /providers/microsoft.aadiam` | The tenant-level CLI surface is historically awkward; the §6.3 command is plausible but unverified | Run it in the demo tenant, or fall back to the REST `PUT` shown in §6.3 |
| O6 | Graph `sessionControls.secureSignInSession` property name for Token Protection | §6.2 Policy 2 will fail on a wrong property name | Cross-check `scripts/provision-ca-policy.sh` (the working reference) against the Graph `conditionalAccessSessionControls` resource |
| O7 | FIC `subject` value for the app-trusts-user-assigned-MI pattern | §6.1 asserts `principalId`; a wrong value means silent auth failure | Fetch [workload-identity-federation-config-app-trust-managed-identity](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity) |
| O8 | Does Entra still reject a `spa` code redeemed server-side with a **synthesised** `Origin` header? | This is Experiment A attempt 2 in `assets/croesus-3way-session-findings.md` and would collapse A3 from two explanations to one | Empirical only — run Experiment A |
| O9 | Whether Microsoft Graph advertises any PoP/DPoP acceptance for delegated confidential-client tokens | Would change the §4.5 verdict from "no" to "yes for Graph" | Monitor the Entra identity-platform what's-new and the DPoP rollout |
| O10 | Front-channel logout: is `web.logoutUrl` the front-channel logout URL, or is there a separate `frontchannelLogoutUri`? | §2.2 and §6.1 assume `logoutUrl` | Fetch the Graph `application` / `webApplication` resource page |

---

## 9. Mapping the original Croesus finding to a resolution

### 9.1 What was observed

Two sign-in rows sharing SessionId `007bc799-6350-25bf-fbe9-ebbcb7093b63` and tenant `728d20a5-0b44-47dd-9470-20f37cbf2d9a` (`assets/app-registration-analysis-findings.md` §4):

| Field | Sign-in #1 (interactive) | Sign-in #2 (server-side) |
| --- | --- | --- |
| `IsInteractive` | TRUE | FALSE |
| IP Address | Desjardins corp | `3.97.32.113` (AWS) |
| Token Protection status | **bound (code 0)** | **unbound (code 1008)** |
| Browser | Edge 146.0.0 | (empty) |
| `deviceId` | `5b4b24f4-…` | `5b4b24f4-…` (same) |
| `isCompliant` / trustType | true / Entra joined | true / Entra joined |
| App / Resource | `sp-CentralGPD-prod-fed` / Microsoft Graph | same / Microsoft Graph |
| `ResultType` | 0 | 0 |

Characterised as: **"this second hop presents a replayed user token to Microsoft Graph"**, evidenced by **`Unbound (1008)`** (`assets/latest-info/email-thread-with-croesus.md` line 625).

### 9.2 What the evidence actually supports — three separations

**(a) `1008` is a binding status, not a grant classifier, and not replay evidence.**
Per §4.4, `1008` means "the client isn't integrated with the platform broker, such as Windows Account Manager (WAM)". A server-side .NET process on an AWS host is *definitionally* not broker-integrated. Nothing about that observation distinguishes a legitimate authorization-code redemption from an OBO exchange from a replay. Furthermore, **Microsoft Graph is not a Token-Protection-supported resource** ([concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)), so the binding status on a Graph-targeted leg is out of scope and benign. This is exactly the correction already recorded at `assets/croesus-escalation-packet.md` line 60 and `assets/croesus-3way-session-findings.md` line 239. **The original email overstated the evidence.** That should be acknowledged to the customer plainly — it is the fastest route to re-establishing technical credibility.

**(b) The *same* `deviceId` and `isCompliant: true` on a leg with no device is the real anomaly.**
Sign-in #2 has an empty browser field and an AWS source IP, yet carries `deviceId 5b4b24f4-…` and `isCompliant: true`. That is finding **V1** in `assets/app-registration-analysis-findings.md`: "A non-broker-bound token carrying compliant-device claims from an external IP can satisfy **device-based CA grants it should not**." This is the security-relevant observation, and it is *independent* of whether a replay occurred. It is a consequence of a device-derived claim travelling with a credential to a host that has no device.

**(c) Two rows with the same resource and the same session is consistent with several grants.**
Both rows name Microsoft Graph as the resource. A genuine OBO would show **two different resources** (API on leg 1, Graph on leg 2) — the exact signature `docs/evidence-narrative.md` builds the demo around. Seeing Graph on both rows is consistent with a replay, *and* with a refresh-token grant for Graph, *and* with a second authorization-code redemption for Graph. Sign-in logs cannot distinguish them, because **bearer-byte continuity is not recorded** (`scripts/evidence-kql.kusto`, header comment). The grant genuinely remains unclassified without a captured `/token` request (escalation Q7).

### 9.3 What the BFF pattern changes — and the sign-in log evidence that proves each change

| # | Change | Why it follows | Sign-in log evidence that proves it |
| --- | --- | --- | --- |
| 1 | The backend becomes a **confidential client** under the `web` platform, presenting a credential on `/token` | §2.1–2.3; and Entra rejects a plain server-side `spa` redemption with `AADSTS9002327` (§5.2) | `ClientCredentialType` becomes non-empty (`clientSecret` / `clientAssertion`) on the non-interactive leg — **Query F** in §3.6. Token-side twin: `azpacr` = `1` (secret) or `2` (certificate). With MI+FIC, `FederatedCredentialId` is populated. |
| 2 | **The AWS egress IP stops appearing on the second leg** | "The IP address of non-interactive sign-ins performed by confidential clients doesn't match the actual source IP … Instead, it shows the original IP used for the original token issuance." ([concept-noninteractive-sign-ins](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-noninteractive-sign-ins)) | The `IPAddress` on the non-interactive leg becomes the Desjardins corp IP instead of `3.97.32.113`. **This alone dissolves the observation that drives routes R1 and R3.** State the caveat: a logging behaviour, not a network control. |
| 3 | **Two audiences replace one** | The OBO middle tier must receive an API-audienced token and acquire a *fresh* Graph-audienced token; "Applications can't redeem a token for a different app" ([v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)) | **Query C** in §3.6 returns `array_length(ResourceIds) > 1` for one `CorrelationId`. In-token: two distinct `aud` values and two distinct `uti` values. This is the headline proof in `docs/evidence-narrative.md`. |
| 4 | **The device claim stops travelling with a credential** | The browser holds a cookie, not a token. Downstream calls are made by a workload identity carrying a delegated user context, not by a relayed user token | Finding V1 is structurally closed. The non-interactive leg no longer presents `isCompliant: true` derived from a device that is nowhere near the request. |
| 5 | **CA and CAE gain a competent counterparty** | §4.2–4.3. One server-side place to declare `cp1`, parse `WWW-Authenticate: insufficient_claims`, and re-drive interaction | `xms_cc: ["cp1"]` present in the token. `ConditionalAccessStatus` and per-policy `CaResult` legible per leg — **Query D** in §3.6. CAE visible in the sign-in detail. |
| 6 | **Refresh tokens leave the browser and lose the 24-hour cap** | Consequence A5 (`assets/croesus-3way-session-findings.md`) — pending citation O4 | Non-interactive renewal rows continue past the 24-hour mark without a fresh interactive sign-in. |
| 7 | **"Replay" becomes structurally impossible to confuse with OBO** | A replay reuses one token with one `aud` and one `uti`; an OBO issues a second token with a different `aud` and `uti` | Side by side: the gated Tier 2a `ReplayAttempt` App Insights event (one token, one audience, no fresh issuance) against the `OboExchange` event (two audiences, fresh `expires_on`). `docs/obo-demo-guide.md` step 7 already builds this. |

### 9.4 What the BFF pattern does **not** change — say this out loud

1. **The downstream token is still an unbound bearer token.** Per §4.5, there is no DPoP or PoP option for a confidential-client BFF calling Graph today. The BFF reduces the *exposure surface* (§4.6); it does not change the *token type*. Anyone claiming otherwise will be caught by a security-literate reviewer.
2. **Token Protection still will not fire for this flow.** Graph is not a supported resource. The `1008` exhibit stays a clearly labelled Tier 2b artefact on Exchange Online — exactly as `docs/evidence-narrative.md` and `scripts/provision-ca-policy.sh` already scope it.
3. **A leg with no device still fails a compliant-device grant.** If the Dev tenant's CA policy *grants* on `compliantDevice`, a confidential-client server-side leg still has no device. The policy must use location as a *condition* (exclusion), not a grant — consequence **A6** in `assets/croesus-3way-session-findings.md`. The BFF does not make an unsatisfiable grant satisfiable.
4. **The prod/non-prod asymmetry (A4) is still unexplained and is still Desjardins' to close.** Nothing in this research resolves why Prod passes and Dev does not when both legs lack device context. Route **R2** (tenant parity check) remains the highest-value, zero-dependency next action.
5. **OBO is not required for an ordinary BFF.** Guardrail from `assets/croesus-3way-session-findings.md` §7: "Do **not** mandate On-Behalf-Of. Nothing yet establishes that a middle-tier requirement exists." A BFF that calls Graph directly with the user's delegated token — acquired by *the BFF*, for *Graph*, via its own confidential-client redemption — is a perfectly valid shape and needs no exposed API scope. OBO is required only when a *separate* downstream API sits behind the BFF.

### 9.5 The one-paragraph version for the customer

> The `Unbound (1008)` line in the sign-in log is a device-and-session binding status. It records that the calling client is not integrated with the Windows platform broker — which a server-side process never is — and it does not identify an OAuth grant or prove that a token was replayed. Microsoft Graph is not a resource Token Protection covers, so that status on a Graph-bound leg is expected and benign. What the logs *do* show is a non-interactive leg from an external IP that nonetheless carries device-compliance claims derived from the user's workstation. That is the real finding, and it is a direct consequence of a user's token being relayed to a host that has no device of its own — the pattern Microsoft explicitly warns against. Registering the redeeming backend as a confidential `web` client fixes the shape: the backend presents its own credential (visible as `ClientCredentialType` in the log), the vendor's cloud IP stops appearing on the second leg entirely because Entra records the original issuance IP for confidential clients, downstream calls resolve to a second, distinctly-audienced token with its own `uti`, and the device claim stops travelling with the credential. The token that ultimately reaches Microsoft Graph is still a bearer token — Entra does not yet offer DPoP or proof-of-possession for this flow — but it never enters the browser, which removes the largest practical theft surface, and the backend becomes the one place that can answer a Conditional Access claims challenge properly.

---

## 10. Sources

**App registration and protocol**

* [Microsoft identity platform and OAuth2.0 On-Behalf-Of flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
* [Microsoft identity platform and OAuth 2.0 authorization code flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow)
* [Understanding the app manifest (Azure AD Graph format)](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest)
* [How to Register an App in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app)
* [Access token claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference)
* [Optional claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference)
* [Workload Identity Federation](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation)

**Sign-in logs and monitoring**

* [Sign-in logs in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-ins)
* [Non-interactive sign-in logs](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-noninteractive-sign-ins)
* [Learn about the sign-in log activity details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details)
* [Learn about the monitoring and health activity log schemas](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/reference-azure-monitor-sign-ins-log-schema)
* [Azure Monitor Logs reference — SigninLogs](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/signinlogs)
* [What are the identity logs you can stream to an endpoint?](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-diagnostic-settings-logs-options)
* [How to configure Microsoft Entra diagnostic settings](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/howto-configure-diagnostic-settings)
* [Integrate Microsoft Entra logs with Azure Monitor logs](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/howto-integrate-activity-logs-with-azure-monitor-logs)
* [Log latency in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/reference-log-latency)
* [Microsoft Entra ID Protection scenario: Mastering risk analysis](https://learn.microsoft.com/en-us/entra/architecture/id-protection-guide-analyze)

**Conditional Access, CAE, Token Protection**

* [Continuous access evaluation in Microsoft Entra](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation)
* [How to use Continuous Access Evaluation enabled APIs in your applications](https://learn.microsoft.com/en-us/entra/identity-platform/app-resilience-continuous-access-evaluation)
* [How Token Protection Enhances Conditional Access Policies](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)
* [Token Protection deployment guide — Web apps (Preview)](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-web-apps)
* [Token Protection Deployment Guide — Windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows)
* [Token Protection Deployment Guide — Apple Platforms](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-apple)
* [Create conditionalAccessPolicy — Microsoft Graph v1.0](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies)

**Token theft, replay, PoP/DPoP**

* [Protecting Tokens in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id)
* [What are risk detections? — Microsoft Entra ID Protection](https://learn.microsoft.com/en-us/entra/id-protection/concept-identity-protection-risks)
* [Proof-of-Possession (PoP) tokens (MSAL.NET)](https://learn.microsoft.com/en-us/entra/msal/dotnet/advanced/proof-of-possession-tokens)
* [Acquiring access tokens protected with Proof-of-Possession (MSAL.js)](https://learn.microsoft.com/en-us/entra/msal/javascript/browser/access-token-proof-of-possession)
* [Using device bound Entra tokens in Azure DevOps](https://learn.microsoft.com/en-us/azure/devops/release-notes/roadmap/2025/proof-of-possession)
* [Best practices for securing the custom extension extensibility to Azure Logic Apps](https://learn.microsoft.com/en-us/entra/id-governance/custom-extension-security#ensure-proof-of-possession-pop-usage)
* [Scenario: Signed HTTP requests (SHR)](https://learn.microsoft.com/en-us/entra/msidweb/agent-id-sdk/scenarios/signed-http-request)
* [Token theft playbook](https://learn.microsoft.com/en-us/security/operations/token-theft-playbook)
* [RFC 6750 — OAuth 2.0 Bearer Token Usage](https://www.rfc-editor.org/rfc/rfc6750.txt)
* [RFC 7800 — Proof-of-Possession Key Semantics for JWTs](https://tools.ietf.org/html/rfc7800)
* [RFC 8705 — OAuth 2.0 Mutual-TLS Client Authentication and Certificate-Bound Access Tokens](https://datatracker.ietf.org/doc/html/rfc8705)
* [RFC 9449 — OAuth 2.0 Demonstrating Proof of Possession (DPoP)](https://datatracker.ietf.org/doc/html/rfc9449)

**Workspace files consulted (read-only)**

* `assets/croesus-escalation-packet.md`
* `assets/croesus-3way-session-findings.md`
* `assets/app-registration-analysis.md`
* `assets/app-registration-analysis-findings.md`
* `assets/app-registration-verification.md`
* `assets/latest-info/email-thread-with-croesus.md`
* `assets/dev-dev.txt`, `assets/dev-prod.txt`, `assets/prod-prod.txt`
* `docs/evidence-narrative.md`
* `docs/configuration-contract.md`
* `docs/obo-demo-guide.md`
* `docs/deck/generate-deck.py`
* `scripts/evidence-kql.kusto`
* `scripts/provision-app-registrations.sh`, `scripts/teardown-app-registrations.sh`
* `scripts/provision-ca-policy.sh`, `scripts/teardown-ca-policy.sh`
* `scripts/negative-test.sh`, `scripts/verify-clean.sh`, `scripts/setup-deploy-identity.sh`
