# Subagent Research — "1008" and the Real Croesus / GPD Central Unbound Token Replay

**Date:** 2026-06-30
**Status:** Complete
**Scope (RESEARCH ONLY, no code changes):** Determine precisely what the "1008 issue" is and how the real Croesus / "GPD Central" SaaS app is believed to behave such that it performs an UNBOUND TOKEN REPLAY instead of a standards-compliant On-Behalf-Of (OBO) exchange.

---

## 1. Research questions

1. What is "1008"? (AADSTS code? MSAL error? HTTP status? app-specific code? Conditional Access / Token Protection signal? something else?)
2. What does the real app reportedly DO that produces an unbound token / replay?
3. What is the exact audience/resource mismatch (which client id, which resource/scope)?
4. What symptom/error does the real app yield, and how does "1008" relate to that symptom?
5. Any token-protection / token binding / CAE / Conditional Access / proof-of-possession references?

---

## 2. Direct answers

### 2.1 What "1008" is

"1008" is the **Entra ID Token Protection "token-binding status" event code** recorded in the raw Entra **sign-in logs**. Value **0 = bound**, value **1008 = unbound** (i.e., the token was not cryptographically bound to the originating client/session — it was reused/replayed). It is **NOT** an AADSTS code, **NOT** an MSAL error, **NOT** an HTTP status, and **NOT** a Croesus app-specific code. It is a Microsoft Entra **Token Protection / token-binding signal**.

Exact source text (citations):

- assets/croesus-escalation-packet.md:14 — "Our raw Entra sign-in logs show the blocked event as a **server-side token replay** (Token Protection status \"unbound\", code 1008) targeting Microsoft Graph."
- assets/app-registration-analysis-findings.md:23 — "The raw sign-in logs confirm the second event is a **server-side token replay** (Token Protection status \"unbound\", code 1008) targeting Microsoft Graph — not a custom backend-API audience."
- assets/app-registration-analysis-findings.md (raw sign-in log evidence table, prod-prod) — "Token Protection status | **bound (code 0)** | **unbound (code 1008)**" (sign-in #1 interactive vs sign-in #2 replay).
- README.md:8 — "confirmed by raw sign-in logs to be a **token replay** (Token Protection \"unbound\", code 1008) to Microsoft Graph."
- .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md:162 — "| Token Protection status | bound (code 0) | unbound (code 1008) |"

**CRITICAL CORRECTION (scope limit of 1008) — must not be overstated:**

The repo's own later analysis demotes 1008 because Token Protection token binding only emits the bound/unbound (0/1008) signal for **native-application clients** reaching **Exchange Online, SharePoint Online, and Teams** — it does **not** fire for a browser SPA calling a custom API that then calls Microsoft Graph.

- docs/evidence-narrative.md (section "Why Token Protection 1008 is only an optional exhibit") — "Token Protection token binding, and therefore the 1008 \"unbound\" signal, applies to native-application clients reaching specific resources (Exchange Online, SharePoint Online, and Teams services). It does not fire for a browser-based SPA calling a custom API that then calls Microsoft Graph. Presenting 1008 as the OBO proof for this app shape would assert a signal that the platform does not emit here."
- .copilot-tracking/plans/2026-06-29/croesus-mock-saas-obo-flow-plan.instructions.md:23 — "Re-anchor the \"no replay\" proof on OBO audience-binding rather than Token Protection code 1008 — Derived from: research CRITICAL CORRECTION (1008 is native-app/EXO-SPO-Teams only and will not fire for a browser SPA + custom API + Graph)."
- .copilot-tracking/details/2026-06-29/croesus-mock-saas-obo-flow-details.md:21 — "Token Protection 1008 demoted to optional advanced exhibit."

> Interpretation: 1008 is real and was observed in the customer's *actual* prod-prod sign-in logs (where the real Croesus flow targets Microsoft Graph from a native/server context), but the team concluded it is the wrong headline proof for the *mock demo's* SPA→custom-API→Graph shape. For the REAL app, 1008 = the observed unbound/replay signal; for the mock, audience-binding (two distinct `aud` + `jti`) is used instead.

### 2.2 What the real app reportedly DOES (the replay behavior)

After the user's interactive browser sign-in, the **Croesus SaaS backend (hosted in AWS) takes the user's already-issued token and re-presents (replays) it server-side, non-interactively, from an AWS egress IP, directly against Microsoft Graph** — it does NOT exchange it via OBO for a new downstream token. The replayed token still carries the original session's `deviceId` and "Azure AD joined / compliant" device claims even though it now originates from an AWS IP with no real device context.

Evidence:

- assets/app-registration-analysis-findings.md (V1 finding) — "The second event is a **server-side token replay** (\"unbound\" / Token Protection code 1008), not a bound or OBO token. The replayed token inherits the original session's `deviceId` and \"Azure AD joined / compliant\" device claims, but is presented from an **AWS IP with no real device context**."
- assets/app-registration-analysis.md (Observed behaviour) — "Second sign-in: Non-interactive; From **Croesus AWS IP**; Reuses user + device token; Gets blocked by Conditional Access."
- .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md:171 — "the second event's resource is Microsoft Graph (User.Read), not a custom backend-API audience, and Token Protection records it as \"unbound\" (replay), code 1008 — a server-side TOKEN REPLAY that carries the original session's deviceId and \"Azure AD joined / compliant\" device claims from an AWS IP with no real device context."
- README.md ("Wrong versus right") — "The broken baseline … reuses the user's token directly against Graph: one token, one audience, no credential, no API scope."

**Why it cannot be a real OBO (structural proof from the manifests):** A standards OBO requires the middle tier to be a **confidential client** (client secret or certificate) AND the app to **expose an API** (custom scope / Application ID URI). All three exported app registrations have empty credentials and no exposed scope.

- assets/app-registration-analysis-findings.md:24 (Determination #3) — "A genuine Entra OBO requires the middle tier to be a **confidential client** (secret or certificate) **and** the app to **expose an API** … All three exports have empty `passwordCredentials`, `keyCredentials`, and `api.oauth2PermissionScopes`, with SPA-only platform."
- Verified directly in the manifest exports: assets/dev-dev.txt, assets/dev-prod.txt, assets/prod-prod.txt each show `"passwordCredentials": []`, `"keyCredentials": []`, `"oauth2PermissionScopes": []`, `"appRoles": []`, and only a `spa.redirectUris` platform (public client).
- .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md:36 — "These can only do server-side **token replay** (forwarding the SPA's Graph token), which Token Protection flags as \"unbound\" (code 1008). They **cannot** perform standards OBO because OBO requires (a) a confidential client credential … and (b) an exposed API scope so the SPA token has `aud` = the API (not Graph)."

### 2.3 The exact audience / resource mismatch

There is **no OBO audience hop at all** — that is the defect. Instead of `aud = <custom backend API>` on leg 1 and a freshly-minted `aud = Microsoft Graph` on leg 2, the SAME token (audience **Microsoft Graph**) is used for both the browser session and the server-side replay.

Concrete identifiers (from the manifests + sign-in-log evidence table):

- **Resource (audience) on the replayed token:** Microsoft **Graph** — `resourceAppId` `00000003-0000-0000-c000-000000000000`, delegated scope **`User.Read`** (`e1fe6dd8-ba31-4d61-89e7-88639da4683d`). Present in all three manifests (assets/dev-dev.txt, assets/dev-prod.txt, assets/prod-prod.txt under `requiredResourceAccess`).
- **Client app ids (per environment):**
  - dev-dev `sp-CentralGPD-UAT-dev-fed` appId **`713d6ede-38a2-45f4-8982-89ea4fcf1a7f`** (DEV tenant `MVTDEVDesjardins`).
  - dev-prod `sp-CentralGPD-UAT-dev-fed` appId **`e3e358ea-0aae-4f11-b866-00f76b1cf6c1`** (PROD tenant `mvtdesjardins`).
  - prod-prod `sp-CentralGPD-prod-fed` appId **`92dd40a3-f7c2-42ff-9303-fff5928e195a`** (PROD tenant).
- **The mismatch / missing boundary:** there is no custom API audience because `api.oauth2PermissionScopes = []` on every registration. The escalation packet asks Croesus to confirm this exact point.
  - assets/croesus-escalation-packet.md (Q3) — "What is the **audience (`aud`)** of the token your backend presents on the second request? Is it **Microsoft Graph**, or a **custom backend API**? … Our logs show Microsoft Graph as the resource. A real OBO would target your API, not Graph."

### 2.4 The symptom / error the real app yields, and how 1008 relates

- **In PROD (prod-prod):** the replay **succeeds** (`ResultType 0`) and is only **flagged** by Token Protection as unbound (code 1008) — not blocked. Both sign-in rows share the same `deviceId`, `SessionId 007bc799-6350-25bf-fbe9-ebbcb7093b63`, tenant `728d20a5-0b44-47dd-9470-20f37cbf2d9a`; sign-in #2 source IP is **`3.97.32.113` (Amazon AWS)**.
  - assets/app-registration-analysis-findings.md (Raw sign-in log evidence, prod-prod table) — sign-in #2: "IsInteractive FALSE | IP 3.97.32.113 (Amazon AWS) | Token Protection unbound (code 1008) | ResultType 0 (success)".
  - assets/app-registration-analysis-findings.md (V1) — "In PROD it currently succeeds and is only *flagged* (not blocked) by Token Protection."
- **In NON-PROD (dev-prod scenario, DEV tenant):** the same server-side replay is **BLOCKED by Conditional Access** because the AWS IP is untrusted and the Prod-managed device is not compliant cross-tenant in the DEV tenant.
  - assets/app-registration-analysis.md (Device compliance + CA impact) — "Second request: Same device token. But: Comes from external SaaS IP; Not in trusted network; ⇒ Blocked by CA policy."
  - assets/app-registration-analysis-findings.md (Root cause) — "the Croesus backend's server-side token step originates from an untrusted AWS IP with no device context, so device-based CA in the Dev tenant correctly fails the second event."
- **Verbatim AADSTS code for the non-prod denial is still an OPEN gap** (the CA policy name observed is "Restriction Perimetre" / perimeter block in the DEV tenant; the exact AADSTS number was not captured).
  - assets/app-registration-analysis-findings.md (6.1 evidence gaps) — "Verbatim **AADSTS failure code + CA policy name** for the non-prod DEV-tenant denial | **Open**".
  - assets/app-registration-verification.md (Section 4) — instructs operators to capture "AADSTS result/failure code" and "the \"Restriction Perimetre\" perimeter block observed in the DEV tenant".

**How 1008 relates to the symptom:** 1008 is the Entra-side *characterization* of the second event as an unbound/replayed token. It is the root security signal behind the V1 "High (design-level)" finding: a replayed token that still carries compliant-device claims from an external AWS IP can satisfy device-based CA grants it should not. The remediation is to enforce a **Token Protection / token-binding CA control** so unbound (1008) tokens are denied even in PROD.

### 2.5 Token-protection / token-binding / CAE / proof-of-possession references

- **Token Protection / token binding:** central to the analysis (the 1008 = unbound signal). Remediation V1 = "Enforce a **Token Protection / token-binding CA control** so unbound (replayed) tokens are denied." (assets/app-registration-analysis-findings.md V1; README.md V1; escalation packet Q6).
  - escalation packet Q6 — "Is your token handling compatible with **Entra Token Protection / token binding**? Would enforcing a token-binding CA control break your flow?"
- **Proof of possession:** Not referenced by that phrase anywhere in the repo. The conceptually-equivalent control referenced is Token Protection token binding (a session-bound / cryptographically-bound token vs an "unbound" reused one).
- **CAE (Continuous Access Evaluation):** Not referenced anywhere in the repo (no hits).
- **Conditional Access:** pervasive — the CA block is held to be "correct-by-design" (Zero Trust), not a misconfiguration. Cross-tenant device-compliance gap is the architectural root cause. Preferred fix = Entra B2B "Trust compliant devices"; alternative = scoped CA trusted-location exception for the AWS egress IPs + MFA.

---

## 3. Keyword sweep (meaningful hits, file:line + quote)

> Note: dozens of OBO/audience/Graph hits exist across docs/, api/, spa/, and tracking files (the mock demo). Below are the load-bearing hits for the REAL-app replay / 1008 question. Source-tree (api/, spa/) hits for `OBO`/`aud`/`Graph` all belong to the *mock* demo, not the real app.

| Term | File:line | Quote (trimmed) |
| --- | --- | --- |
| 1008 / unbound / replay | assets/croesus-escalation-packet.md:14 | "server-side token replay (Token Protection status \"unbound\", code 1008) targeting Microsoft Graph" |
| 1008 / audience | assets/croesus-escalation-packet.md (Q3) | "audience (aud) … Microsoft Graph, or a custom backend API? … A real OBO would target your API, not Graph." |
| Token binding / CAE-equivalent | assets/croesus-escalation-packet.md (Q6) | "compatible with Entra Token Protection / token binding? Would enforcing a token-binding CA control break your flow?" |
| 1008 / unbound / V1 | assets/app-registration-analysis-findings.md (V1) | "server-side token replay (\"unbound\" / Token Protection code 1008) … presented from an AWS IP with no real device context" |
| bound vs unbound table | assets/app-registration-analysis-findings.md (sign-in log table) | "Token Protection status | bound (code 0) | unbound (code 1008)"; replay IP "3.97.32.113 (Amazon AWS)" |
| OBO impossibility | assets/app-registration-analysis-findings.md (Det. #3) | "empty passwordCredentials, keyCredentials, and api.oauth2PermissionScopes, with SPA-only platform" |
| replay to Graph | assets/app-registration-analysis.md (Observed behaviour) | "Non-interactive; From Croesus AWS IP; Reuses user + device token; Gets blocked by Conditional Access" |
| 1008 / replay | README.md:8 | "token replay (Token Protection \"unbound\", code 1008) to Microsoft Graph" |
| 1008 scope correction | docs/evidence-narrative.md (Token Protection section) | "1008 … applies to native-application clients reaching … EXO, SPO, and Teams … does not fire for a browser-based SPA calling a custom API" |
| audience boundary proof | docs/evidence-narrative.md (headline proof) | "Two distinct audiences with two distinct token identifiers cannot be produced by reusing a single token." |
| AADSTS50011 (mock) | docs/obo-demo-guide.md:75 | redirect URI mismatch — demo only, unrelated to real-app replay |
| AADSTS50079 / AADSTS (mock) | docs/obo-demo-guide.md:118 | ROPC + MFA failure — demo only |
| 1008 demoted | .copilot-tracking/plans/2026-06-29/...plan.instructions.md:23 | "Re-anchor … on OBO audience-binding rather than Token Protection code 1008 … 1008 is native-app/EXO-SPO-Teams only" |
| 1008 / unbound | .copilot-tracking/research/2026-06-15/app-registration-analysis-research.md:171,181 | "server-side TOKEN REPLAY (Token Protection \"unbound\", code 1008) targeting Microsoft Graph" |
| replay vs OBO | .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md:36 | "can only do server-side token replay (forwarding the SPA's Graph token), which Token Protection flags as \"unbound\" (code 1008)" |
| AADSTS gap (open) | assets/app-registration-analysis-findings.md (6.1) | "Verbatim AADSTS failure code + CA policy name for the non-prod DEV-tenant denial | Open" |
| GPD / Croesus | assets/croesus-escalation-packet.md:1-5 | "Croesus / GPD Central — Vendor Escalation Packet … GPD Central server-side token flow" |
| nonce | (no hits) | "nonce" does not appear anywhere in the repo |
| CAE | (no hits) | "CAE" / Continuous Access Evaluation not referenced anywhere |
| appid acr | (no hits) | literal "appid acr" not present; `appid` appears only in the mock demo claim logging (docs/obo-demo-guide.md:139) |

---

## 4. Concise summary

- **"1008" = Entra Token Protection token-binding status code meaning "unbound"** (0 = bound). It is a Microsoft Entra sign-in-log signal, not AADSTS/MSAL/HTTP/app code. It was observed on the customer's real prod-prod second sign-in.
- **Real-app replay behavior:** After interactive login, the Croesus AWS backend re-presents (replays) the user's existing Microsoft Graph token non-interactively from an AWS IP, with no OBO exchange. The replayed token still carries the original session's `deviceId` + compliant-device claims.
- **Audience mismatch:** There is no second audience — the same `aud = Microsoft Graph` (`00000003-0000-0000-c000-000000000000`, `User.Read`) token is reused. A real OBO would mint a separate token whose first leg is `aud = <custom API>`; the registrations expose no API scope (`oauth2PermissionScopes = []`) and hold no credential, so a real OBO is structurally impossible.
- **Symptom:** PROD → replay succeeds (ResultType 0), only *flagged* as unbound/1008. NON-PROD (DEV tenant) → blocked by Conditional Access (untrusted AWS IP + cross-tenant non-compliant device). 1008 is the Entra characterization that drives the V1 high-severity "replayed token carries compliant-device claims it shouldn't" finding; fix = enforce a token-binding CA control.
- **Caveat:** The repo's later correction notes 1008 only fires for native clients → EXO/SPO/Teams, so it is used as a corroborating/optional exhibit, with audience-binding (distinct `aud` + `jti`) as the primary proof in the mock demo.

---

## 5. Unanswered / open questions

1. **Verbatim AADSTS failure code** for the non-prod DEV-tenant CA denial (and exact CA policy name beyond "Restriction Perimetre") — still Open (assets/app-registration-analysis-findings.md 6.1; verification guide Section 4).
2. **Vendor intent:** Did Croesus intend a deliberate replay, or a true OBO that was mis-implemented? Held in the RMS-locked `croesus_entra_oauth_integration_report_*.pdf` (could not be opened).
3. **Full Croesus AWS egress IP ranges** — only `3.97.32.113` and `3.99.119.124` observed; full set owed by vendor (escalation Q4).
4. **Reconciling 1008 in real logs vs the "1008 won't fire for SPA→API→Graph" correction:** the real flow apparently is native/server-side reuse straight to Graph (so 1008 did fire), whereas the mock is SPA→custom-API→Graph (where it wouldn't). Worth confirming the real client type that produced the observed 1008.
5. App **owners** and **admin-consent grants**, and DEV **tenant ID** — Open governance gaps (verification guide Sections 1-3).

---

## 6. Recommended next research

- [ ] Confirm Microsoft's authoritative definition/scope of the Token Protection bound/unbound (0/1008) signal (client types + supported resources: EXO/SPO/Teams) against current Entra docs, to firmly reconcile the real-log 1008 observation vs the SPA-shape correction.
- [ ] Pull the verbatim AADSTS code + CA policy name for the non-prod denial (run verification guide Section 4 KQL / sign-in log query) to close the last symptom gap.
- [ ] If/when an RMS-unlocked copy of `croesus_entra_oauth_integration_report_*.pdf` is available, extract the vendor's intended flow (replay-by-design vs mis-implemented OBO).
- [ ] Determine the actual client type of the Croesus backend leg (native/daemon vs web) that produced the observed 1008, to validate which Token Protection path applied.
- [ ] Catalogue the `assets/Screenshots of app registrations.docx` images 13-16 (raw sign-in-log export) if not already mined, to capture any additional fields (CorrelationId chain, exact resource appId on the replay leg).
