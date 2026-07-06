<!-- markdownlint-disable-file -->
# Token Protection, CA status 1008 ("unbound"), and Graph token replay — mechanics for the Croesus/Desjardins demo

**Research date:** 2026-07-06
**Scope:** RESEARCH ONLY. Reproduce the customer's "second sign-in blocked by Conditional Access / Token Protection unbound 1008" scenario and demonstrate the *wrong* (server-side token replay) flow vs the *right* (standards On-Behalf-Of) flow side by side.
**Status:** COMPLETE — authoritative Microsoft Learn sources located for every claim below. One important correction to the working assumption is noted (Token Protection is a **P1** feature, not P2).

---

## TL;DR — answers to the central questions

1. **Can the 1008 / "unbound" signal be OBSERVED or ENFORCED for a SPA→Graph or AWS-server→Graph token replay in the current preview? — NO, not for Microsoft Graph, and not for a browser/SPA client.**
   - Token Protection **only supports native applications; browser-based applications are explicitly not supported** ([concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)).
   - Token Protection can only be **enforced on Exchange Online, SharePoint Online, and Microsoft Teams** (plus Azure Virtual Desktop / Windows 365 on Windows). **Microsoft Graph is NOT a supported resource** ([concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection); [deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows)).
   - Therefore a raw **SPA→Graph** or **AWS→Graph** call will **not** trigger a Token Protection binding evaluation, so the `TokenProtectionStatusDetails` / `signInSessionStatusCode = 1008` field will **not** be populated for those requests. The 1008 signal is scoped to native-client sign-ins to EXO/SPO/Teams where a Token Protection CA policy is in scope.

2. **What IS observable for the Graph replay (the actual Croesus scenario)?**
   - **Tier 1 — audience (`aud`) enforcement at the resource — HTTP 401.** Microsoft Graph (and any correctly-implemented API) rejects a token whose `aud` is not itself. Microsoft's own OBO documentation states the rule explicitly: *"Applications can't redeem a token for a different app (for example, if a client sends an API a token meant for Microsoft Graph, the API can't redeem it using OBO. It should instead reject the token)."* This is deterministic, needs **no P1/P2**, and is the reliable core of the demo. ([v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))
   - **Non-interactive sign-in log telemetry.** The server-side replay leg shows up in `AADNonInteractiveUserSignInLogs` with anomalous IP/location (AWS egress), no fresh token-issuance event, and — if pointed at a Token-Protection-covered resource — the Unbound/1008 detail.

3. **How to demonstrate wrong-vs-right given preview limits (recommended):**
   - **Primary (always works, deterministic):** demonstrate **Tier 1 audience rejection**. Wrong flow = replay the SPA's *API-audience* access token straight to `https://graph.microsoft.com` → **HTTP 401 (invalid audience)**. Right flow = middle-tier confidential client performs **OBO** (`grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, `requested_token_use=on_behalf_of`) to mint a **new token with `aud=graph.microsoft.com`** → **HTTP 200**.
   - **Secondary (to reproduce the customer's exact 1008 line):** stand up a **report-only** Token Protection CA policy scoped to **Exchange Online / SharePoint Online / Teams** + **native (Mobile apps and desktop clients)** client-apps condition, then have the non-broker (AWS) process present a non-device-bound token to that resource → sign-in logs show **`Token Protection - Sign In Session = Unbound`, `signInSessionStatusCode = 1008`** ("client isn't integrated with the platform broker (WAM)"). This is *observation*, achievable with **P1** and report-only mode — no blocking required.

---

## 1. Token Protection (token binding) in Microsoft Entra

### What it is

Token Protection is a **Conditional Access session control** that reduces token-replay attacks by ensuring **only device-bound sign-in session tokens (Primary Refresh Tokens / PRTs)** are accepted by Microsoft Entra ID when applications request protected resources. When a user registers a supported device, a PRT is issued and **cryptographically bound to that device** (on Windows the secret is stored in the TPM), so a stolen token can't be used from another device. ([concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection); [protecting-tokens-microsoft-entra-id](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id))

- **PRT binding:** *"Primary Refresh Tokens (PRTs) are protected with a cryptographically secure tie between the PRT and the device (client secret) to which the PRT is issued. On Windows devices, the client secret is securely stored on … Trusted Platform Modules (TPM). Today, non-Windows devices store the secret in software."* ([protecting-tokens-microsoft-entra-id](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id))
- **What enforcement does:** *"Enforcing Token Protection in Conditional Access ensures that only refresh tokens which are cryptographically bound to the device are used. Bearer refresh tokens, which can be used from any device, are automatically rejected … the token can only be used from the device it was originally issued to."* ([protecting-tokens-microsoft-entra-id](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id))
- **"Unbound token"** = a request that was **not using bound protocols** — i.e., the token presented is a plain bearer token not tied to the device/broker, so Token Protection cannot confirm device binding.

### The grant/session control: "Require token protection for sign-in sessions"

- Configured under **Access controls → Session → Require token protection for sign-in sessions** in a Conditional Access policy. ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows))
- **License requirement: Microsoft Entra ID P1.** *"Using this feature requires Microsoft Entra ID P1 licenses."* ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows))
  - **CORRECTION to the working assumption:** the prompt referenced "Entra ID P2 / P2 CA grant control." Token Protection is a **P1** capability. P2 (Identity Protection / risk-based CA) is a *separate* set of controls (Anomalous Token, Attacker-in-the-Middle, risky sign-in detections). Cite P1 for Token Protection; cite P2 only if the demo also leans on Identity Protection risk detections. ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows); [protecting-tokens-microsoft-entra-id](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id))

### Preview / support matrix (as of doc ms.date 2026-03-24)

**Platform availability** ([concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)):

| Platform | Status |
| --- | --- |
| Windows | Generally Available |
| iOS / iPadOS | Preview |
| macOS | Preview |

> **"Token Protection currently supports native applications only. Browser-based applications are not supported."** ([concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection))

**Supported resources** (enforcement targets) ([concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection)):

- Exchange Online
- SharePoint Online
- Microsoft Teams
- (Windows only, additionally) Azure Virtual Desktop, Windows 365

**Supported native apps on Windows** (non-exhaustive): Outlook, Teams, OneDrive, OneNote, Word/Excel/PowerPoint, Power BI desktop, Visual Studio Code, Microsoft Edge (**sign-in to Edge profile only**), Microsoft Graph PowerShell *with the `EnableLoginByWAM` option*, Exchange PowerShell module, Windows App, etc. ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows))

**Directly relevant to the SPA/Graph question:**
- Because it is **native-app only** and the resource set is **EXO/SPO/Teams (+AVD/Windows 365)**, Token Protection **does not enforce on browser/SPA→Microsoft Graph calls**. A CA policy targeting Token Protection must explicitly set the **Client apps condition to "Mobile apps and desktop clients"** and *not* leave Browser selected — the deployment guide warns leaving Browser selected can break MSAL.js/Teams Web. ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows))
- Token Protection requires a **PRT**, so *"scenarios such as the use of unregistered devices aren't available as those devices don't have a PRT"* and *"Entra Token Protection only applies to the user who signed into the device."* A headless AWS backend has no PRT and no signed-in device context. ([protecting-tokens-microsoft-entra-id](https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id))

---

## 2. The exact meaning of status code 1008 and where it surfaces

The "unbound" status codes come from the **`Token Protection - Sign In Session`** field on the sign-in event **Basic Info** tab (and the `TokenProtectionStatusDetails` structure in Log Analytics). Values are **Bound** or **Unbound**; when **Unbound**, a `statusCode` explains why ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows)):

| statusCode | Meaning (verbatim from Microsoft Learn) |
| --- | --- |
| 1002 | The request is unbound due to the lack of Microsoft Entra ID device state. |
| 1003 | The request is unbound because the Microsoft Entra ID device state doesn't satisfy Conditional Access policy requirements for token protection (unsupported device registration type, or the device wasn't registered using fresh sign-in credentials). |
| 1005 | The request is unbound for other unspecified reasons. |
| 1006 | The request is unbound because the OS version is unsupported. |
| **1008** | **The request is unbound because the client isn't integrated with the platform broker, such as Windows Account Manager (WAM).** |

**Interpretation for the Croesus case:** `1008` specifically means the calling **client is not integrated with the platform broker (WAM)** — i.e., it is not a native app going through the device broker that would produce a device-bound token. A **server-side process replaying a bearer token from an AWS egress IP** is, by definition, *not* WAM-integrated, so a Token-Protection-scoped request from it evaluates to **Unbound / 1008**. This is fully consistent with the analysis that the vendor is replaying the user's token server-side rather than performing a broker-bound sign-in.

**Where it surfaces:**
- **Sign-in logs** — both **interactive** (`SigninLogs`) and **non-interactive** (`AADNonInteractiveUserSignInLogs`); the deployment guide explicitly says to *"Capture both interactive and non-interactive sign-in logs."* The vendor's server-side replay leg is a **non-interactive** user sign-in. ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows))
- **Fields that carry it:**
  - `TokenProtectionStatusDetails` (JSON) → `signInSessionStatus` (Bound/Unbound) and `signInSessionStatusCode` (1002/1003/1005/1006/1008).
  - `ConditionalAccessPolicies[]` → `enforcedSessionControls` (contains `"SignInTokenProtection"`, historically `"Binding"`), `sessionControlsNotSatisfied`, and `result`.
  - `ConditionalAccessStatus` on the row (`success` / `failure` / `notApplied`) reflects the overall CA outcome; in **report-only** the policy result appears under the Report-only pane rather than blocking.
- **String change caveat:** *"The value of the string used in `enforcedSessionControls` and `sessionControlsNotSatisfied` changed from `Binding` to `SignInTokenProtection` in late June 2023."* Queries should match **both**. ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows))

**CA status semantics** (from the activity-details doc) ([concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details)):
- `Failure`: *"The sign-in satisfied the user and application condition of at least one Conditional Access policy and grant controls are either not satisfied or set to block access."* → this is what a **blocked** (enforced) Token Protection sign-in looks like.
- `Not Applied`: policy conditions not matched.
- In **Report-only** mode the outcome is shown on the **Report-only** tab (`reportOnlyFailure` etc.), not enforced.

---

## 3. Reproducing a token replay that surfaces the unbound signal

### Mechanically, how a server-side process replays a user token to Graph

1. The SPA signs the user in interactively (auth-code + PKCE) and obtains an **access token whose `aud` is the vendor's API** (the intended middle-tier).
2. Instead of doing a standards OBO exchange, the vendor's **AWS backend takes that same bearer token and re-sends it** in an `Authorization: Bearer …` header to a downstream resource **from a different network context** (AWS egress IP), reusing the *same* token (same `jti`/`iat`, no new issuance).
3. Why this reads as "unbound": the replayed token is a plain **bearer** token with **no device/PRT binding** and the calling client **isn't WAM/broker-integrated**, so if a Token Protection policy is in scope for that request, Entra evaluates the sign-in as **Unbound (1008)**. Microsoft's guidance explicitly warns that relaying access tokens from a middle tier back out breaks binding: *"Security risks of relaying access tokens from a middle-tier resource to a client … Inability to satisfy token binding and Conditional Access scenarios …"* ([v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))

### What is realistically observable in a demo tenant (P1)

- **Yes — you can make Unbound/1008 appear in sign-in logs**, but **only for a Token-Protection-covered combination**: a **native-app client** signing in to **Exchange Online / SharePoint Online / Teams** with a **report-only** (or enforced) Token Protection CA policy in scope, where the presented token is not device/broker bound. Report-only mode is the documented way to *observe without blocking*. ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows))
- **No — you cannot make 1008 appear for a raw SPA→Graph or AWS→Graph replay**, because Token Protection does not apply to browser clients and **Graph is not a supported resource**. The binding evaluation simply doesn't run for that request. ([concept-token-protection](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection))

### Constraints summary

| Constraint | Effect on the demo |
| --- | --- |
| Native apps only (browser unsupported) | SPA-origin requests never produce Token Protection binding telemetry. |
| Resources = EXO/SPO/Teams (+AVD/Win365) | Microsoft Graph replay produces **no** 1008; must target EXO/SPO/Teams to see it. |
| Requires PRT / device registration | Headless AWS backend has no PRT → inherently "unbound" when in scope. |
| P1 for the CA control; report-only for observation | Observation ≠ enforcement; report-only surfaces the signal without blocking. |

### Observing vs enforcing (cite the docs)

- **Observing (telemetry):** sign-in logs (interactive + non-interactive) capture `TokenProtectionStatusDetails` / Unbound / statusCode regardless of whether the policy blocks — this is what **report-only** mode is for. ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows); [concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details))
- **Enforcing (blocking):** flip the policy from **Report-only → On**; a blocked sign-in shows CA `Failure` with the Token Protection session control **not satisfied**. This is the **P1** grant/session control. ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows))

---

## 4. KQL / sign-in log evidence

Two tables: **`SigninLogs`** (interactive) and **`AADNonInteractiveUserSignInLogs`** (the vendor's server-side replay leg). Correlate via **`CorrelationId`** (groups a sign-in session), and per-token identity via **Request ID** and **Unique token identifier** ([concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details)):
- *Correlation ID* — groups sign-ins from the same session.
- *Request ID* — corresponds to an issued token.
- *Unique token identifier* — correlates the sign-in with the token request.

### 4a. Unbound / 1008 evidence (adapted from Microsoft's official sample)

The official "devices don't meet policy requirements" sample filters `TokenProtectionStatusDetails` for `signInSessionStatusCode == 1003`; swap to **1008** for the WAM/broker case ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows)):

```kusto
// Non-interactive sign-ins flagged Unbound with statusCode 1008 (client not WAM/broker integrated)
AADNonInteractiveUserSignInLogs
| where TimeGenerated > ago(7d)
| where TokenProtectionStatusDetails != ""
| extend parsed = parse_json(TokenProtectionStatusDetails)
| extend bindingStatus     = tostring(parsed["signInSessionStatus"])       // "Unbound"
| extend bindingStatusCode = tostring(parsed["signInSessionStatusCode"])   // "1008"
| where bindingStatusCode == "1008"
| project TimeGenerated, UserPrincipalName, AppDisplayName, ResourceDisplayName,
          IPAddress, bindingStatus, bindingStatusCode, CorrelationId, Id
| sort by TimeGenerated desc
```

### 4b. Block-vs-allow by app (official Microsoft sample, both string variants)

Matches both the pre- and post-June-2023 strings (`Binding` and `SignInTokenProtection`) ([deployment-guide-token-protection-windows](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows)):

```kusto
AADNonInteractiveUserSignInLogs
| where TimeGenerated > ago(7d)
| project Id, ConditionalAccessPolicies, UserPrincipalName, AppDisplayName, ResourceDisplayName
| where ConditionalAccessPolicies != "[]"
| where ResourceDisplayName in ("Office 365 Exchange Online","Office 365 SharePoint Online","Microsoft Teams")
| mv-expand todynamic(ConditionalAccessPolicies)
| where ConditionalAccessPolicies["enforcedSessionControls"] contains '["Binding"]'
     or ConditionalAccessPolicies["enforcedSessionControls"] contains '["SignInTokenProtection"]'
| where ConditionalAccessPolicies.result != "reportOnlyNotApplied" and ConditionalAccessPolicies.result != "notApplied"
| extend SessionNotSatisfyResult = ConditionalAccessPolicies["sessionControlsNotSatisfied"]
| extend Result = case(SessionNotSatisfyResult contains 'SignInTokenProtection', 'Block', 'Allow')
| summarize by Id, UserPrincipalName, AppDisplayName, Result
| summarize Requests = count(), Block = countif(Result == "Block"), Allow = countif(Result == "Allow") by AppDisplayName
| sort by Requests desc
```

### 4c. Correlate the two legs (interactive user sign-in ↔ non-interactive replay)

```kusto
// Leg 1: interactive user sign-in from the SPA
let interactive =
    SigninLogs
    | where TimeGenerated > ago(1d)
    | where AppDisplayName == "<SPA app name>"
    | project L1_Time=TimeGenerated, CorrelationId, UserPrincipalName,
              L1_IP=IPAddress, L1_Resource=ResourceDisplayName, L1_UniqueTokenId=UniqueTokenIdentifier;
// Leg 2: non-interactive replay (e.g., from an AWS egress IP)
let replay =
    AADNonInteractiveUserSignInLogs
    | where TimeGenerated > ago(1d)
    | extend tp = parse_json(TokenProtectionStatusDetails)
    | project L2_Time=TimeGenerated, CorrelationId, UserPrincipalName,
              L2_IP=IPAddress, L2_Resource=ResourceDisplayName, L2_App=AppDisplayName,
              L2_Bind=tostring(tp["signInSessionStatus"]), L2_Code=tostring(tp["signInSessionStatusCode"]);
interactive
| join kind=inner replay on CorrelationId, UserPrincipalName
| project UserPrincipalName, CorrelationId, L1_Time, L1_IP, L1_Resource,
          L2_Time, L2_IP, L2_App, L2_Resource, L2_Bind, L2_Code
| sort by L1_Time desc
```

> Note: `CorrelationId` accuracy is best-effort (it is derived from client-passed parameters), so also correlate on `UserPrincipalName` + time window + `UniqueTokenIdentifier` where possible. ([concept-sign-in-log-activity-details](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details))

### 4d. Distinguishing genuine OBO second-leg from a replayed unbound token

| Signal | Genuine OBO second leg | Replayed (wrong-flow) token |
| --- | --- | --- |
| Token issuance | **New token issued** (jwt-bearer / `on_behalf_of` exchange) → distinct **Request ID** / **Unique token identifier**, fresh `iat`/`jti`, `aud = graph.microsoft.com`. | **No new issuance** — same token reused; same `jti`/`iat`; `aud` = the middle-tier API, not Graph. |
| Sign-in log shape | Non-interactive sign-in with **middle-tier confidential app as client** and **Graph as resource**; CA/MFA claims flow through. | Non-interactive leg with **anomalous IP/location** (AWS egress); if aimed at EXO/SPO/Teams under a TP policy → **Unbound / 1008**. |
| Resource acceptance | Graph accepts (`aud` correct) → 200. | Graph **rejects on `aud`** → **401** (see §5); if `aud`=Graph was replayed verbatim, Graph accepts bearer but the leg still looks anomalous. |
| Binding | Can satisfy device/binding/CA step-up because a real token request occurs. | Cannot satisfy binding — *"Inability to satisfy token binding and Conditional Access scenarios"* per OBO doc. |

---

## 5. Audience / binding enforcement at the resource (Tier 1 — HTTP 401)

- **Microsoft Graph (and any correct API) rejects a token whose `aud` is not itself.** Microsoft's OBO documentation states the invariant twice, verbatim:
  > *"This token must have an audience (`aud`) claim of the app making this OBO request … Applications can't redeem a token for a different app (for example, if a client sends an API a token meant for Microsoft Graph, the API can't redeem it using OBO. It should instead reject the token)."* ([v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))
- **This audience rejection is an HTTP 401 at the resource** (bearer-token semantics, RFC 6750 `WWW-Authenticate` with `error="invalid_token"`), and it is **completely different from the CA 1008 sign-in-log signal**:
  - **401 audience rejection** = the **resource** refuses a token minted for a different audience. Deterministic, no license dependency, happens on the API request path.
  - **CA 1008 "Unbound"** = an **Entra Conditional Access / Token Protection** determination recorded in **sign-in logs**, only for supported native-app→EXO/SPO/Teams combinations. It is a sign-in-time policy evaluation, not an API-layer 401.
- Graph best-practices confirm the general error contract (401 = auth/token problem; 403 = authorized identity lacking privilege). ([best-practices-concept](https://learn.microsoft.com/en-us/graph/best-practices-concept)) The audience mismatch is a token-validity failure → **401**, not 403.
- **Correct pattern** the demo should show as the "right" flow: SPA → middle-tier confidential client → **OBO** (`grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, `assertion=<token with aud=middle-tier>`, `requested_token_use=on_behalf_of`, `scope=https://graph.microsoft.com/…`) → **new token with `aud=graph.microsoft.com`** → Graph 200. SPAs specifically **must** hand the token to a middle-tier confidential client for OBO: *"In the case of Single-page apps (SPAs), they should pass an access token to a middle-tier confidential client to perform OBO flows instead."* ([v2-oauth2-on-behalf-of-flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow))

---

## 6. Recommended demo design (given preview limitations)

**Two tiers, clearly labeled so customer + vendor see the *wrong* vs *right* flow side by side:**

**Tier 1 — Audience enforcement (deterministic, no license dependency) — the reliable core:**
- **Wrong flow:** the mock vendor backend takes the SPA's *API-audience* access token and calls `GET https://graph.microsoft.com/v1.0/me` with it → **HTTP 401** (invalid audience). Show the `WWW-Authenticate: Bearer error="invalid_token"` header and decode the token's `aud` to prove it isn't Graph.
- **Right flow:** the middle-tier confidential client performs OBO → new token with `aud=graph.microsoft.com` → **HTTP 200** with `/me`. Decode both tokens side-by-side (different `aud`, new `jti`/`iat`, new Request ID).

**Tier 2 — Token Protection 1008 telemetry (to reproduce the customer's exact sign-in-log line):**
- Create a **Conditional Access policy → Require token protection for sign-in sessions**, **Report-only**, **Client apps = Mobile apps and desktop clients**, **Resources = Office 365 Exchange Online / SharePoint Online / Teams**, scoped to a test user. (**P1**.)
- Have the **non-broker / AWS-context** process present a non-device-bound token to one of those resources → sign-in logs (`AADNonInteractiveUserSignInLogs`) show **`Token Protection - Sign In Session = Unbound`, `signInSessionStatusCode = 1008`**. Run the §4a KQL to surface it.
- Optionally flip Report-only → On to show the **enforced Failure** (blocked) outcome — this is the "second sign-in blocked by Conditional Access" the customer saw.

**Why split it this way:** the customer's *analysis conclusion* (server-side token replay, not standards OBO) is proven **deterministically by Tier 1 (401 audience rejection)**, which works for the Graph target the vendor actually uses. The customer's *observed telemetry* (1008 unbound) is reproduced by **Tier 2**, which the docs restrict to native-app→EXO/SPO/Teams — so the demo must point that leg at a supported resource, not Graph, to make 1008 appear.

---

## 7. Clarifying questions for the requester

1. **Which resource did the customer's 1008 line actually target?** If their raw logs show `ResourceDisplayName = Office 365 Exchange Online / SharePoint Online / Teams`, Tier 2 reproduces it exactly. If the 1008 was recorded against **Microsoft Graph**, that would contradict the current documented support matrix and warrants a support-case escalation (possible undocumented/preview behavior).
2. **Is the demo's downstream resource Microsoft Graph specifically, or a customer/vendor API?** This decides whether Tier 1 (Graph `aud` 401) is the primary artifact or whether a custom resource API must implement its own `aud` validation.
3. **License available in the demo tenant — P1 confirmed?** Token Protection CA control needs **P1**. If only P2/Identity Protection is desired, we'd pivot Tier 2 to Anomalous-Token / risk-based detections instead.
4. **Is device registration / a Windows Entra-joined test device available?** Tier 2's "Bound (good) vs Unbound (replay)" contrast is most convincing when you can also show a **Bound** sign-in from a registered device beside the **Unbound/1008** replay leg.
5. **Do we need enforcement (blocking) in the demo, or is report-only observation sufficient** for the customer/vendor conversation?

---

## Sources

- Token Protection concept & support matrix: <https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection>
- Token Protection Windows deployment guide (status codes incl. **1008**, KQL, report-only, P1): <https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows>
- Protecting tokens in Microsoft Entra (PRT binding, replay defense, network enforcement): <https://learn.microsoft.com/en-us/entra/identity/devices/protecting-tokens-microsoft-entra-id>
- OAuth 2.0 On-Behalf-Of flow (audience rule, SPA→confidential-client requirement, relay warning): <https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow>
- Sign-in log activity details (Correlation ID / Request ID / Unique token identifier; CA status semantics): <https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details>
- Troubleshooting Conditional Access (sign-in event investigation, CA error codes): <https://learn.microsoft.com/en-us/entra/identity/conditional-access/troubleshoot-conditional-access>
- Microsoft Graph best practices (401 vs 403 error contract): <https://learn.microsoft.com/en-us/graph/best-practices-concept>
