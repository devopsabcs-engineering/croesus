<!-- markdownlint-disable-file -->
# Subagent Research — Reversible Conditional Access Provisioning & the OBO Best-Practices Gap

**Date:** 2026-07-06
**Status:** Complete
**Scope (RESEARCH ONLY — no changes outside `.copilot-tracking/research/`):** Document how to (1) provision a Microsoft Entra Conditional Access (CA) token-protection policy via automation, (2) FULLY and reversibly tear down every tenant / app-registration change so the demo tenant (`MngEnvMCAP675646.onmicrosoft.com`, Entra ID P2) returns to its exact original state, and (3) enumerate the precise wrong-vs-right On-Behalf-Of (OBO) gap. All facts are cited to Microsoft Learn / Graph / az CLI docs.

---

## 0. TL;DR — the three most important discoveries

1. **Endpoint + schema correction.** The Graph path is `POST /identity/conditionalAccess/policies` (note the slash — it is `conditionalAccess/policies`, NOT `conditionalAccessPolicies` as the brief's shorthand implied). Token protection is a **session control**, not a grant control. Its Graph field is `sessionControls.secureSignInSession = { "isEnabled": true }` — and **this field exists only in the `beta` endpoint**, not in `v1.0`. So a token-protection CA policy must be created against `https://graph.microsoft.com/beta/identity/conditionalAccess/policies`. State is `enabled` | `disabled` | `enabledForReportingButNotEnforced` (report-only). ([create](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies), [beta sessionControls](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesssessioncontrols?view=graph-rest-beta), [secureSignInSessionControl](https://learn.microsoft.com/en-us/graph/api/resources/securesigninsessioncontrol?view=graph-rest-beta), [conditionalAccessPolicy](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy))

2. **Safest reversible pattern = a state file of created object ids + tag-by-prefix + guarded idempotent teardown.** Provision writes each created object id (CA policy id, oauth2PermissionGrant id, app object ids) to a JSON state file; teardown deletes exactly those ids (`DELETE /identity/conditionalAccess/policies/{id}`, `DELETE /oauth2PermissionGrants/{id}`), each guarded by an existence check so re-runs never error — mirroring the repo's existing [`teardown-app-registrations.sh`](../../../../scripts/teardown-app-registrations.sh) look-up-or-skip pattern. A unique `displayName` prefix (e.g. `croesus-demo-`) lets a "sweep" pass find and remove strays even if the state file is lost. Deletes return `204 No Content` and are safe to repeat.

3. **The wrong-vs-right OBO gap is exactly five missing pieces.** To move from server-side token replay (reuse the user's token against Graph) to compliant OBO, the middle tier must add: (a) a **confidential-client credential** (certificate preferred), (b) an **exposed API scope** (`access_as_user`) so leg-1's `aud` is the API (not Graph), (c) the **`on_behalf_of` grant with a `client_assertion`** signed by that credential, producing a **distinct leg-2 audience** and **fresh token** (new `jti`/`iat`), (d) **pre-authorization / `knownClientApplications`** so the SPA→API→Graph consent chain works, and (e) the **reject-the-token rule** — never redeem a token whose `aud` is not this API. Microsoft explicitly documents relaying the middle-tier's tokens as an anti-pattern. ([OBO flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))

---

## 1. Creating a token-protection Conditional Access policy via automation

### 1.1 The exact schema — token protection is `sessionControls.secureSignInSession`

A `conditionalAccessPolicy` has four top-level shaping properties: `displayName`, `state`, `conditions`, and either `grantControls` and/or `sessionControls`. A valid policy must contain at least one of an application rule, a user rule, or a grant/session control. ([conditionalAccessPolicy resource](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy), [create policy](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies))

- `state` (required) — one of `enabled`, `disabled`, `enabledForReportingButNotEnforced`. The last value is **report-only** mode. ([conditionalAccessPolicy resource](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy))
- `grantControls` — `{ operator: "AND"|"OR", builtInControls: [...] }` where `builtInControls` ∈ `block`, `mfa`, `compliantDevice`, `domainJoinedDevice`, `approvedApplication`, `compliantApplication`, `passwordChange`, `riskRemediation`. **Token protection is NOT a grant control** — it does not appear in this enum. ([conditionalAccessGrantControls](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccessgrantcontrols))
- `sessionControls` — the container that holds token protection. Its members are `applicationEnforcedRestrictions`, `cloudAppSecurity`, `disableResilienceDefaults`, `persistentBrowser`, `signInFrequency` (v1.0) plus `continuousAccessEvaluation`, **`secureSignInSession`**, and `globalSecureAccessFilteringProfile` (beta). ([v1.0 sessionControls](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesssessioncontrols), [beta sessionControls](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesssessioncontrols?view=graph-rest-beta))

**The token-protection control field:** `secureSignInSession` — "Session control to require sign in sessions to be bound to a device." Its only property is `isEnabled` (Boolean). This is the Graph representation of the portal control **"Require token protection for sign-in sessions"**. ([secureSignInSessionControl](https://learn.microsoft.com/en-us/graph/api/resources/securesigninsessioncontrol?view=graph-rest-beta))

```json
"sessionControls": {
  "secureSignInSession": { "isEnabled": true }
}
```

> **CRITICAL:** `secureSignInSession` is documented ONLY under `graph-rest-beta`. The v1.0 `conditionalAccessSessionControls` type does not list it. Therefore a token-protection policy must be created/updated against the **beta** endpoint: `https://graph.microsoft.com/beta/identity/conditionalAccess/policies`. (`signInFrequency` is unrelated — it enforces re-auth cadence, not device binding; do not conflate the two.)

### 1.2 Scoping the policy narrowly (avoid tenant-wide impact)

Scope via `conditions.users` and `conditions.applications`:

- **User/group scope:** `conditions.users.includeUsers: ["<test-user-object-id>"]` or `includeGroups: ["<demo-group-id>"]`. Always set an `excludeUsers` break-glass account. ([create policy examples](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies))
- **Cloud-app scope:** `conditions.applications.includeApplications: ["<app-id>"]`. Well-known resource app ids that token protection actually enforces on: Exchange Online = `00000002-0000-0ff1-ce00-000000000000`, SharePoint Online = `00000003-0000-0ff1-ce00-000000000000`, Microsoft Graph = `00000003-0000-0000-c000-000000000000` (see note in §1.5 on enforcement coverage). ([create policy examples](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies))
- Token protection currently supports native-application `clientAppTypes` only (browser not supported — see §1.5); a demo policy would set `clientAppTypes: ["mobileAppsAndDesktopClients"]`. ([token protection concept](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection))

### 1.3 Report-only vs enabled — recommend report-only for safe demoing

Microsoft's own token-protection deployment guidance says: **"Create a Conditional Access policy in report-only mode before enforcing token protection"**, start with a pilot group, and capture both interactive and non-interactive sign-in logs before enforcing. For a reversible customer demo, prefer `state: "enabledForReportingButNotEnforced"` — it emits the token-binding evaluation into the sign-in logs (the `1008`/unbound signal is a log signal, per the repo's own findings) without actually blocking anyone, which is both safer and easier to reverse. Flip to `enabled` only for the enforcement beat of the demo, then flip back or delete. ([token protection deployment](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection), [report-only mode](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-report-only))

### 1.4 Can this be done with `az rest`? Yes.

`az rest` auto-acquires a Microsoft Graph token when the `--uri` host is `graph.microsoft.com`, so no separate token dance is needed — this is exactly how the repo's [`provision-app-registrations.sh`](../../../../scripts/provision-app-registrations.sh) already PATCHes applications (`az rest --method PATCH --uri https://graph.microsoft.com/v1.0/applications/...`). The same works for CA policies against the **beta** path.

Concrete create (report-only token-protection policy, scoped to one test user + Exchange Online):

```bash
# Requires: signed-in principal holding Policy.ReadWrite.ConditionalAccess
#           (Conditional Access Administrator or Security Administrator role).
POLICY_ID="$(az rest --method POST \
  --uri "https://graph.microsoft.com/beta/identity/conditionalAccess/policies" \
  --headers "Content-Type=application/json" \
  --body '{
    "displayName": "croesus-demo-token-protection",
    "state": "enabledForReportingButNotEnforced",
    "conditions": {
      "clientAppTypes": ["mobileAppsAndDesktopClients"],
      "applications": { "includeApplications": ["00000002-0000-0ff1-ce00-000000000000"] },
      "users": {
        "includeUsers": ["<TEST_USER_OBJECT_ID>"],
        "excludeUsers": ["<BREAK_GLASS_ACCOUNT_ID>"]
      }
    },
    "sessionControls": {
      "secureSignInSession": { "isEnabled": true }
    }
  }' \
  --query id -o tsv)"
echo "Created CA policy: $POLICY_ID"   # persist this id — see §2
```

Read one / list all:

```bash
az rest --method GET --uri "https://graph.microsoft.com/beta/identity/conditionalAccess/policies/$POLICY_ID"
az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies?\$select=id,displayName,state" \
  --query "value[?starts_with(displayName,'croesus-demo-')].{id:id,name:displayName,state:state}" -o table
```

Toggle report-only ⇄ enabled ⇄ disabled (PATCH, returns `204`):

```bash
az rest --method PATCH \
  --uri "https://graph.microsoft.com/beta/identity/conditionalAccess/policies/$POLICY_ID" \
  --headers "Content-Type=application/json" \
  --body '{ "state": "enabled" }'
```

Endpoints and verbs (authoritative):

| Operation | Method + path | Success | Notes |
| --- | --- | --- | --- |
| Create | `POST /identity/conditionalAccess/policies` | `201 Created` + policy body (has `id`) | Use **beta** for `secureSignInSession`. ([create](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies)) |
| Get | `GET /identity/conditionalAccess/policies/{id}` | `200` | ([resource/methods](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy)) |
| List | `GET /identity/conditionalAccess/policies` | `200` (`value[]`) | Filter client-side by `displayName` prefix. |
| Update (enable/disable/report-only) | `PATCH /identity/conditionalAccess/policies/{id}` | `204 No Content` | Send only changed fields. ([update](https://learn.microsoft.com/en-us/graph/api/conditionalaccesspolicy-update)) |
| Delete | `DELETE /identity/conditionalAccess/policies/{id}` | `204 No Content` | Idempotent teardown target. ([delete](https://learn.microsoft.com/en-us/graph/api/conditionalaccesspolicy-delete)) |

> **PowerShell alternative:** `New-MgIdentityConditionalAccessPolicy` / `Update-...` / `Remove-...` (module `Microsoft.Graph.Identity.SignIns`) do the same; but since the repo is bash + Azure CLI + OIDC, `az rest` is the right fit and needs no extra module install. ([create policy — PowerShell tab](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies))

### 1.5 Required permissions / directory roles + enforcement caveat

- **Graph permissions (create/update/delete):** least-privileged = `Policy.Read.All` **and** `Policy.ReadWrite.ConditionalAccess` (delegated or application). There is a documented [known issue](https://learn.microsoft.com/en-us/graph/known-issues#conditional-access-policy-requires-consent-to-additional-permission) where the operation may require consent to additional permissions. ([create](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies), [update](https://learn.microsoft.com/en-us/graph/api/conditionalaccesspolicy-update))
- **Directory roles (delegated):** **Conditional Access Administrator** or **Security Administrator** (least privilege). ([create](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies))
- **Enforcement coverage caveat (validates repo WI-02):** Token Protection "currently supports **native applications only**. **Browser-based applications are not supported**." Enforced resources are **Exchange Online, SharePoint Online, Microsoft Teams** (plus Azure Virtual Desktop and Windows 365 on Windows). Platform status: Windows GA; iOS/iPadOS/macOS Preview. So a browser-SPA → custom-API → Graph shape will NOT trigger enforcement; the policy can be created and the unbound/`1008` signal read from logs, but observable blocking requires a native client hitting EXO/SPO/Teams. ([token protection concept](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection))

---

## 2. FULLY reversible teardown (the critical requirement)

### 2.1 Delete a CA policy by id

`DELETE https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies/{id}` → `204 No Content`. Same permissions/roles as create. ([delete](https://learn.microsoft.com/en-us/graph/api/conditionalaccesspolicy-delete))

```bash
az rest --method DELETE \
  --uri "https://graph.microsoft.com/beta/identity/conditionalAccess/policies/$POLICY_ID"
```

### 2.2 The robust idempotent state-file pattern

**Principle:** provision RECORDS every object it creates; teardown DELETES exactly those recorded ids, each delete guarded so a missing object is a no-op. This restores the tenant to its exact prior state and makes both scripts re-runnable.

```bash
# --- provision: append created ids to a machine-readable state file ---
STATE_FILE="${STATE_FILE:-.demo-state.json}"
# On first run, seed an empty object; thereafter merge new ids in.
[[ -f "$STATE_FILE" ]] || echo '{}' > "$STATE_FILE"

record() {  # record <jq-key> <value>
  local tmp; tmp="$(mktemp)"
  jq --arg v "$2" ".$1 = \$v" "$STATE_FILE" > "$tmp" && mv "$tmp" "$STATE_FILE"
}

record caPolicyId    "$POLICY_ID"
record spaGraphGrant "$GRANT_ID"      # oauth2PermissionGrant id from §2.3
```

```bash
# --- teardown: read ids, delete each, tolerate already-gone (204 or 404) ---
STATE_FILE="${STATE_FILE:-.demo-state.json}"
del_graph() {  # del_graph <graph-path-with-id>
  az rest --method DELETE --uri "https://graph.microsoft.com/beta/$1" 2>/dev/null \
    && echo ">>> deleted $1" || echo ">>> already gone (skip): $1"
}

if [[ -f "$STATE_FILE" ]]; then
  CA="$(jq -r '.caPolicyId // empty' "$STATE_FILE")"
  GR="$(jq -r '.spaGraphGrant // empty' "$STATE_FILE")"
  [[ -n "$GR" ]] && del_graph "oauth2PermissionGrants/$GR"          # revoke consent first
  [[ -n "$CA" ]] && del_graph "identity/conditionalAccess/policies/$CA"
  rm -f "$STATE_FILE"
fi
```

**How the existing repo teardown works (referenced pattern):** [`teardown-app-registrations.sh`](../../../../scripts/teardown-app-registrations.sh) does NOT use a state file — it looks each object up **by display name** (`az ad app list --display-name ... --query "[0].appId"`) and only deletes if found (`delete_app` guards on non-empty id; the Key Vault cert delete guards on `az keyvault certificate show`). This is idempotent and re-runnable, and is the model to extend. A state file is strictly better for reversibility because it deletes the *exact* object created (not "whatever currently matches this name"), avoiding the risk of deleting a pre-existing collision. Best practice = **do both**: prefer the recorded id; fall back to a prefix sweep (§2.5).

### 2.3 Tier 2 lab tenant/app-registration changes + how to revoke them

Per README WI-01, Tier 2 requires granting the **SPA Microsoft Graph `User.Read` delegated consent**. That consent materializes as an **`oauth2PermissionGrant`** on the SPA's service principal. To reverse it fully:

1. **Find the grant(s)** for the SPA service principal:

   ```bash
   SPA_SP_ID="$(az ad sp show --id "$SPA_CLIENT_ID" --query id -o tsv)"
   GRAPH_SP_ID="$(az ad sp show --id 00000003-0000-0000-c000-000000000000 --query id -o tsv)"
   az rest --method GET \
     --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?\$filter=clientId eq '$SPA_SP_ID'" \
     --query "value[?resourceId=='$GRAPH_SP_ID'].{id:id,scope:scope,consentType:consentType}" -o table
   ```

2. **Delete the grant** → revokes the delegated consent: `DELETE /oauth2PermissionGrants/{id}` → `204`. Note existing access tokens remain valid until they expire; only *new* tokens are denied the revoked scope. ([delete oAuth2PermissionGrant](https://learn.microsoft.com/en-us/graph/api/oauth2permissiongrant-delete))

   ```bash
   az rest --method DELETE --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants/$GRANT_ID"
   ```

   - **Watch for TWO grants:** the docs warn there can be two grants authorizing the same app — one with `consentType: "Principal"` (a specific user consented) and one with `consentType: "AllPrincipals"` (admin/tenant-wide consent). Delete **both** to fully revoke. ([delete oAuth2PermissionGrant — note](https://learn.microsoft.com/en-us/graph/api/oauth2permissiongrant-delete))
   - **Permissions to delete a grant:** least-privileged `DelegatedPermissionGrant.ReadWrite.All` (or `Directory.ReadWrite.All`); roles include Application Administrator / Cloud Application Administrator / Privileged Role Administrator. ([delete oAuth2PermissionGrant — permissions](https://learn.microsoft.com/en-us/graph/api/oauth2permissiongrant-delete))

3. **Application-permission (app-role) consent, if any** materializes as an **`appRoleAssignment`** on the SP instead (`DELETE /servicePrincipals/{spId}/appRoleAssignments/{assignmentId}`). The mock SPA only needs delegated `User.Read`, so this is typically N/A here — but list-and-check for completeness. The existing provision uses `az ad app permission admin-consent`, which creates these grant objects; deleting the SP (`az ad app delete` / `az ad sp delete`) also removes its grants, but for a targeted, reversible demo prefer deleting just the grant so the app registration lifecycle is decoupled.

### 2.4 Idempotency, safety, and clean-tenant verification

- **Re-runnable provision:** look up before create (apps by display name; scope id reused; CA policy by `displayName` prefix so a second run updates rather than duplicates). The repo already demonstrates this shape in [`provision-app-registrations.sh`](../../../../scripts/provision-app-registrations.sh).
- **Re-runnable teardown:** every delete guarded (missing object → skip, not error), exactly as [`teardown-app-registrations.sh`](../../../../scripts/teardown-app-registrations.sh) does today. Graph `DELETE` returns `204`; a repeat returns `404` which the guard swallows.
- **Verify the tenant is clean afterward:**

  ```bash
  # 1) No demo CA policies remain
  az rest --method GET \
    --uri "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies?\$select=id,displayName" \
    --query "value[?starts_with(displayName,'croesus-demo-')]" -o table   # expect empty

  # 2) No lingering Graph consent for the SPA
  az rest --method GET \
    --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?\$filter=clientId eq '$SPA_SP_ID'" \
    --query "value[?resourceId=='$GRAPH_SP_ID']" -o table                 # expect empty

  # 3) App registrations gone (existing teardown covers this)
  az ad app list --display-name "Croesus GPD Central SPA (mock)" --query "[0].appId" -o tsv  # expect empty
  ```

### 2.5 Tag/name with a unique demo prefix so teardown finds strays

Give every created object a unique, greppable prefix — `croesus-demo-` for CA policy `displayName`s (matching the repo's existing `Croesus GPD Central ... (mock)` convention for apps). Then a "sweep" teardown can delete strays even if the state file is lost:

```bash
# Sweep: delete every CA policy whose displayName starts with the demo prefix
for id in $(az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies?\$select=id,displayName" \
  --query "value[?starts_with(displayName,'croesus-demo-')].id" -o tsv); do
  az rest --method DELETE --uri "https://graph.microsoft.com/beta/identity/conditionalAccess/policies/$id"
done
```

> CA policies have no free-text `tags` field, so the `displayName` prefix IS the tag. App registrations support `tags` (`az ad app update --id ... --set tags="['croesus-demo']"`) as an additional strays-finding hook.

---

## 3. The OBO best-practices gap (wrong vs right)

### 3.1 OBO flow requirements (Microsoft)

OBO lets a middle-tier web API call a downstream API using the user's identity (delegation). Requirements ([OBO flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)):

- The middle tier is a **confidential client** and authenticates to the token endpoint with either a **client secret** or — preferred — a **certificate** (`client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer` + `client_assertion` = a JWT signed by the cert's private key). ([OBO — certificate case](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow), [certificate credentials](https://learn.microsoft.com/en-us/entra/identity-platform/certificate-credentials))
- The middle-tier request uses `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer` and `requested_token_use=on_behalf_of`, with `assertion` = the inbound token whose `aud` is the middle-tier API. ([OBO — request params](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))
- **Reject-the-token rule:** "This token must have an audience (`aud`) claim of the app making this OBO request... Applications can't redeem a token for a different app (for example, if a client sends an API a token meant for Microsoft Graph, the API can't redeem it using OBO. It should instead reject the token)." ([OBO — assertion param](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))
- The API **exposes a scope** (`access_as_user`) so leg-1's `aud` is the API, and consent for the SPA→API→downstream chain is arranged via **`knownClientApplications`** + **`preAuthorizedApplications`** (or admin consent) so the user is prompted once. ([OBO — gaining consent](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))

The repo's [`api/Controllers/MeController.cs`](../../../../api/Controllers/MeController.cs) implements exactly this via `Microsoft.Identity.Web` `ITokenAcquisition.GetAuthenticationResultForUserAsync(GraphScopes, user: User)` — after a defensive `AudienceMatchesThisApi` check that enforces the reject-the-token rule before any exchange.

### 3.2 Gap checklist — what is MISSING to go from replay → compliant OBO

The customer's real flow reuses the user's token directly against Graph (one token, one audience, no credential, no exposed scope). To become compliant OBO, exactly these five pieces must be added (frame each as a visualizable before/after row):

| # | Missing piece | Wrong (replay) | Right (OBO) | Authority |
| --- | --- | --- | --- | --- |
| a | **Middle-tier credential** | None (public/SPA-only) | Certificate in Key Vault (preferred) → `client_assertion` | [OBO cert case](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow) |
| b | **Exposed API scope** | None — SPA asks for a Graph scope directly | `api://<api-id>/access_as_user`, so leg-1 `aud` = API | [OBO consent / scopes](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow) |
| c | **Distinct leg-2 audience** | Same Graph-audienced token reused | New token minted with `aud` = Microsoft Graph | [OBO protocol diagram](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow) |
| d | **Fresh token issuance** (new `jti`/`iat`) | Identical token bytes replayed | Token endpoint issues a brand-new token (new `jti`, `iat`) | [OBO token response](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow) |
| e | **Pre-authorization + reject-the-token** | No `knownClientApplications`/`preAuthorizedApplications`; foreign-aud token blindly forwarded | SPA pre-authorized; API rejects any non-API-audienced token | [OBO consent](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow) |

The single decisive difference (per repo analysis) is the **credentialed API that exposes a scope**: with it, leg-1's `aud` is the API and the middle tier can mint a fresh, audience-bound leg-2 token; without it, the second hop can only be a replayed user token.

### 3.3 Microsoft guidance: token relay is an anti-pattern; OBO is the recommended pattern

The OBO doc's own warning is the citable anti-pattern statement:

> **"DO NOT send access tokens that were issued to the middle tier to anywhere except the intended audience for the token."** Risks called out: increased risk of token interception over compromised TLS; **inability to satisfy token binding and Conditional Access scenarios requiring claim step-up (for example, MFA, Sign-in Frequency)**; incompatibility with admin-configured device-based policies (MDM, location). ([OBO — middle-tier request warning](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))

The "inability to satisfy token binding" line ties directly to the customer's Token Protection `1008`/unbound finding: a replayed token cannot satisfy device-bound token protection, which is precisely why the correct answer is OBO (fresh, audience-bound issuance), not token relay. Complementary hardening reference: [Protecting tokens in Microsoft Entra](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id).

---

## 4. Consolidated `az rest` / `az ad` command reference

| Purpose | Command |
| --- | --- |
| Create token-protection CA policy (report-only) | `az rest --method POST --uri https://graph.microsoft.com/beta/identity/conditionalAccess/policies --headers "Content-Type=application/json" --body '{...secureSignInSession...}'` |
| Enable / disable / report-only | `az rest --method PATCH --uri .../beta/identity/conditionalAccess/policies/$ID --body '{"state":"enabled"}'` |
| List demo policies | `az rest --method GET --uri "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies?\$select=id,displayName,state"` |
| Delete CA policy | `az rest --method DELETE --uri https://graph.microsoft.com/beta/identity/conditionalAccess/policies/$ID` |
| List SPA→Graph consent grants | `az rest --method GET --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?\$filter=clientId eq '$SPA_SP_ID'"` |
| Revoke (delete) a delegated grant | `az rest --method DELETE --uri https://graph.microsoft.com/v1.0/oauth2PermissionGrants/$GRANT_ID` |
| Grant SPA Graph User.Read (Tier 2 setup) | `az ad app permission add --id $SPA_CLIENT_ID --api 00000003-0000-0000-c000-000000000000 --api-permissions e1fe6dd8-ba31-4d61-89e7-88639da4683d=Scope` then `az ad app permission admin-consent --id $SPA_CLIENT_ID` |
| Delete app registration | `az ad app delete --id $CLIENT_ID` |

> `az rest` auto-selects the Graph resource from the `graph.microsoft.com` host (no `--resource` needed) — same call shape the repo already relies on in [`provision-app-registrations.sh`](../../../../scripts/provision-app-registrations.sh).

---

## 5. Recommendations for the Tier 2 plan (non-binding)

1. Create the token-protection CA policy against **beta** with `secureSignInSession.isEnabled = true`, `state: enabledForReportingButNotEnforced`, scoped to a single test user + one resource, with a break-glass `excludeUsers`.
2. Prefix everything `croesus-demo-`; write created ids to a `.demo-state.json` state file.
3. Extend the existing teardown to delete: (a) the CA policy by recorded id (fallback: prefix sweep), (b) the SPA's Graph `oauth2PermissionGrant`(s) — both `Principal` and `AllPrincipals` — before deleting apps.
4. Add a `verify-clean.sh` step asserting empty results for the three checks in §2.4.
5. Keep the OBO gap as a 5-row before/after visual (§3.2); anchor the "no replay" proof on **audience binding** (distinct `aud`/`jti`), not on `1008`, because token protection does not enforce on the browser-SPA→custom-API→Graph shape (§1.5).

## 6. Potential next research

- Whether `az rest` against `/beta` CA policies is blocked by any tenant "Graph beta" governance in the demo tenant.
- Exact sign-in-log KQL to surface the token-binding (`1008`) signal under report-only enforcement (cross-reference [scripts/evidence-kql.kusto](../../../../scripts/evidence-kql.kusto)).
- Whether a Conditional Access **template** (`templateId`) exists for token protection to simplify create + guarantee reversibility via template reset.

## 7. Sources

- [Create conditionalAccessPolicy](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-post-policies)
- [conditionalAccessPolicy resource (methods, state values)](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy)
- [conditionalAccessSessionControls (v1.0)](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesssessioncontrols)
- [conditionalAccessSessionControls (beta — secureSignInSession)](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesssessioncontrols?view=graph-rest-beta)
- [secureSignInSessionControl (beta)](https://learn.microsoft.com/en-us/graph/api/resources/securesigninsessioncontrol?view=graph-rest-beta)
- [conditionalAccessGrantControls](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccessgrantcontrols)
- [Update conditionalAccessPolicy](https://learn.microsoft.com/en-us/graph/api/conditionalaccesspolicy-update)
- [Delete conditionalAccessPolicy](https://learn.microsoft.com/en-us/graph/api/conditionalaccesspolicy-delete)
- [How Token Protection enhances Conditional Access (platform/resource limits, report-only deployment)](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)
- [Conditional Access report-only mode](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-report-only)
- [Delete oAuth2PermissionGrant (revoke delegated consent)](https://learn.microsoft.com/en-us/graph/api/oauth2permissiongrant-delete)
- [OAuth 2.0 On-Behalf-Of flow (requirements, anti-pattern warning, consent)](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
- [Certificate credentials for application authentication](https://learn.microsoft.com/en-us/entra/identity-platform/certificate-credentials)
- [Protecting tokens in Microsoft Entra](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id)
- Repo patterns: [scripts/provision-app-registrations.sh](../../../../scripts/provision-app-registrations.sh), [scripts/teardown-app-registrations.sh](../../../../scripts/teardown-app-registrations.sh), [api/Controllers/MeController.cs](../../../../api/Controllers/MeController.cs)
