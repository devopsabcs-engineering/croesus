# Croesus / GPD Central — Entra App Registration & SSO Conditional Access Analysis

> [!IMPORTANT]
> **Next step (one open item blocks final classification).** We still need **one captured `/token` request** from a successful and a failing transaction, showing whether the redemption carries an **`Origin` header**. That single fact classifies the grant: an `Origin` header means a browser authorization-code redemption (consistent with the `spa` registrations); no `Origin` header means a genuine server-side redemption, which a `spa` public client cannot service (Entra returns `AADSTS9002327`) and which would instead need a `web` confidential-client registration.
>
> **Who can capture it (self-verify first):** Desjardins can often answer this **without the vendor** — see [Who can capture the `/token` `Origin` header](#who-can-capture-the-token-origin-header). The raw `Origin` header lives only on the HTTP request the caller sends to Entra, so it is not a field in the tenant sign-in logs. But if the redemption runs in the **user's browser**, a Desjardins user can read the `Origin` header directly with browser DevTools, and the tenant sign-in logs independently show whether the second-leg redemption **succeeds** or **fails with `AADSTS9002327`**. Only if those checks confirm a genuine **server-side** call must the capture come from **Croesus (Olivier Leblanc)**, who then exclusively holds the outbound `/token` request; send the [Croesus escalation packet](assets/croesus-escalation-packet.md) and point to **Q7** (presence indicators and SHA-256 hashes only, never raw tokens or secrets). Keep **Mathieu Santerre (Desjardins)** in the loop as the internal customer contact.
>
> **Guardrail:** until the grant is classified, do not change Conditional Access, allowlist AWS IPs, or mandate OBO.

Analysis of the **Desjardins** "GPD Central" (Central GPD) integration with the **Croesus** SaaS platform. The work answers one customer question: the second, non-interactive sign-in arriving from the Croesus AWS backend — blocked by Conditional Access (CA) in non-prod — is this **expected OAuth behaviour** or a **misconfiguration**, and should the fix be **internal (Option A)** or **escalated to the vendor (Option B)**?

## Bottom line

- The **Conditional Access block is correct-by-design** (Zero Trust), not a Desjardins misconfiguration.
- The grant behind the second, non-interactive sign-in is **not yet classified from a captured request**. Authorization code with PKCE is the leading hypothesis: the vendor states Central redeems an authorization code at Entra's `/token` endpoint, and `/token` is the normal endpoint for that redemption rather than proof of On-Behalf-Of (OBO). The three exported registrations are `spa`-platform public clients (no secret, certificate, or exposed API scope), which is consistent with browser authorization-code redemption rather than a confidential middle tier.
- The `1008` "unbound" line means the client is **not integrated with the platform broker** (Windows Account Manager), a device- and session-binding status. It does not identify the grant or prove that an access token was replayed.
- **Recommended path: capture one correlated transaction first** (escalate to Croesus for the authoritative flow definition and the AWS egress IP ranges) to classify the grant, then choose the minimum remediation. OBO is one option, appropriate only if a genuine middle-tier requirement is proven; it is not required for ordinary code redemption.

## Security findings

| ID | Severity | Finding |
| --- | --- | --- |
| V1 | Medium (unclassified) | A non-interactive sign-in from an AWS IP records Token Protection "unbound" (code `1008`) and carries compliant-device claims; in prod it is only flagged, not blocked. `1008` is a broker-binding status, not proof of token replay, and the grant is not yet classified. Remediation: capture one correlated `/token` request to classify the grant, then enforce the minimum control that fits it. |
| V2 | Medium | Tenant-boundary drift — a UAT/dev-named registration lives in the PROD tenant. |
| V3 | Low | dev-dev hygiene — implicit ID-token issuance enabled + an extra SiteMinder test redirect. |

Positive posture confirmed: no credentials on any registration, scoped HTTPS redirects, single-tenant, service-principal lock enabled.

## Reproduction confirmed (live evidence)

We reproduced the customer's signal end to end in the demo tenant (`MngEnvMCAP675646.onmicrosoft.com`) and captured it directly from the Microsoft Entra sign-in logs.

What we confirmed:

- A browser sign-in by a guest user to the mock Croesus SPA, calling the mock Croesus API, was recorded with a Token Protection sign-in-session status of `Unbound (statusCode: 1008)`.
- The same user and source IP also produced a `bound` sign-in (statusCode `0`), giving a clean bound-versus-unbound contrast that matches the customer's reported pattern.
- A report-only Conditional Access Token Protection policy surfaced the evaluation without blocking anyone.

The captured interactive sign-in (filtered on Token Protection StatusCode equals 1008):

| Field | Value |
| --- | --- |
| User | Emmanuel Knafo (`emknafo@microsoft.com`, guest, B2B collaboration) |
| Application | Croesus GPD Central SPA (mock) (`06ef7c0a-9df3-4bcd-8b6f-ee275ca0adc2`) |
| Resource | Croesus GPD Central API (mock) (`bc6338a5-a02a-4ddf-b1f4-9a9234bed8a8`) |
| Client app | Browser |
| Token Protection - Sign In Session | `Unbound (statusCode: 1008)` |

Full proof, including portal screenshots and the reproducing KQL, is published on the project wiki: [Token Protection 1008 evidence](https://github.com/devopsabcs-engineering/croesus/wiki/Token-Protection-1008-Evidence).

Reproduce it with this query against the `croesus-law` Log Analytics workspace:

```kusto
SigninLogs
| where TimeGenerated > ago(7d)
| extend tp = parse_json(TokenProtectionStatusDetails)
| where tostring(tp.signInSessionStatusCode) == "1008"
| project TimeGenerated, UserPrincipalName, AppDisplayName, ResourceDisplayName, IPAddress
```

> [!NOTE]
> This browser-context `1008` row is retained as a captured exhibit pending verification (tracked as DR-01). Token Protection supports native applications only and does not cover Microsoft Graph, so whether a browser sign-in to a custom API legitimately carries an `Unbound (1008)` binding status is a factual question under review. Read the row as a binding-status observation, not as proof of token replay.

## Who can capture the `/token` `Origin` header

Desjardins created and owns the three app registrations and the Entra tenant; Croesus runs the backend that calls Entra's `/token` endpoint. That split determines who can observe what, because the `Origin` header exists only on the HTTP request itself.

The `/token` endpoint is **Microsoft Entra's** (`login.microsoftonline.com/{tenant}/oauth2/v2.0/token`). The `Origin` header is an HTTP header on the request **sent to** that endpoint, so it physically exists only at the two ends of that one hop: the **caller** (Croesus's server code, or the browser Croesus serves) and **Microsoft Entra**, which receives it. Entra does **not** surface the raw `Origin` header as a field in the tenant sign-in logs. Owning the app registration lets Desjardins read the sign-in log entry and change the registration — it does not let them read the raw header off Croesus's request.

The practical catch: whether Desjardins can capture it alone depends on the very thing under test.

- If the redemption runs in the **user's browser** (the normal `spa` pattern), the request passes through the Desjardins user's machine, so a Desjardins user **can capture it directly** with browser DevTools — the browser sets `Origin` automatically. No vendor needed.
- If the redemption runs **server-side at Croesus** (Olivier's stated claim), it never touches the Desjardins user's browser or network, so **only Croesus can capture it**.

### Self-verify sequence (cheapest first)

1. **Browser DevTools (Desjardins, no vendor).** Have a Desjardins user sign in to Central with DevTools open (Network tab, preserve log) and filter for a POST to `oauth2/v2.0/token`. If it appears, the redemption is browser-side; inspect its request headers for `Origin` (it will be present) — question answered, consistent with the `spa` registrations. If no `/token` POST appears for the second (non-interactive) leg, the redemption is happening on Croesus's server. A browser HAR contains real codes and tokens, so treat it as sensitive: record only "`Origin` present: yes/no", never share the raw trace.
2. **Entra sign-in logs (Desjardins, no vendor).** Entra's behaviour is deterministic: a `spa`-platform client can only redeem an authorization code from a request carrying `Origin`, and rejects a plain server-side redemption with `AADSTS9002327`. So the outcome in the tenant logs is strong indirect proof. Query the non-interactive second leg (filter on the AWS IPs `3.97.32.113` / `3.99.119.124`, or by app/resource) and read the result: a failure with `AADSTS9002327` proves a server-side, no-`Origin` redemption against a `spa` client (which then needs a `web` confidential-client registration); a success implies the redemption actually carries an `Origin` (browser) — the surprising case worth a direct Croesus capture. The recorded client-app (Browser vs non-browser) and IP add corroboration.
3. **Escalate to Croesus (only if steps 1-2 show a genuine server-side call).** The header then lives exclusively on Croesus's machine, so only they can produce it. Request the redacted `/token` per **Q7** of the [escalation packet](assets/croesus-escalation-packet.md) — `Origin` presence, `grant_type`, client-auth method, and audience, as presence indicators or SHA-256 hashes only.

| Question | Who can answer |
| --- | --- |
| Raw `Origin` header on a **server-side** `/token` call | Croesus only (their outbound request; never reaches Desjardins users) |
| `Origin` header on a **browser-side** `/token` call | Desjardins directly, via browser DevTools |
| Did the second-leg redemption succeed or fail with `AADSTS9002327`? | Desjardins, from its own Entra sign-in logs |
| Intended flow definition and AWS egress ranges | Croesus |

## How to fix it properly

The `1008` "unbound" signal means the second-hop client is not integrated with the platform broker (Windows Account Manager). It is a binding status, not a classification of the grant. The durable path is to classify the grant from one captured request, then apply the minimum control that fits the confirmed flow.

Recommended sequence:

1. Escalate to the vendor first (Option B). Ask Croesus for the authoritative flow definition and the AWS egress IP ranges, and confirm whether the `/token` POST is a browser authorization-code redemption (with an `Origin` header) or a server-side call.
2. Classify the grant from one correlated transaction. An `authorization_code` grant with a matching `code_verifier`, followed by Graph use of the newly returned token, supports the authorization-code explanation. An `assertion` with `requested_token_use=on_behalf_of` would establish OBO. The same bearer fingerprint crossing the boundary without a new issuance would support relay or replay.
3. Apply the minimum remediation for the confirmed flow. If a genuine middle-tier requirement is proven, standards On-Behalf-Of is one option: the API registration carries a confidential-client credential (a certificate in Key Vault), exposes an `access_as_user` scope, and pre-authorizes the SPA so the middle tier mints a fresh, audience-bound Graph token instead of forwarding the user's token. The mock API in this repository demonstrates that shape. OBO is not required for ordinary authorization-code redemption.
4. Prefer Entra B2B "Trust compliant devices" to resolve the cross-tenant root cause, so device-compliance claims flow correctly across the tenant boundary rather than being stripped and re-presented.
5. Enforce token binding with Conditional Access only where it applies. Keep any Token Protection policy in report-only until the flow is classified. Token Protection is native-app-only and does not cover Microsoft Graph, so it does not bind a browser-to-Graph hop.

The five pieces a standards OBO requires, for the case where OBO is the confirmed fix, are listed under [What a real OBO needs that the replay lacks](#what-a-real-obo-needs-that-the-replay-lacks).

## Deliverables

| Document | Purpose |
| --- | --- |
| [Analysis & findings report](assets/app-registration-analysis-findings.md) | Full analysis: verified registration matrix, naming reconciliation, security findings, determination, and Option A vs Option B recommendation. |
| [Croesus escalation packet](assets/croesus-escalation-packet.md) | Vendor questions and evidence requests to confirm the intended flow and AWS egress ranges. |
| [Verification guide](assets/app-registration-verification.md) | Operator-run commands to close evidence gaps (owners, admin-consent, tenant IDs, sign-in logs). |
| [Intent / scope notes](assets/app-registration-analysis.md) | Original analysis intent and the six comparison dimensions. |

## Source evidence

| Asset | Description |
| --- | --- |
| [dev-dev.txt](assets/dev-dev.txt) · [dev-prod.txt](assets/dev-prod.txt) · [prod-prod.txt](assets/prod-prod.txt) | Entra app-registration manifest exports for the three environments. |
| [Screenshots of app registrations.docx](assets/Screenshots%20of%20app%20registrations.docx) | 16 Azure Portal / sign-in-log screenshots from the 2026-05-29 session (readable). |
| [croesus_entra_oauth_integration_report_20260529_185237.pdf](assets/croesus_entra_oauth_integration_report_20260529_185237.pdf) | Vendor OAuth integration advisory (RMS-protected). |
| [non_prod_sso_conditional_access_report_20260529_180104.pdf](assets/non_prod_sso_conditional_access_report_20260529_180104.pdf) | Non-prod SSO Conditional Access report. |
| [PROD.docx](assets/PROD.docx) | Production reference document (RMS-protected). |

## Mock Croesus SaaS OBO demo

The analysis above concluded that the three production registrations cannot perform a standards-compliant On-Behalf-Of (OBO) exchange: they hold no credential and expose no API scope. They are `spa`-platform public clients, consistent with browser authorization-code redemption; the grant behind the second sign-in is not yet classified from a captured request, so it is not asserted here as a replay. This demo builds the OBO shape end-to-end as a reference so we can show Desjardins what a standards OBO looks like and how to prove it, without claiming it reconstructs Croesus production.

We frame the demo as the vendor would: we are the Croesus vendor providing a setup guide, and Desjardins stands up the two app registrations in their own tenant.

### Architecture

A React single-page application (SPA) signs the user in and requests a token for the API scope only. The ASP.NET Core middle-tier API validates that token, performs the OBO exchange to acquire a separate Microsoft Graph token, and calls `GET /me`. The SPA never asks for a Graph scope, which forces the OBO boundary to exist.

```mermaid
sequenceDiagram
    participant U as User
    participant S as SPA (public client)
    participant A as croesus-api (confidential client)
    participant E as Entra token endpoint
    participant G as Microsoft Graph
    U->>S: Interactive sign-in (auth code + PKCE)
    S->>E: acquireTokenSilent (scope = api://API/access_as_user)
    E-->>S: token A (aud = API)
    S->>A: GET /api/me with Bearer token A
    A->>A: Validate token A (aud == API, scp == access_as_user)
    A->>E: OBO exchange (on_behalf_of, client cert, assertion = token A)
    E-->>A: token B (aud = Microsoft Graph, new jti and iat)
    A->>G: GET /me with Bearer token B
    G-->>A: user profile
    A-->>S: profile plus decoded-claim evidence
```

### Wrong versus right

The broken baseline in [assets/app-registration-analysis-findings.md](assets/app-registration-analysis-findings.md) reuses the user's token directly against Graph: one token, one audience, no credential, no API scope. The live demo now shows both sides of that boundary. The good path proves OBO succeeds with distinct audiences, distinct `jti` values, and a confidential middle tier. The safe bad path replays the API token to Graph and shows Graph returning `401`. That UI rejection demonstrates audience enforcement; it does not claim the mock emits the customer's Token Protection 1008 signal.

| Property | Broken baseline (replay) | Corrected demo (OBO) |
| --- | --- | --- |
| Tokens involved | One user token reused | Two tokens, leg 1 and leg 2 |
| Second-leg audience (`aud`) | Microsoft Graph (same token) | Microsoft Graph (freshly issued) |
| Middle-tier credential | None | Certificate in Key Vault |
| Exposed API scope | None | `access_as_user` |
| `jti` across legs | Identical (replay) | Distinct (new issuance) |

### App registration comparison: mock versus real

The demo runs two registrations precisely because the three real Croesus registrations lack the pieces a standards OBO needs. The real environments (`dev-dev`, `dev-prod`, `prod-prod`) share one minimal SPA-only shape; the mock splits the work into a public SPA and a credentialed API.

| Field | Mock SPA | Mock API | Real SPA (all three environments) |
| --- | --- | --- | --- |
| Role in flow | Public client (front end) | Confidential middle tier | Single SPA, no middle tier |
| signInAudience | AzureADMyOrg | AzureADMyOrg | AzureADMyOrg |
| Platform | SPA (auth code + PKCE) | API / daemon | SPA |
| Client secret | None | None | None |
| Certificate | None | Self-signed cert in Key Vault | None |
| Exposed API scope | None | `access_as_user` | None |
| Application ID URI | None | `api://<api-client-id>` | None |
| Pre-authorized client | n/a | Mock SPA | None |
| Graph permissions | None (requests the API scope) | `User.Read` (delegated) | `User.Read` (delegated) |
| Performs standards OBO | No (by design) | Yes (the middle tier) | No (cannot) |

The decisive row is the credentialed API that exposes a scope: the mock has it, none of the three real registrations do. That single difference is why the real registrations cannot mint a fresh, audience-bound token B through a confidential OBO exchange, while the mock can. What grant the real second hop actually uses is a separate question that only a captured `/token` request classifies. See [assets/app-registration-analysis-findings.md](assets/app-registration-analysis-findings.md#verified-comparison) for the full real-registration matrix.

### Prerequisites

- An Azure subscription and a single Microsoft Entra tenant where you can create app registrations and grant admin consent.
- The Azure CLI signed in to that tenant.
- A GitHub repository with the deploy federated-identity and configuration variables described in [docs/configuration-contract.md](docs/configuration-contract.md).

### Provision

Create the two single-tenant app registrations and capture their identifiers with the provisioning script:

```bash
./scripts/provision-app-registrations.sh
```

Store the script's outputs as the GitHub Actions repository variables listed in [docs/configuration-contract.md](docs/configuration-contract.md). The API certificate lives only in Key Vault and never enters the repository or the workflow.

### Deploy

The [.github/workflows/deploy-croesus.yml](.github/workflows/deploy-croesus.yml) workflow authenticates to Azure with an OpenID Connect federated credential (no deploy secret), provisions the infrastructure from [infra/main.bicep](infra/main.bicep), and deploys the [spa/](spa/) and [api/](api/) projects to two App Service Web Apps. A post-deploy evidence job runs the smoke test and the gated negative tests and writes the proof to the run summary.

### Read the evidence

The API logs decoded claims (never raw tokens) to Application Insights for both legs: leg 1 shows `aud == API`, leg 2 shows `aud == Microsoft Graph` with a new `jti` and `iat`. The Entra non-interactive sign-in logs in [scripts/evidence-kql.kusto](scripts/evidence-kql.kusto) corroborate the two correlated legs. Together they prove the second token is freshly issued, not a relay of the first.

For the full walk-through and the question-by-question proof, see the demo guide and the evidence narrative.

| Document | Purpose |
| --- | --- |
| [docs/obo-demo-guide.md](docs/obo-demo-guide.md) | Provision, deploy, exercise the flow, and interpret the evidence. |
| [docs/evidence-narrative.md](docs/evidence-narrative.md) | Maps the escalation-packet questions to the demo's concrete evidence. |
| [docs/configuration-contract.md](docs/configuration-contract.md) | Authoritative catalog of every configuration value the demo consumes. |

## Tier 2 replay lab

The live demo ships in three clearly-labeled tiers so the wrong and right flows sit side by side. All Tier 2 behaviour is gated off by default and fully reversible.

- Tier 1 (always on, no tenant changes): a client-only negative control replays the API-audienced token to Microsoft Graph and shows the expected `401` audience rejection. It proves audience binding without introducing any vulnerability.
- Tier 2a (gated): the SPA acquires a real Microsoft Graph token and forwards it to `POST /api/replay`; the API replays that same token to Graph from server-side context and emits a distinct `ReplayAttempt` Application Insights event. This reproduces the server-side replay shape the sign-in logs attribute to the Croesus AWS backend, with no fresh token issuance.
- Tier 2b (gated): a report-only Conditional Access Token Protection policy scoped to a native client and a supported resource (Exchange Online) surfaces the real Token Protection `1008` "unbound" status in the non-interactive sign-in logs.

Tier 2b produces the literal `1008` value that Tier 2a cannot. That value is a Conditional Access sign-in-log signal, not an HTTP response. Enforcement (blocking) evaluates only for native-app clients reaching Exchange Online, SharePoint Online, or Teams (see the licensing section), but the sign-in-session status detail is recorded more broadly. In this tenant we captured `Unbound (statusCode: 1008)` on the browser sign-in to the mock Croesus API itself (see [Reproduction confirmed](#reproduction-confirmed-live-evidence)). Tier 2a reproduces the replay mechanics; Tier 2b reproduces the telemetry.

### What a real OBO needs that the replay lacks

The corrected demo closes five gaps the broken baseline leaves open. A standards OBO requires all five; the replay has none of them.

- A confidential-client credential on the middle tier (a certificate in Key Vault), so the backend can authenticate its own token request.
- An exposed API scope (`access_as_user`) on the middle-tier registration, so the SPA can request a token whose audience is the API rather than Graph.
- A distinct leg-2 audience, minted by the OBO exchange as a new token with `aud = Microsoft Graph` rather than the API-audienced leg-1 token.
- A fresh `jti` and `iat` on the leg-2 token, proving new issuance rather than a relay of the same token.
- Pre-authorization of the SPA on the API plus resource-side reject-the-token behaviour, so a token minted for one audience cannot be redeemed against another.

### Gates and reversible teardown

Two feature gates keep Tier 2 off unless a lab explicitly enables it: `Demo:EnableReplay` on the API (the replay endpoint is only mapped when true) and `VITE_ENABLE_REPLAY_DEMO` on the SPA (the replay control only renders when true). Both default to false. The deploy workflow's manually approved `replay-lab` job provisions the policy, sets the live gate, and runs the Tier 2 evidence in one gated pass; it never runs on the default path.

Every Tier 2 change reverses cleanly:

- [scripts/provision-ca-policy.sh](scripts/provision-ca-policy.sh) creates the report-only policy; [scripts/teardown-ca-policy.sh](scripts/teardown-ca-policy.sh) deletes it by recorded id, with a `croesus-demo-` prefix sweep as a fallback.
- The extended [scripts/teardown-app-registrations.sh](scripts/teardown-app-registrations.sh) also revokes the SPA-to-Graph delegated grant, resets the live `Demo__EnableReplay` setting to false, and removes the `replay-lab` federated credential from the deploy identity.
- [scripts/verify-clean.sh](scripts/verify-clean.sh) is read-only and exits non-zero if any residue remains, so it can gate a "the tenant is clean" claim.

The `replay-lab` GitHub environment is a repository-side object with no secrets and no tenant effect, so the teardown scripts intentionally retain it as durable lab infrastructure. Remove it manually under Settings, then Environments if you want the repository returned to its exact prior state. The full end-to-end walk-through lives in [docs/obo-demo-guide.md](docs/obo-demo-guide.md).

The current live demo objects (the replay gate, the Conditional Access policy id, and the SPA-to-Graph grant id) are captured with identifiers and portal screenshots on the wiki: [Live demo state](https://github.com/devopsabcs-engineering/croesus/wiki/Live-Demo-State). Capture or refresh that record before running any teardown.

## Entra ID licensing for the Tier 2 replay lab

Two distinct capabilities carry different licensing requirements, and the earlier working assumption that Token Protection needs P2 was wrong.

- Reading the `1008` "unbound" status from sign-in logs is telemetry available with Entra ID P1 (or P2) sign-in logs.
- Configuring token protection, the Conditional Access session control **Require token protection for sign-in sessions** (preview), requires **Microsoft Entra ID P1**, not P2. The [Token Protection deployment guide](https://learn.microsoft.com/en-us/entra/identity/conditional-access/deployment-guide-token-protection-windows) states the feature requires P1 licenses. P2 (Identity Protection and risk-based Conditional Access) is a separate set of controls and is not required for this exhibit.

Tenant findings (`MngEnvMCAP675646.onmicrosoft.com`, queried 2026-06-30 via Microsoft Graph `subscribedSkus`):

| Service plan | Capability | Provisioning status |
| --- | --- | --- |
| `AAD_PREMIUM_P2` | Microsoft Entra ID P2 (Identity Protection, risk-based Conditional Access) | Success |
| `AAD_PREMIUM` | Microsoft Entra ID P1 (Conditional Access, Token Protection, sign-in logs) | Success |
| `MFA_PREMIUM` | Multifactor authentication | Success |

These plans are delivered by the `Microsoft_365_E5_(no_Teams)` SKU (5 seats, 2 consumed), so additional seats are available for lab service accounts.

Conclusion and caveat:

- Licensing is present. Entra ID P1 is provisioned, so the report-only Token Protection policy and sign-in-log inspection are both available to the lab.
- Enforcement is scoped by design, but the diagnostic status is broader. The session control "Require token protection for sign-in sessions" blocks only native applications reaching Exchange Online, SharePoint Online, or Teams. The sign-in-session status detail (`bound` or `unbound`, with code `0` or `1008`) is nonetheless recorded on other sign-ins: in this tenant the browser sign-in to the mock Croesus API was recorded as `Unbound (statusCode: 1008)`, the exact signal the customer reported. Enforcement remains scoped to the documented resources, but the diagnostic 1008 status was reproduced on the browser-to-API flow and is captured in [Reproduction confirmed](#reproduction-confirmed-live-evidence).
