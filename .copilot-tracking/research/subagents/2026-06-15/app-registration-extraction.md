<!-- markdownlint-disable-file -->
# Subagent Research: App Registration Extraction (Croesus)

## Research Topics / Questions

1. Capture the analysis methodology, questions, expected output, and dev/prod context from assets/app-registration-analysis.md.
2. Extract full config from each of the three exports: assets/dev-dev.txt, assets/dev-prod.txt, assets/prod-prod.txt.
3. Identify security concerns per export (credentials, redirect URIs, permissions, audience, implicit grant, etc.).
4. Verify the source-env / target-env filename naming hypothesis against file contents and the analysis doc matrix.
5. Confirm no other relevant files exist in assets/.

## Status

Complete (text assets fully read). Two PDFs and one DOCX in assets/ are binary and were NOT read — flagged as potentially relevant context.

---

## 1. README.md

- Workspace root README.md contains only a single line: `# croesus` (line 1). No additional repo context available.

## 2. Methodology — assets/app-registration-analysis.md

The document is NOT a step-by-step technical runbook. It is an intent/scoping document that frames **what the customer wants verified** and **what decision they are trying to make**. The whole file is ~199 lines of markdown (lines 200+ are empty).

### Core intent (lines 1-13)

The customer wants to determine whether the **current Azure App Registration design and configuration is correct and expected** across combinations of:
- Tenant environments (Dev vs Prod Entra ID tenants)
- Croesus application environments (Dev / UAT vs Prod SaaS)
- Authentication flows (interactive vs non-interactive)

Most critical question: whether the observed **"second non-interactive sign-in from SaaS (AWS IP)"** behaviour is **expected OR misconfigured**.

### Environment matrix the doc defines (lines 17-30)

| Entra Tenant        | Croesus Environment | Scenario Name | Concern                             |
| ------------------- | ------------------- | ------------- | ----------------------------------- |
| Dev tenant (MVTDev) | Dev / UAT Croesus   | dev-dev       | Baseline behaviour validation       |
| Prod tenant         | Dev/UAT Croesus     | prod-dev      | Cross-environment security boundary |
| Prod tenant         | Prod Croesus        | prod-prod     | Expected working reference          |

> NOTE: the doc's matrix uses the label **`prod-dev`** for the cross-environment case, but the supplied file is named **`dev-prod.txt`**. See Section 6 (naming hypothesis) — these refer to the same scenario with token order reversed.

### Verification dimensions the doc enumerates (lines 32-150)

1. Environment matrix validity — are dev-dev / prod-dev / prod-prod valid patterns; should behaviour differ; is token/security behaviour consistent.
2. App Registration correctness — confirm all apps are **SPA** + **OIDC/OAuth (not SAML)**; redirect URIs properly scoped per env; whether separate registrations per env are warranted. Doc suspects **dev has residual/test config (extra redirect URIs, federation endpoints)** and prod is cleaner.
3. Token / auth flow validation (most critical) — first sign-in interactive from corporate IP, device compliant; second sign-in non-interactive from **Croesus AWS IP**, reuses user+device token, blocked by Conditional Access. Wants to know if this is expected token-replay / on-behalf-of (OBO) for SPA, or a config/flow/SaaS-design fault.
4. Device compliance + Conditional Access — is CA behaving correctly (doc assumes likely yes); should the second request even exist.
5. Cross-tenant + hybrid identity — devices managed in **Prod tenant (Intune / Entra hybrid join)** but auth happens in **Dev tenant** for non-prod Croesus; device compliance cannot be trusted cross-tenant so CA fails in dev tenant. Is prod-managed-device-against-dev-tenant a valid pattern.
6. Identity & federation config differences — presence of **SiteMinder federation redirect in Dev**, multiple redirect URIs in Dev, clean vs noisy configs; do these influence auth flows.

### Underlying question + decision (lines 152-199)

> "Is our architecture and configuration correct, or are we compensating for a design flaw (either ours or Croesus)?"

Decision branches:
- Option A — internal fix (fix app reg config, align environments, adjust CA/architecture).
- Option B — push vendor Croesus (stop token reuse / second sign-in), but hesitant without certainty.

### Expected output / scoring

- The doc prescribes **no explicit output template, no numeric scoring, and no formal risk rubric.** It is qualitative. The implied deliverable is a determination: expected-behaviour-needing-policy-change vs misconfiguration, plus a fix-internally-vs-escalate-to-vendor recommendation.

---

## 3. Export format (all three .txt files)

- All three are **single pretty-printed JSON objects** representing a **Microsoft Graph `application` resource** (i.e. `az ad app show` / Graph `GET /applications/{id}` style — the Application object, NOT the service principal, despite the `sp-` display-name prefix).
- Each is a complete object (no truncation; file ends cleanly with closing brace).
- Common shape across all three:
  - `signInAudience: "AzureADMyOrg"` (single tenant) on all three.
  - `createdByAppId: "18ed3507-a475-4ccb-b669-d66bc9f2a36e"` on all three (consistent provisioning/automation app — same creator).
  - `requiredResourceAccess`: Microsoft Graph (`00000003-0000-0000-c000-000000000000`) with one delegated **Scope** `e1fe6dd8-ba31-4d61-89e7-88639da4683d` (= **User.Read** delegated). Admin consent state is NOT present in this export (application object does not include consent grants).
  - `keyCredentials: []`, `passwordCredentials: []` → **no certificates, no client secrets** on any app. **No federated identity credentials block present** either.
  - `appRoles: []`, `api.oauth2PermissionScopes: []`, `identifierUris: []` → **no exposed API, no app roles, no scopes published**.
  - `publicClient.redirectUris: []`, `web.redirectUris: []` → redirect URIs live ONLY under `spa.redirectUris` → all three are **SPA-type** registrations (auth-code + PKCE, no secret required).
  - `api.acceptMappedClaims: true` on all three.
  - `servicePrincipalLockConfiguration.isEnabled: true` (allProperties true) on all three (good hygiene — prevents credential tampering).
  - No `owners`, no `tags` (empty array), no `notes`, no `verifiedPublisher` on any.
  - `tokenEncryptionKeyId: null` on all.

---

## 4. Per-file extraction

### assets/dev-dev.txt — `sp-CentralGPD-UAT-dev-fed`

- objectId (`id`): `0110690a-75b6-4809-9c9a-956ab805434b` (line 2)
- appId (clientId): `713d6ede-38a2-45f4-8982-89ea4fcf1a7f` (line 4)
- displayName: `sp-CentralGPD-UAT-dev-fed` (line 9)
- createdDateTime: `2025-07-09T20:25:43Z` (line 7)
- **publisherDomain: `MVTDEVDesjardins.onmicrosoft.com`** (line 18) → **DEV Entra tenant**
- signInAudience: AzureADMyOrg (single tenant)
- SPA redirectUris (2):
  1. `https://spsfondation.dev.desjardins.com/affwebservices/tools/oidc-tool.html` — **SiteMinder federation endpoint** (`affwebservices` is CA SiteMinder; `oidc-tool.html`). Dev domain.
  2. `https://pat-gpd-central.certif.desjardins.com/CentralWebApp/LogonSso.aspx` — non-prod (`pat`/`certif`) Central web app SSO logon.
- implicitGrantSettings: `enableIdTokenIssuance: true`, `enableAccessTokenIssuance: false` (line ~75) → **ID-token implicit issuance ENABLED** (only this app has it on).
- Graph User.Read delegated; no secrets/certs/FIC; no exposed API/roles.

### assets/dev-prod.txt — `sp-CentralGPD-UAT-dev-fed` (SAME display name as dev-dev)

- objectId (`id`): `a2a7a9ad-8624-43d7-a921-a33d00c05c5b`
- appId (clientId): `e3e358ea-0aae-4f11-b866-00f76b1cf6c1` (**different appId from dev-dev** → distinct registration)
- displayName: `sp-CentralGPD-UAT-dev-fed` (identical name to dev-dev — naming collision / "UAT-dev" name living in prod tenant)
- createdDateTime: `2025-07-09T20:39:55Z` (~14 min after dev-dev, same day)
- **publisherDomain: `mvtdesjardins.onmicrosoft.com`** → **PROD Entra tenant** (lowercase, no `DEV`)
- signInAudience: AzureADMyOrg
- SPA redirectUris (1):
  1. `https://pat-gpd-central.certif.desjardins.com/CentralWebApp/LogonSso.aspx` (non-prod certif/pat endpoint — same as dev-dev's 2nd URI; the SiteMinder federation URI is ABSENT here).
- implicitGrantSettings: `enableIdTokenIssuance: false`, `enableAccessTokenIssuance: false` (implicit fully off — tighter than dev-dev).
- Graph User.Read delegated; no secrets/certs/FIC; no exposed API/roles.

### assets/prod-prod.txt — `sp-CentralGPD-prod-fed`

- objectId (`id`): `f75b1dc5-6d7b-4a37-a353-caf805022eaf`
- appId (clientId): `92dd40a3-f7c2-42ff-9303-fff5928e195a`
- displayName: `sp-CentralGPD-prod-fed` (distinct `-prod-fed` naming)
- createdDateTime: `2025-04-25T15:06:29Z` (oldest — created ~2.5 months before the two dev-named apps)
- **publisherDomain: `mvtdesjardins.onmicrosoft.com`** → **PROD Entra tenant**
- signInAudience: AzureADMyOrg
- SPA redirectUris (1):
  1. `https://gpd-central.desjardins.com/CentralWebApp/LogonSso.aspx` — **clean production** Central web app SSO logon (no `pat`/`certif`/`dev`).
- implicitGrantSettings: `enableIdTokenIssuance: false`, `enableAccessTokenIssuance: false`.
- Graph User.Read delegated; no secrets/certs/FIC; no exposed API/roles.

### Quick comparison

| Field | dev-dev | dev-prod | prod-prod |
| --- | --- | --- | --- |
| displayName | sp-CentralGPD-UAT-dev-fed | sp-CentralGPD-UAT-dev-fed | sp-CentralGPD-prod-fed |
| appId | 713d6ede… | e3e358ea… | 92dd40a3… |
| publisherDomain (tenant) | MVTDEVDesjardins (DEV) | mvtdesjardins (PROD) | mvtdesjardins (PROD) |
| created | 2025-07-09 20:25 | 2025-07-09 20:39 | 2025-04-25 15:06 |
| SPA redirect count | 2 | 1 | 1 |
| SiteMinder fed redirect | YES | no | no |
| Redirect cleanliness | dev + certif/pat | certif/pat | clean prod |
| Implicit ID token | TRUE | false | false |
| Implicit access token | false | false | false |
| Secrets / certs / FIC | none | none | none |
| Exposed API / appRoles | none | none | none |
| Graph perms | User.Read (delegated) | User.Read (delegated) | User.Read (delegated) |
| signInAudience | AzureADMyOrg | AzureADMyOrg | AzureADMyOrg |

---

## 5. Security / configuration observations

1. **No credentials anywhere** — zero client secrets, zero certificates (`keyCredentials`), zero federated identity credentials on all three. Consistent with pure SPA public-client OIDC (auth-code + PKCE). POSITIVE finding; also see point 6 re: the OBO question. So there are no long-lived/expiring secret risks to report.
2. **dev-dev has ID-token implicit grant enabled** (`enableIdTokenIssuance: true`) while dev-prod and prod-prod have it off. Implicit ID-token issuance in dev = residual/test hygiene gap; directly corroborates the analysis doc's claim that "dev registrations contain residual/test configs."
3. **dev-dev carries an extra SiteMinder federation redirect URI** (`spsfondation.dev.desjardins.com/affwebservices/.../oidc-tool.html`) absent in the prod-tenant apps. Corroborates the doc's "SiteMinder federation redirect in Dev" observation. A federation/test tool redirect on a dev app is a potential token-redirection surface to scope/clean.
4. **Naming/labeling drift across tenant boundary** — `dev-prod.txt` is a `sp-CentralGPD-UAT-dev-fed` (UAT/dev-named) registration that physically lives in the **PROD** tenant (`mvtdesjardins`). A UAT-named app in the prod tenant is a governance/clarity concern and matches the customer's "environments currently partly mixed" suspicion.
5. **Redirect-URI environment scoping** — dev-dev/dev-prod both point at non-prod `pat-gpd-central.certif.desjardins.com`; prod-prod points at clean `gpd-central.desjardins.com`. Redirects are appropriately environment-scoped per registration (no prod URL leaking into dev or vice-versa within the SPA lists). All HTTPS; **no localhost / no http:// / no wildcard** redirects observed → no dangling-localhost-in-prod concern.
6. **No exposed API, no app roles, no oauth2PermissionScopes, no `knownClientApplications`** on any registration. **Implication for the customer's core question:** these registrations themselves cannot mint a downstream access token via On-Behalf-Of (they expose no API and hold no credential). Therefore the **"second non-interactive sign-in from the Croesus AWS IP" is almost certainly driven from the Croesus SaaS backend side, not by these Entra app registrations.** This is a strong evidence pointer toward Option B (vendor/Croesus behaviour) rather than an Entra app-registration misconfiguration — though full confirmation needs the sign-in logs / CA reports (see the PDFs in Section 7).
7. **All single-tenant (`AzureADMyOrg`)** — no multi-tenant / personal-account exposure. POSITIVE.
8. **`servicePrincipalLockConfiguration` enabled** on all three — good tamper protection.
9. **No owners recorded** in the export (Graph application object excludes owners by default; cannot assess ownership from these files — would need `az ad app owner list`).
10. `acceptMappedClaims: true` on all three — generally benign but worth noting for claims-mapping policy interactions.

---

## 6. Naming hypothesis verification

**Hypothesis given:** filenames encode a source-environment / target-environment relationship.

**Finding — the naming encodes `[Croesus app environment]-[Entra tenant]`, NOT source/target in the literal sense:**

| File | App (Croesus env from displayName) | Tenant (from publisherDomain) | Fits `[croesus]-[tenant]`? |
| --- | --- | --- | --- |
| dev-dev.txt | UAT/dev (`-UAT-dev-fed`) | DEV (`MVTDEVDesjardins`) | dev croesus + dev tenant ✓ |
| dev-prod.txt | UAT/dev (`-UAT-dev-fed`) | PROD (`mvtdesjardins`) | dev croesus + prod tenant ✓ |
| prod-prod.txt | prod (`-prod-fed`) | PROD (`mvtdesjardins`) | prod croesus + prod tenant ✓ |

- The contents **support** an environment-pairing naming scheme. The most consistent reading is **`<CroesusEnv>-<EntraTenant>`**.
- **Discrepancy vs the analysis doc matrix:** the doc labels the cross-environment row **`prod-dev`** (Prod tenant + Dev Croesus), but the file is named **`dev-prod`** (Dev Croesus + Prod tenant). **Same scenario, token order reversed.** The doc's `prod-dev` == the file's `dev-prod`. There is **no separate `prod-dev.txt`** and **no `dev-prod` row** in the doc — the two notations describe the identical cross-boundary combination (a Dev/UAT Croesus app registration sitting in the Prod Entra tenant).
- Cross-environment mismatch **confirmed**: `dev-prod.txt` is a **dev/UAT-named registration deployed in the production tenant** — the exact "cross-environment security boundary" case the customer flagged. This is the registration to scrutinize for the second-sign-in / CA-block investigation.

---

## 7. Other files in assets/ (NOT read — binary)

`list_dir` on assets/ returned, beyond the 4 text files, three additional files that look directly relevant to the same investigation but are binary and were not opened:

- `assets/croesus_entra_oauth_integration_report_20260529_185237.pdf` — almost certainly the Entra/OAuth integration analysis (relevant to the token-flow question).
- `assets/non_prod_sso_conditional_access_report_20260529_180104.pdf` — likely the Conditional Access / SSO sign-in evidence for the non-prod scenario (relevant to the "second sign-in blocked by CA" behaviour).
- `assets/PROD.docx` — likely prod-scenario notes/evidence.

These were out of the explicit read list but are clearly part of the same engagement and likely contain the sign-in logs / CA decisions needed to fully answer the "second non-interactive sign-in" question.

---

## Clarifying questions

1. Should the two PDFs and `PROD.docx` in assets/ be ingested as part of this analysis? They appear to hold the sign-in / Conditional Access evidence that the .txt app-registration exports alone cannot provide.
2. Is there expected to be a separate `prod-dev.txt` (the doc's matrix label), or is `dev-prod.txt` the canonical artifact for that cross-boundary row? (Contents indicate the latter.)
3. Are admin-consent / oauth2PermissionGrant states and app owners needed? They are not present in these application-object exports and would require additional Graph queries.

## Recommended next research (not completed this session)

- [ ] Extract text/tables from `assets/non_prod_sso_conditional_access_report_20260529_180104.pdf` to confirm the second sign-in IP (AWS), the device-compliance evaluation, and the CA policy that blocked it.
- [ ] Extract `assets/croesus_entra_oauth_integration_report_20260529_185237.pdf` to confirm the OAuth flow Croesus actually performs (auth-code+PKCE vs OBO vs token replay).
- [ ] Extract `assets/PROD.docx` for the prod reference behaviour.
- [ ] Confirm `e1fe6dd8-ba31-4d61-89e7-88639da4683d` = Microsoft Graph `User.Read` delegated (well-known ID; high confidence) and capture admin-consent state via `az ad app permission list-grants` / Graph oauth2PermissionGrants.
- [ ] Pull app owners (`az ad app owner list`) for each of the three appIds to close the ownership/governance gap.
- [ ] Confirm tenant IDs behind `MVTDEVDesjardins.onmicrosoft.com` (dev) and `mvtdesjardins.onmicrosoft.com` (prod) to make the cross-tenant device-compliance argument concrete.
