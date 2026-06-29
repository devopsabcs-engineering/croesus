<!-- markdownlint-disable-file -->
# Subagent Research: Azure Hosting + GitHub Actions CI/CD + OBO Evidence (Croesus / Desjardins demo)

Status: Complete

## Research Topics / Questions

1. Azure hosting options for an MSAL.js SPA + confidential-client middle-tier Web API performing Entra On-Behalf-Of (OBO) to Microsoft Graph. Compare Azure Static Web Apps, Azure App Service, Azure Container Apps; recommend ONE simplest credible option.
2. GitHub Actions CI/CD (repo `devopsabcs-engineering/croesus`) using OIDC federated credentials (no stored secrets) to deploy SPA + API. Where the confidential client secret/cert should live (Key Vault vs GitHub secret); recommend cert + Key Vault.
3. Evidence/logging strategy proving the second token leg is a bound OBO token, not an unbound token replay (Token Protection code 1008 vs bound code 0). Entra sign-in logs, Graph activity, App Insights / structured backend logging of the OBO exchange.
4. How the CI/CD run can output/link to this evidence.

---

## ⚠️ Critical Framing Correction (read first)

The user request conflates **two different "binding" concepts**. This matters for what the demo can actually prove, so it is called out up front.

### Concept A — Entra "Token Protection" CA session control (the source of code 1008)

- "Token Protection" is a **Conditional Access session control** ("Require token protection for sign-in sessions"). It checks whether the sign-in session token is **device-bound to a Primary Refresh Token (PRT)** on a registered device. Source: Token Protection concept doc, ms.date 2026-03-24.
- The sign-in-log field is **"Token Protection - Sign In Session"** with values **Bound** / **Unbound**. When **Unbound**, a `signInSessionStatusCode` is emitted:
  - `1002` – unbound due to lack of Entra device state
  - `1003` – unbound: device state doesn't satisfy CA (unsupported registration / no fresh creds)
  - `1005` – unbound, other/unspecified
  - `1006` – unbound: OS version unsupported
  - **`1008` – unbound: the client isn't integrated with the platform broker (e.g., Windows Account Manager / WAM)**
  - Source: Token Protection Windows deployment guide, "Capture logs and analyze → Sign-in logs", ms.date 2026-03-24.
- **There is no documented "code 0 = bound" status code.** "Bound" is reported as the textual value `Bound`; the numeric `signInSessionStatusCode` values listed are the *unbound* reasons. The premise "bound (code 0) vs unbound (1008)" is partly a mischaracterization — `1008` specifically means "client not integrated with the platform broker (WAM)", not "token replay".
- **Token Protection supports NATIVE apps only; browser-based apps are explicitly NOT supported** (concept doc: *"Token Protection currently supports native applications only. Browser-based applications are not supported."*). It also only protects **Exchange Online, SharePoint Online, Teams** (+ AVD/W365 on Windows) — **not a custom Web API and not Microsoft Graph generically**.
- **Implication:** A browser **MSAL.js SPA → custom Web API → Graph OBO** demo will **not naturally produce a Token Protection bound/unbound (1008) signal at all**, because (a) it's a browser app, and (b) the resources aren't EXO/SPO/Teams. Trying to make `1008` the headline "proof" would be technically incorrect and would not survive scrutiny from a security-literate customer (Desjardins).

### Concept B — OBO audience-binding (this is the property the demo SHOULD prove)

The genuinely demonstrable "this token is bound and not a replay" property for an OBO middle tier is **audience/issuer binding of the OAuth tokens themselves**:

- The middle-tier API must only accept a **token A** whose `aud` claim equals the **API's own** App ID URI / client ID (not Graph, not the SPA).
- Entra's OBO endpoint **refuses to redeem** a token whose `aud` is not the redeeming app. From the OBO doc (ms.date 2025-01-04): *"This token must have an audience (`aud`) claim of the app making this OBO request… Applications can't redeem a token for a different app (for example, if a client sends an API a token meant for Microsoft Graph, the API can't redeem it using OBO. It should instead reject the token)."*
- The OBO request to the token endpoint uses `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, `requested_token_use=on_behalf_of`, `assertion=<token A>`, and a **confidential-client credential** (`client_secret` OR `client_assertion` signed by a certificate). The result is a **freshly issued token B** with `aud=https://graph.microsoft.com`.
- **Proof of "no unbound replay"** = show that:
  1. Leg 1 token (SPA→API) has `aud = <API>` and `scp = <API scope>` (NOT `aud=graph`).
  2. Leg 2 token (API→Graph) is a **new** token with `aud = https://graph.microsoft.com`, a different `jti`/issue time, obtained via `jwt-bearer` + `on_behalf_of` using the API's confidential credential — i.e., the API did **not** forward/replay the client's bearer token to Graph.
  3. The API **rejects** any token whose `aud` ≠ API (negative test: forwarding a Graph-scoped token to the API fails; forwarding the API token directly to Graph fails because its `aud` is the API, not Graph).

**Recommendation for the deliverable:** Lead the evidence story with **Concept B (OBO audience binding)** — it's the correct, defensible proof that "Croesus implements OAuth correctly." Optionally include **Concept A (Token Protection 1008)** as a *separate, clearly-labeled* advanced section showing the team understands device-bound session tokens, but do **not** present 1008 as the proof of the OBO leg.

---

## Findings

### 1. Azure hosting options for SPA + confidential-client OBO API

Source: Azure Static Web Apps "API support" overview (ms.date 2024-10-02). API backend matrix:

| Service | Managed by SWA | Bring-your-own |
| --- | --- | --- |
| Azure Functions | ✔ | ✔ |
| Azure API Management | — | ✔ |
| Azure App Service | — | ✔ |
| Azure Container Apps | — | ✔ |

Key SWA constraints (same doc): one backend type per environment; API route prefix must be `/api`; HTTP only (no WebSocket); **45-second** max request duration; network-isolated backends not supported; bring-your-own requires the **Standard** plan and can't link to PR preview environments.

Comparison for THIS scenario (confidential client that must hold a secret/cert and reach the Entra token endpoint + Graph):

- **Azure Static Web Apps + managed Azure Functions API** — Simplest deploy story (one resource, one GitHub Action), free/cheap, global CDN for the SPA. A managed Functions API **can** run server-side OBO code and read a secret/cert from app settings or a Key Vault reference. Caveat: SWA's **built-in authentication** (the `/.auth/*` endpoints, `x-ms-client-principal`) is an **EasyAuth-style platform feature and is NOT the same as a custom MSAL.js + OBO flow** — for this demo the SPA must use **MSAL.js directly** against the SPA app registration, and the Functions API validates the bearer token and runs OBO itself; SWA built-in auth should be left off / unused. The 45s limit and "one backend type" are non-issues here.
- **Azure App Service (one or two Web Apps)** — Most "credible enterprise" shape and the easiest place to host a **confidential client** because: native **Managed Identity + Key Vault references** for the secret/cert (App Service Key Vault references doc, ms.date 2026-04-09), first-class **Application Insights** integration, and `azure/webapps-deploy` GitHub Action. Two apps (SPA static site on one, API on another) or a single app serving both. Slightly more infra than SWA.
- **Azure Container Apps** — Most flexible/portable but the **most infra and CI/CD overhead** (Dockerfiles, ACR, image build/push, ingress). Overkill for a small two-tier demo; only choose if containers are a hard requirement.

**RECOMMENDATION (single simplest credible option):**
**Azure App Service — two Web Apps (or one App Service Plan hosting two apps): `croesus-spa` (static SPA) and `croesus-api` (the confidential-client OBO Web API).**

Rationale:
- The confidential client needs a **certificate/secret + reach to `login.microsoftonline.com` and `graph.microsoft.com`** — App Service gives this natively with **system-assigned Managed Identity → Key Vault reference** for the cert, no secret in code or config. (App Service Key Vault references doc.)
- **Application Insights** is a one-toggle integration on App Service, which is exactly where the OBO structured-logging evidence will live.
- **OIDC GitHub Actions deploy** via `azure/login` + `azure/webapps-deploy` is the documented, well-trodden path.
- It looks like a real production middle tier to the customer (not a serverless toy), which strengthens the "we can do OAuth properly" message.

Acceptable alternative if "simplest possible" outranks "most credible": **SWA + managed Functions API** (single resource, single workflow) — note the SWA-built-in-auth caveat above.

> Static Web Apps built-in auth ≠ custom MSAL OBO. SWA's `/.auth` is platform-managed federation that yields `x-ms-client-principal`; it does **not** give the API a delegated user access token usable for OBO. The demo must do MSAL.js (SPA) → bearer token → API validates → API runs OBO. Confirmed by SWA apis-overview "Integrated security" wording vs. the OBO doc's explicit middle-tier flow.

### 2. GitHub Actions CI/CD with OIDC (no stored secrets) — `devopsabcs-engineering/croesus`

Source: "Authenticate to Azure from GitHub Actions by OpenID Connect" (ms.date 2024-07-01).

- Configure a **federated identity credential (FIC)** on a Microsoft Entra app registration **OR** a user-assigned managed identity, trusting GitHub's OIDC issuer for the repo (`repo:devopsabcs-engineering/croesus:ref:refs/heads/main`, or `environment:production`). Workload identity federation create-trust doc is linked from the OIDC article.
- Workflow needs `permissions: id-token: write` (+ `contents: read`).
- `azure/login@v2` consumes `client-id`, `tenant-id`, `subscription-id` (stored as **non-secret** GitHub Actions variables or secrets — they're identifiers, not credentials). No client secret is stored for the **deploy** identity.
- Default OIDC audience `api://AzureADTokenExchange`.
- After login, deploy with `azure/webapps-deploy@v3` (App Service) — links referenced from the OIDC doc ("Azure webapp deploy", "Azure functions").

**Two distinct credentials — do not confuse them:**

| Credential | Purpose | How it's secured |
| --- | --- | --- |
| **Deploy identity** (GitHub → Azure) | CI/CD pushes code to App Service / reads Key Vault | **OIDC federated credential — NO stored secret** |
| **Croesus API confidential-client credential** (API → Entra token endpoint for OBO) | Runtime OBO token leg | **Certificate in Key Vault**, surfaced to App Service via **Key Vault reference + Managed Identity**; never in GitHub, never in `.bicepparam`, never in code |

**Recommendation: certificate + Key Vault over plaintext secret.** The OBO doc explicitly supports the certificate path (`client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`, `client_assertion=<JWT signed by the cert>`) as the **Second case** alongside the shared-secret First case. Certificates are the Microsoft-recommended confidential-client credential; Key Vault gives centralized rotation + audit. App Service Key Vault references doc shows `@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/<name>)` app-setting syntax, resolved via the app's **system-assigned Managed Identity** granted **Key Vault Secrets User** (RBAC) or **Get** (access policy).

**Example workflow shape** (illustrative — verify action versions before use):

```yaml
name: deploy-croesus
on:
  push:
    branches: [ main ]
permissions:
  id-token: write       # required for OIDC
  contents: read
env:
  AZURE_TENANT_ID:   ${{ vars.AZURE_TENANT_ID }}
  AZURE_CLIENT_ID:   ${{ vars.AZURE_CLIENT_ID }}        # deploy identity (FIC), NOT the API cred
  AZURE_SUBSCRIPTION_ID: ${{ vars.AZURE_SUBSCRIPTION_ID }}
  SPA_APP_NAME:  croesus-spa
  API_APP_NAME:  croesus-api
jobs:
  build-spa:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: 20 }
      - name: Build SPA (inject public config — client IDs/tenant/scope are NOT secrets)
        working-directory: ./spa
        env:
          VITE_SPA_CLIENT_ID: ${{ vars.SPA_CLIENT_ID }}
          VITE_TENANT_ID:     ${{ vars.AZURE_TENANT_ID }}
          VITE_API_SCOPE:     ${{ vars.API_SCOPE }}      # e.g. api://<api-app-id>/access_as_user
        run: npm ci && npm run build
      - uses: actions/upload-artifact@v4
        with: { name: spa, path: ./spa/dist }
  build-api:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4   # or setup-dotnet, per API stack
        with: { node-version: 20 }
      - run: npm ci && npm run build
        working-directory: ./api
      - uses: actions/upload-artifact@v4
        with: { name: api, path: ./api }
  deploy:
    needs: [ build-spa, build-api ]
    runs-on: ubuntu-latest
    environment: production           # gate + environment-scoped vars
    steps:
      - uses: actions/download-artifact@v4
      - name: Azure login (OIDC, no secret)
        uses: azure/login@v2
        with:
          client-id:       ${{ env.AZURE_CLIENT_ID }}
          tenant-id:       ${{ env.AZURE_TENANT_ID }}
          subscription-id: ${{ env.AZURE_SUBSCRIPTION_ID }}
      - name: Deploy SPA
        uses: azure/webapps-deploy@v3
        with: { app-name: ${{ env.SPA_APP_NAME }}, package: spa }
      - name: Deploy API
        uses: azure/webapps-deploy@v3
        with: { app-name: ${{ env.API_APP_NAME }}, package: api }
```

Config placement summary:
- **Public, non-secret** (client IDs, tenant ID, API scope, authority): GitHub Actions **variables** (`vars.*`), baked into SPA build / API app settings. These are not credentials.
- **Secret/cert (API confidential client):** **Key Vault**, referenced from `croesus-api` app settings via `@Microsoft.KeyVault(...)`. **Never** in the workflow, repo, or `.bicepparam`.
- **Deploy auth:** OIDC FIC — no secret.

### 3. Evidence / logging strategy (proof of no token replay)

Three complementary evidence layers:

#### 3a. Backend structured logging (PRIMARY, most convincing) → Application Insights

On every request the `croesus-api` should log (to App Insights via the App Insights SDK / OpenTelemetry) the **decoded, non-sensitive claims** of BOTH legs. Log claims only — never the raw token strings.

- **Leg 1 (inbound SPA→API token A):** `aud` (must == API client ID / App ID URI), `scp` (must contain the API's delegated scope, e.g. `access_as_user`), `appid`/`azp` (must == SPA client ID), `oid`/`upn`, `iss`, `iat`, `jti`. The assertion that `aud == API` and `scp == API scope` proves the SPA token was minted **for the API**, not a Graph token being smuggled.
- **OBO call:** log the **request shape** — `grant_type=jwt-bearer`, `requested_token_use=on_behalf_of`, target `scope=https://graph.microsoft.com/User.Read`, and that the credential used was the **API's certificate/secret** (log the credential thumbprint/kid, never the secret).
- **Leg 2 (outbound API→Graph token B):** `aud` (must == `https://graph.microsoft.com` or `00000003-0000-0000-c000-000000000000`), `scp` (Graph scope), `iat`/`jti` **different from token A** (proves freshly issued, not replayed), and that token B's `aud` ≠ API and ≠ SPA.
- **Negative-test evidence:** record a deliberately-failed attempt where a Graph-audience token is presented to the API (API returns 401 because `aud` ≠ API) and where token A is sent directly to Graph (Graph 401 because `aud` ≠ Graph). This is the strongest "you cannot just replay an unbound token" demonstration.

> Do not validate/parse tokens for APIs you don't own (Graph). The OBO doc warns Graph tokens may be in a non-JWT/encrypted format. For the **inbound API token A** (an API you DO own) decoding/validating claims is correct and expected. For the **Graph token B**, log only what your own MSAL result object reports (scopes, expiry, the fact a new token was acquired) rather than cracking the Graph JWT.

#### 3b. Entra ID sign-in logs (CORROBORATING) → Log Analytics / KQL

- The OBO middle-tier leg is a **service-to-service / non-interactive** sign-in and lands in **`AADNonInteractiveUserSignInLogs`** (the SPA's interactive login lands in `SigninLogs`). Sign-in logs doc (ms.date 2025-11-07) confirms four log types incl. **Non-interactive user sign-ins**.
- For each leg you can show **Application** (SPA vs API), **Resource** (`ResourceDisplayName` = the API for leg 1, "Microsoft Graph" for leg 2), correlation ID linking them, and that the **resource of the API→Graph row is Microsoft Graph** while the **resource of the SPA→API row is the API** — i.e., distinct tokens for distinct audiences, not one token reused.
- **Token Protection bound/unbound (Concept A)**: field **"Token Protection - Sign In Session"** in the sign-in detail **Basic Info** pane (Bound / Unbound + `signInSessionStatusCode`). In Log Analytics this is the **`TokenProtectionStatusDetails`** column with `signInSessionStatus` / `signInSessionStatusCode`. As noted in the framing correction, this signal is **for native apps on registered devices accessing EXO/SPO/Teams** and will generally **not** be populated for a browser SPA + custom API + Graph flow — so use it only as a clearly-labeled "advanced / device-bound session" exhibit, not the OBO proof.

**KQL — show the two legs / correlate (corroborating exhibit):**

```kusto
// Interactive SPA sign-in (leg 0: user → SPA)
SigninLogs
| where TimeGenerated > ago(1h)
| where AppDisplayName == "Croesus SPA"
| project TimeGenerated, CorrelationId, AppDisplayName, ResourceDisplayName, UserPrincipalName, Status

// Non-interactive OBO legs (API → API token, API → Graph)
AADNonInteractiveUserSignInLogs
| where TimeGenerated > ago(1h)
| where AppDisplayName == "Croesus API" or ResourceDisplayName == "Microsoft Graph"
| project TimeGenerated, CorrelationId, AppDisplayName, ResourceDisplayName,
          UserPrincipalName, IncomingTokenType, ResourceIdentity, Status
| sort by TimeGenerated asc
```

**KQL — Token Protection bound vs unbound (Concept A, advanced/optional exhibit)** — adapted from the Token Protection Windows deployment guide sample:

```kusto
AADNonInteractiveUserSignInLogs
| where TimeGenerated > ago(7d)
| where TokenProtectionStatusDetails != ""
| extend d = parse_json(TokenProtectionStatusDetails)
| extend bindingStatus     = tostring(d["signInSessionStatus"])       // "Bound" / "Unbound"
| extend bindingStatusCode = tostring(d["signInSessionStatusCode"])   // 1002/1003/1005/1006/1008
| project TimeGenerated, UserPrincipalName, AppDisplayName, ResourceDisplayName,
          bindingStatus, bindingStatusCode
| sort by TimeGenerated desc
```

(For the Token-Protection-enforced/blocked variant, the guide's `mv-expand ConditionalAccessPolicies` + `enforcedSessionControls contains '["SignInTokenProtection"]'` query shows Block vs Allow. Note: the string changed from `Binding` → `SignInTokenProtection` in late June 2023; queries should match both.)

#### 3c. Microsoft Graph activity / sign-in resource

- The `croesus-api` calling `GET /v1.0/me` with token B appears as a **Microsoft Graph** sign-in (`ResourceDisplayName == "Microsoft Graph"`) in the non-interactive logs, on behalf of the user (`UserPrincipalName` preserved). This demonstrates the **delegated user identity flowed through** (OBO preserves the user; client-credentials would show an app-only sign-in with no user) — another "this is a real OBO, not a replay" signal.

### 4. Surfacing the evidence from the CI/CD run

Add a **post-deploy smoke-test / evidence job** that runs after deploy and emits artifacts + a step summary:

- **Smoke test:** exercise the SPA→API→Graph path against the freshly deployed environment (e.g., a Playwright headless login using a test user, or a service-principal-driven token acquisition), then call an API endpoint that returns the **decoded claim summary** (aud/scp/appid for both legs) the API logged. Print it to `$GITHUB_STEP_SUMMARY` and upload as an artifact.
- **Negative test in CI:** assert that presenting a Graph-audience token to the API yields **401**, and that the API token presented directly to Graph yields **401** — fail the job if either succeeds. This makes "no unbound replay" a **gated, repeatable** check, not a one-off screenshot.
- **Link to live evidence:** after `azure/login` (OIDC), the job can run `az monitor log-analytics query` to execute the correlation KQL and dump the two-leg rows into the step summary, and print deep links to the **App Insights** transaction search and the **Entra sign-in logs** filtered by correlation ID. Optionally run `az rest`/Graph query for the recent sign-in.
- **App Insights link:** since the API is on App Service with App Insights, the post-deploy job prints the portal URL to the App Insights resource (Transaction search filtered to the smoke-test operation) so the customer can click straight to the OBO claim logs.

---

## References

- On-Behalf-Of flow (grant_type jwt-bearer, requested_token_use=on_behalf_of, shared-secret First case + certificate Second case, aud-binding "can't redeem a token for a different app"). ms.date 2025-01-04. https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow
- Token Protection (Conditional Access session control; Bound/Unbound; native-apps-only; EXO/SPO/Teams). ms.date 2026-03-24. https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection
- Token Protection — Windows deployment guide (sign-in log field "Token Protection - Sign In Session"; unbound status codes 1002/1003/1005/1006/**1008** = not integrated with platform broker/WAM; `TokenProtectionStatusDetails` / `signInSessionStatusCode`; Log Analytics KQL samples; `Binding`→`SignInTokenProtection` rename June 2023). ms.date 2026-03-24. https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows
- Static Web Apps — API support overview (managed Functions vs bring-your-own App Service/Container Apps/APIM; `/api` prefix; 45s limit; Standard plan for BYO). ms.date 2024-10-02. https://learn.microsoft.com/en-us/azure/static-web-apps/apis-overview
- Authenticate to Azure from GitHub Actions by OpenID Connect (FIC on Entra app or UAMI; `id-token: write`; `azure/login@v2`; client/tenant/subscription IDs; audience `api://AzureADTokenExchange`). ms.date 2024-07-01. https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect
- App Service — Use Key Vault references as app settings (`@Microsoft.KeyVault(SecretUri=...)`; system/user-assigned MI; Key Vault Secrets User RBAC / Get policy; rotation). ms.date 2026-04-09. https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references
- Sign-in logs in Microsoft Entra ID (four log types incl. Non-interactive user sign-ins; Who/How/What = User/Application/Resource). ms.date 2025-11-07. https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-ins

(All URLs fetched 2026-06-29; ms.date = the doc's own last-updated date.)

## Clarifying Questions

1. **Token Protection (1008) framing:** Given that Entra Token Protection is native-app/EXO-SPO-Teams only and won't fire for a browser SPA + custom API + Graph OBO, do you want the deliverable to (a) re-anchor the "no replay" proof on **OBO audience binding** (recommended), (b) additionally build a *separate* native-app + EXO/SPO exhibit just to show a real bound/unbound 1008 signal, or (c) both? This changes scope materially.
2. **Tenant access for the demo:** Will the demo run in a tenant where you can enable **Entra ID P1** (required for Token Protection CA) and stream **sign-in logs to a Log Analytics workspace** (required for the KQL)? If not, the evidence narrows to App Insights backend logs + the Entra portal sign-in blade.
3. **API stack:** What language/runtime is the `croesus-api` middle tier (Node/.NET/etc.)? It changes the MSAL library, the OBO code sample, and the App Insights/OpenTelemetry logging snippet.
4. **Repo layout:** Does `devopsabcs-engineering/croesus` already contain the SPA and API source (the visible workspace only shows `assets/*.md` analysis docs)? Confirm folder structure so the workflow paths (`./spa`, `./api`) are accurate.
5. **One app vs two:** Preference for two App Service Web Apps (`croesus-spa` + `croesus-api`) vs a single app serving both, vs the simpler SWA+Functions alternative?
6. **App registrations:** Are the three Entra app registrations (SPA public client, API confidential client with exposed scope + Graph delegated perm, deploy FIC identity) already created (the workspace has `app-registration-analysis*.md`), or does the plan need to include creating them?

---

## Recommended Next Research (not completed this session)

- [ ] Confirm exact **App Insights / OpenTelemetry** code to log decoded JWT claims for the chosen API runtime (Node `@azure/monitor-opentelemetry` vs .NET `Microsoft.ApplicationInsights`), and a safe claim-redaction helper.
- [ ] Pull the **MSAL confidential-client OBO code sample** for the chosen runtime (e.g., `acquireTokenOnBehalfOf` in `@azure/msal-node`, or `AcquireTokenOnBehalfOf` in MSAL.NET) with certificate-from-Key-Vault loading.
- [ ] Verify the current **`azure/webapps-deploy`** and **`azure/static-web-apps-deploy`** action major versions and whether the SWA deploy action supports OIDC (historically it used a deployment token).
- [ ] Confirm the **`AADNonInteractiveUserSignInLogs` schema columns** (`IncomingTokenType`, `ResourceIdentity`, `CrossTenantAccessType`) actually distinguish the OBO leg as desired — validate against a live tenant query rather than docs.
- [ ] Bicep/IaC for the App Service + Key Vault + Managed Identity + App Insights + diagnostic settings (sign-in logs → Log Analytics) so the evidence pipeline is reproducible.
- [ ] Decide and document a **break-glass negative test** that is safe to run in CI (forging an aud-mismatched token without leaking real tokens).
