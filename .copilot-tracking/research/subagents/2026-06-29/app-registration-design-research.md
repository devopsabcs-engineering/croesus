<!-- markdownlint-disable-file -->
# Subagent Research: Standards-Compliant OBO App Registration Design (Mock "Croesus / GPD Central")

**Status:** Complete
**Date:** 2026-06-29
**Researcher mode:** Researcher Subagent

## Research Scope

Research the exact Microsoft Entra ID app registration design needed for a standards-compliant On-Behalf-Of (OBO) flow demo: a SPA front end + a confidential-client middle-tier Web API that calls Microsoft Graph via OBO. This is for a MOCK SaaS app modeling "Croesus / GPD Central". The mock must show the CORRECT registrations, contrasting against the REAL broken ones (SPA-only public clients, only Graph `User.Read` delegated, no secret/cert, no exposed API scope — token replay only, not real OBO).

### Research Questions / Topics

1. The two app registrations (SPA public client + middle-tier confidential Web API) with exact portal steps AND az CLI / Microsoft Graph commands to create them, expose the scope (`access_as_user` + Application ID URI), add a secret/cert, and grant admin consent.
2. `knownClientApplications` / pre-authorized applications so the SPA calls the API scope without an extra consent prompt.
3. Single-tenant vs multi-tenant decision for vendor SaaS (Croesus vendor providing a setup guide; Desjardins owns the registrations in their tenant and does SSO). Demo recommendation + real-world multi-tenant onboarding pattern.
4. Required Microsoft Graph delegated permissions + admin consent, and how to verify a registration can actually perform OBO vs the broken state.
5. A ready-to-run provisioning script (az CLI / Microsoft Graph) for reproducible CI provisioning.

## Baseline: The Broken (REAL) State Being Contrasted

From workspace context files:

- assets/app-registration-analysis-findings.md
- assets/app-registration-analysis.md
- assets/app-registration-verification.md

Confirmed broken-state shape of the REAL registrations (appIds: dev-dev `713d6ede-38a2-45f4-8982-89ea4fcf1a7f`, dev-prod `e3e358ea-0aae-4f11-b866-00f76b1cf6c1`, prod-prod `92dd40a3-f7c2-42ff-9303-fff5928e195a`):

- **SPA platform only** (SPA redirect URI), public client — no confidential-client posture.
- **Microsoft Graph `User.Read` delegated only** — no downstream API permissions beyond profile read.
- **Empty `passwordCredentials` and `keyCredentials`** — no client secret, no certificate, no federated credential.
- **Empty `api.oauth2PermissionScopes`** — no exposed API scope, no Application ID URI.
- **Single-tenant** (`signInAudience = AzureADMyOrg`).

**Consequence:** These can only do server-side **token replay** (forwarding the SPA's Graph token), which Token Protection flags as "unbound" (code 1008). They **cannot** perform standards OBO because OBO requires (a) a confidential client credential to sign the token-exchange request and (b) an exposed API scope so the SPA token has `aud` = the API (not Graph). The broken registrations have neither.

## Key Findings

### Topic 1 — The Two App Registrations (CORRECT design)

Standards OBO needs **two** registrations. The official protocol/diagram and middle-tier request requirements are in the OBO reference. ([OBO flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow), ms.date 2025-01-04)

#### Registration A — SPA front end (public client)

- **Platform:** Single-page application; redirect URI e.g. `https://localhost:3000` (auth code + PKCE; no implicit, no client secret).
- **API permission:** delegated permission to **Registration B's** exposed scope (e.g. `api://<api-app-id>/access_as_user`). Plus the default Microsoft Graph `User.Read` (`openid`, `profile` as needed for sign-in).
- **No credentials** — SPAs are public clients; the SPA never does OBO itself. Per the OBO reference: *"In the case of Single-page apps (SPAs), they should pass an access token to a middle-tier confidential client to perform OBO flows instead."*

#### Registration B — middle-tier Web API (confidential client) — the piece the broken apps lack

Three things make it OBO-capable:

1. **Exposed API + scope.** Set Application ID URI (`api://<app-id>` or a verified-domain URI) and add a delegated scope `access_as_user` (Who can consent: Admins and users for a demo). The SPA requests a token whose `aud` = this API. ([Expose a web API](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-configure-app-expose-web-apis), ms.date 2025-05-14)
2. **A confidential-client credential** — client **secret** or **certificate**. Required to sign the OBO token-exchange (`grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, `requested_token_use=on_behalf_of`). The OBO reference shows both the shared-secret (`client_secret`) and certificate (`client_assertion` + `client_assertion_type`) variants of the middle-tier token request.
3. **Downstream Graph delegated permission** — `User.Read` (the API calls `GET https://graph.microsoft.com/v1.0/me` with `scope=https://graph.microsoft.com/user.read offline_access`).

> Constraint worth noting in the mock: a middle-tier API in OBO **cannot** use a custom token-signing key. Per the OBO reference: *"applications with custom signing keys can't be used as middle-tier APIs in the OBO flow. This includes enterprise applications configured for single sign-on."* This is directly relevant because the REAL Croesus apps are tied to enterprise-application SSO — reinforcing why they are the wrong shape for OBO and why a clean, separate confidential API registration is the correct mock.

#### Exact CLI to build Registration B's scope

The cleanest reproducible way is `az ad app create` then an `az rest PATCH` against Microsoft Graph to set `api.oauth2PermissionScopes` with `value: access_as_user` (verified pattern from the MCP-on-Entra docs). ([Container Apps MCP auth](https://learn.microsoft.com/azure/container-apps/mcp-authentication#standalone-container-app-with-microsoft-entra-id-authentication))

```bash
# Registration B (API)
API_ID=$(az ad app create --display-name "Croesus GPD Central API (mock)" \
  --sign-in-audience AzureADMyOrg --query appId -o tsv)
az ad sp create --id "$API_ID"

# Application ID URI
az ad app update --id "$API_ID" --identifier-uris "api://$API_ID"

# Expose access_as_user scope (PATCH because az ad app has no first-class scope cmd)
API_OBJ=$(az ad app show --id "$API_ID" --query id -o tsv)
SCOPE_ID=$(uuidgen)
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/$API_OBJ" \
  --headers "Content-Type=application/json" \
  --body "{\"api\":{\"oauth2PermissionScopes\":[{\"id\":\"$SCOPE_ID\",\"adminConsentDescription\":\"Access the Croesus GPD Central API as the signed-in user\",\"adminConsentDisplayName\":\"Access Croesus GPD Central API\",\"isEnabled\":true,\"type\":\"User\",\"userConsentDescription\":\"Access the Croesus GPD Central API on your behalf\",\"userConsentDisplayName\":\"Access Croesus GPD Central API\",\"value\":\"access_as_user\"}]}}"

# Confidential-client credential (secret). Use cert in production.
az ad app credential reset --id "$API_ID" --append --years 1
# Downstream Graph User.Read (delegated) on the API
az ad app permission add --id "$API_ID" \
  --api 00000003-0000-0000-c000-000000000000 \
  --api-permissions e1fe6dd8-ba31-4d61-89e7-88639da4683d=Scope   # User.Read delegated
az ad app permission admin-consent --id "$API_ID"
```

Notes:
- `00000003-0000-0000-c000-000000000000` is the well-known Microsoft Graph app ID; `e1fe6dd8-ba31-4d61-89e7-88639da4683d` is the `User.Read` delegated permission ID (stable, used across Microsoft docs samples).
- `az ad app credential reset --append --years 1` adds a client secret without wiping existing creds. ([Digital Twins how-to](https://learn.microsoft.com/azure/digital-twins/how-to-create-app-registration#collect-important-values))
- For a certificate instead: register a `.cer` via `az ad app credential reset --id "$API_ID" --cert @cert.pem --append` (cert variant). Cert is the production-grade choice; secret is fine for the demo.

### Topic 2 — `knownClientApplications` / pre-authorized applications (no extra consent prompt)

Two complementary mechanisms, both on **Registration B (the API)**:

1. **`preAuthorizedApplications`** — the API declares Registration A (the SPA) as pre-authorized for the `access_as_user` scope. Result: the SPA gets the API scope **without any consent prompt**. Per the expose-API quickstart: *"To suppress prompting for consent by users of your app to the scopes you've defined, you can pre-authorize the client application... users won't be prompted for their consent."*
2. **`knownClientApplications`** — the API lists the SPA's appId so a **single combined consent** (using the `.default` scope) covers both the client→API permission and the API→Graph permission at once. Per the OBO reference: *"The middle tier application adds the client to the knownClientApplications list... the consent screen shows permissions for both the client to the middle tier API, and also... permissions required by the middle-tier API."*

CLI to set both on Registration B (via Graph PATCH):

```bash
SPA_ID=$(az ad app show --id "$SPA_APP_ID" --query appId -o tsv)
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/$API_OBJ" \
  --headers "Content-Type=application/json" \
  --body "{
    \"api\": {
      \"knownClientApplications\": [\"$SPA_ID\"],
      \"preAuthorizedApplications\": [
        { \"appId\": \"$SPA_ID\", \"delegatedPermissionIds\": [\"$SCOPE_ID\"] }
      ]
    }
  }"
```

> Important consent gotcha from the OBO reference: with `.default` combined consent you must **not** mix `.default` with other delegated scopes like `User.Read` in the same request, or you get `AADSTS70011`. `offline_access` may be combined with `.default` to get refresh tokens; split other token requests.

### Topic 3 — Single-tenant vs Multi-tenant (vendor SaaS framing)

**Framing:** Croesus (vendor) provides a setup guide; Desjardins (customer/outsider) owns the registrations in their own tenant and does SSO. This maps onto Microsoft's documented "third-party API where the customer creates/consents the app in their own tenant" model.

**Demo recommendation: single-tenant** (`signInAudience = AzureADMyOrg`). It is the simplest correct shape, and the Microsoft docs explicitly call this out for the "build your own app registration for a third party" case. From the tenancy doc, "Accounts in this directory only" is recommended *"if building an app registration for a third party that instructs you to build your own app registration for their app."* ([Single and multitenant apps](https://learn.microsoft.com/en-us/entra/identity-platform/single-and-multi-tenant-apps), ms.date 2025-03-13). This is exactly the Croesus→Desjardins relationship: the customer stands up the registrations in their tenant. The two registrations both live in the customer tenant; the SPA→API and API→Graph relationships are all intra-tenant, so OBO works without any cross-tenant provisioning.

**Real-world multi-tenant SaaS onboarding pattern** (document but do NOT use for the demo): a true vendor-hosted multi-tenant SaaS would register the app once in the vendor tenant (`signInAudience = AzureADMultipleOrgs`), use `/common` or `/organizations` authority, validate multiple issuers, and onboard each customer via the **admin-consent endpoint** which provisions the service principal in the customer tenant. ([Convert to multitenant](https://learn.microsoft.com/en-us/entra/identity-platform/howto-convert-app-to-be-multi-tenant), ms.date 2024-11-13)

Admin-consent onboarding URL (provisions the SP + consent in the customer tenant):

```text
https://login.microsoftonline.com/{tenant-or-organizations}/v2.0/adminconsent
  ?client_id=<api-or-client-app-id>
  &scope=https://graph.microsoft.com/.default
  &redirect_uri=<your-redirect>
  &state=12345
```

([Admin consent protocols](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent), ms.date 2023-11-08). Key multi-tenant subtlety from the convert doc: for a multi-tier app, *all resources the client requests must already exist in the customer tenant* — so the API's SP must be provisioned (via admin consent) before the SPA can get tokens for it. For multitenant, the **Application ID URI must be globally unique** (e.g. `https://<verified-domain>/croesus-api`, not `api://<guid>` necessarily) — single-tenant has no such constraint, another reason single-tenant is simpler for the demo.

**Verdict for the mock:** single demo tenant, both registrations single-tenant. Add a short "in a real SaaS this would be multi-tenant with admin-consent onboarding" callout so the demo teaches both the correct OBO shape AND the realistic vendor onboarding model.

### Topic 4 — Required Graph permissions, admin consent, and OBO verification

**Graph delegated permission required on the API (Registration B):** `User.Read` (delegated) at minimum — that is the downstream resource the OBO sample calls (`GET /me`). Add more delegated Graph scopes (e.g. `Mail.Read`) only as the demo needs them. Admin consent is granted on the **API** registration so the OBO exchange does not trigger interactive consent: *"A tenant admin can guarantee that applications have permission to call their required APIs by providing admin consent for the middle tier application."* (OBO reference, "Admin consent"). CLI: `az ad app permission admin-consent --id "$API_ID"`, or portal **API permissions → Grant admin consent**. ([Configure client to access web API](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-configure-app-access-web-apis), ms.date 2025-01-27)

**How to verify a registration can actually do OBO (vs the broken state):**

| Check | OBO-capable (CORRECT) | Broken (REAL) |
| --- | --- | --- |
| Exposed scope | `api.oauth2PermissionScopes` has `access_as_user`; Application ID URI set | empty |
| Confidential credential | `passwordCredentials` OR `keyCredentials` non-empty | both empty |
| Downstream perm | Graph `User.Read` delegated + admin consent | `User.Read` present but app is a public SPA, no API audience to exchange |
| Platform | Web (confidential) for the API; SPA only for the front end | SPA-only public client |
| SPA→API wiring | `preAuthorizedApplications` / `knownClientApplications` set on API | none |

Verification commands (extend the existing assets/app-registration-verification.md style):

```bash
# Exposed scopes (should list access_as_user on the API)
az ad app show --id "$API_ID" --query "api.oauth2PermissionScopes[].value" -o tsv
# Application ID URI
az ad app show --id "$API_ID" --query "identifierUris" -o tsv
# Confidential credential present?
az ad app show --id "$API_ID" --query "{secrets:passwordCredentials, certs:keyCredentials}" -o json
# Pre-authorized SPA + known client app
az ad app show --id "$API_ID" --query "api.preAuthorizedApplications" -o json
az ad app show --id "$API_ID" --query "api.knownClientApplications" -o json
# Downstream Graph delegated perms + admin-consent grants
az ad app permission list --id "$API_ID" -o table
az ad app permission list-grants --id "$API_ID" -o table
```

A positive functional test: run the OBO token request against `https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token` with `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, `requested_token_use=on_behalf_of`, `assertion=<SPA-issued token whose aud = API>`, `client_id=<API>`, `client_secret=<secret>`, `scope=https://graph.microsoft.com/user.read offline_access`. A 200 with an `access_token` for Graph proves OBO works; the broken apps cannot even produce a token with `aud=API` to use as the assertion.

### Topic 5 — Ready-to-run provisioning script (CI-reproducible)

Synthesized from the verified snippets above (Container Apps MCP auth PATCH pattern + az ad app create/credential/permission patterns). Single-tenant demo. Idempotency note: `az ad app create` is not idempotent on re-run (creates duplicates); for true CI, look up by display name first or store the appIds as outputs.

```bash
#!/usr/bin/env bash
set -euo pipefail

GRAPH_APP_ID="00000003-0000-0000-c000-000000000000"
GRAPH_USER_READ="e1fe6dd8-ba31-4d61-89e7-88639da4683d"   # delegated User.Read
SPA_REDIRECT="https://localhost:3000"

# 1) Middle-tier API (confidential client)
API_ID=$(az ad app create --display-name "Croesus GPD Central API (mock)" \
  --sign-in-audience AzureADMyOrg --query appId -o tsv)
az ad sp create --id "$API_ID"
az ad app update --id "$API_ID" --identifier-uris "api://$API_ID"
API_OBJ=$(az ad app show --id "$API_ID" --query id -o tsv)
SCOPE_ID=$(uuidgen)

# 2) SPA (public client)
SPA_ID=$(az ad app create --display-name "Croesus GPD Central SPA (mock)" \
  --sign-in-audience AzureADMyOrg --query appId -o tsv)
az ad sp create --id "$SPA_ID"
SPA_OBJ=$(az ad app show --id "$SPA_ID" --query id -o tsv)
# SPA platform redirect URI (spa, not web)
az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$SPA_OBJ" \
  --headers "Content-Type=application/json" \
  --body "{\"spa\":{\"redirectUris\":[\"$SPA_REDIRECT\"]}}"

# 3) Expose access_as_user + pre-authorize the SPA + known client app (on the API)
az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$API_OBJ" \
  --headers "Content-Type=application/json" \
  --body "{\"api\":{\"oauth2PermissionScopes\":[{\"id\":\"$SCOPE_ID\",\"adminConsentDescription\":\"Access Croesus GPD Central API as the signed-in user\",\"adminConsentDisplayName\":\"Access Croesus GPD Central API\",\"isEnabled\":true,\"type\":\"User\",\"userConsentDescription\":\"Access the Croesus GPD Central API on your behalf\",\"userConsentDisplayName\":\"Access Croesus GPD Central API\",\"value\":\"access_as_user\"}],\"knownClientApplications\":[\"$SPA_ID\"],\"preAuthorizedApplications\":[{\"appId\":\"$SPA_ID\",\"delegatedPermissionIds\":[\"$SCOPE_ID\"]}]}}"

# 4) Confidential credential on the API
API_SECRET=$(az ad app credential reset --id "$API_ID" --append --years 1 --query password -o tsv)

# 5) Downstream Graph User.Read (delegated) on the API + admin consent
az ad app permission add --id "$API_ID" --api "$GRAPH_APP_ID" --api-permissions "$GRAPH_USER_READ=Scope"
az ad app permission admin-consent --id "$API_ID"

# 6) SPA -> API delegated permission ("access_as_user") + admin consent
az ad app permission add --id "$SPA_ID" --api "$API_ID" --api-permissions "$SCOPE_ID=Scope"
az ad app permission admin-consent --id "$SPA_ID"

echo "API appId:    $API_ID"
echo "API scope:    api://$API_ID/access_as_user"
echo "SPA appId:    $SPA_ID"
echo "API secret:   (store securely; do not commit) $API_SECRET"
```

PowerShell (Microsoft.Graph) equivalent uses `New-MgApplication` / `Update-MgApplication` with the same `Api` (oauth2PermissionScopes, knownClientApplications, preAuthorizedApplications), `Spa.RedirectUris`, `Add-MgApplicationPassword`, and `New-MgServicePrincipalAppRoleAssignment` / `New-MgOauth2PermissionGrant` for consent. The az CLI path above is the recommended primary for CI because `az rest PATCH` against Graph is the most direct, well-documented way to set the scope/pre-auth fields (az CLI has no first-class `expose-api` subcommand for scopes).

## References

- [Microsoft identity platform and OAuth2.0 On-Behalf-Of flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow) — ms.date 2025-01-04. Protocol diagram, middle-tier token request (secret + cert), client limitations (SPA must use middle-tier confidential client; no custom signing key; enterprise-SSO apps excluded), consent (`knownClientApplications`, `preAuthorizedApplications`, admin consent, `.default`/`AADSTS70011`).
- [How to configure an application to expose a web API](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-configure-app-expose-web-apis) — ms.date 2025-05-14. Application ID URI, add scope (`access_as_user`-style), pre-authorize client to suppress consent.
- [Web API app registration and API permissions (configure client to access web API)](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-configure-app-access-web-apis) — ms.date 2025-01-27. Client → API delegated permission via "My APIs", Graph delegated perms, Grant admin consent button.
- [Single and multitenant apps in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/identity-platform/single-and-multi-tenant-apps) — ms.date 2025-03-13. Audience table; "build your own app registration for a third party" → single tenant.
- [Convert single-tenant app to multitenant](https://learn.microsoft.com/en-us/entra/identity-platform/howto-convert-app-to-be-multi-tenant) — ms.date 2024-11-13. `/common` vs `/organizations`, multi-issuer validation, `knownClientApplications` JSON, multi-tier multi-tenant onboarding (resource SP must exist in customer tenant first), globally-unique App ID URI requirement.
- [Microsoft identity platform admin consent protocols](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent) — ms.date 2023-11-08. `/v2.0/adminconsent` endpoint, `.default` scope, tenant provisioning of SP.
- [Container Apps MCP authentication (Entra)](https://learn.microsoft.com/azure/container-apps/mcp-authentication) — verified `az rest PATCH` pattern to set `api.oauth2PermissionScopes` with `value: access_as_user`.
- [Deploy remote MCP server On-Behalf-Of](https://learn.microsoft.com/azure/developer/azure-mcp-server/how-to/deploy-remote-mcp-server-on-behalf-of) — `az ad app permission add --api <downstream> --api-permissions <id>=Scope` for downstream OBO permissions.
- [Azure Digital Twins app registration](https://learn.microsoft.com/azure/digital-twins/how-to-create-app-registration) — `az ad app credential reset --append` secret pattern.
- Workspace baseline (broken state): assets/app-registration-analysis-findings.md, assets/app-registration-analysis.md, assets/app-registration-verification.md.

## Recommended Next Research (not completed this session)

- [ ] Exact certificate path for Registration B (`az ad app credential reset --cert` vs federated identity credential `--create-cert`) and how a cert changes the OBO request (`client_assertion`).
- [ ] MSAL.js (SPA) + MSAL middle-tier (Node/.NET) code wiring for the demo app, including the WWW-Authenticate claims-challenge handling the OBO reference describes for CA/MFA step-up.
- [ ] Idempotent CI variant of the provisioning script (look-up-or-create by display name; store appIds/secret in Key Vault or GH Actions secrets) and a teardown script.
- [ ] Conditional Access interaction: how Token Protection / device-compliance behaves with correct OBO vs the broken token-replay (ties back to the Croesus "unbound / 1008" finding) — confirm OBO produces a bound token.
- [ ] Optional `App Roles` vs delegated scope variant if any service-to-service (app-only) leg is desired (OBO is delegated-only; roles would need client-credentials flow).

## Clarifying Questions

1. Should the mock be built as runnable code (MSAL.js SPA + Node/.NET middle-tier API) in this repo, or is the goal only the **registration design + provisioning artifacts** (CLI/scripts/docs) to contrast against the broken state?
2. Demo tenant: single-tenant in one demo tenant (recommended) confirmed, or do you also want a worked multi-tenant onboarding walkthrough (admin-consent endpoint) as a second artifact?
3. Credential preference for Registration B in the demo: **client secret** (simplest) or **certificate / federated identity credential** (production-grade)?
