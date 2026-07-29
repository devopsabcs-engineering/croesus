# Croesus / GPD Central — Vendor Escalation Packet

**To:** Croesus (GPD Central / Central GPD vendor team)
**From:** Desjardins identity / security
**Date:** 2026-06-15
**Subject:** Authoritative definition of the GPD Central server-side token flow and AWS egress ranges

---

## 1. Framing (read first)

This is a request for design clarification, **not** a defect report against your product or our Conditional Access policy.

Our Conditional Access policy is behaving **correctly by design**: in our non-prod (Dev) tenant it denies a non-interactive token request that arrives from an untrusted AWS IP with no compliant-device context. Our analysis of the three app registrations (dev-dev, dev-prod, prod-prod) confirms they **cannot perform a standards-compliant Entra On-Behalf-Of (OBO) flow** as configured — they have no client secret, no certificate, and no exposed API scope.

We want to be precise about what our logs do and do not prove. The blocked event shows Token Protection status "unbound" (code 1008). That status means the client is **not integrated with the platform broker** (Windows Account Manager) — a device- and session-binding signal — and does **not** by itself prove that an access token was replayed, nor does it identify the OAuth grant. Your registrations are declared under the Entra `spa` (public client) platform, which is fully consistent with an ordinary **authorization-code-with-PKCE** redemption (the current leading explanation). One point is worth confirming: Microsoft Entra only redeems a `spa`-platform authorization code from a cross-origin browser request carrying an `Origin` header, and rejects a plain server-side redemption with `AADSTS9002327`. So a literal "mandatory server-side `/token` call" and a `spa` registration cannot both be exactly true unless the redemption runs in the browser. A single captured `/token` request settles this.

To choose the **most proportionate** internal accommodation, we need you to confirm your intended flow. The questions below give us exactly what we need.

---

## 2. Questions for Croesus

| # | Question | Why we need it |
| --- | --- | --- |
| Q1 | What is the **exact OAuth/OIDC flow** GPD Central performs after the user's interactive sign-in? Specifically, is the second, server-initiated `/token` call an **authorization-code-with-PKCE** redemption, an **On-Behalf-Of (OBO)** exchange, a **refresh-token** grant, or a **reuse/replay** of an existing token? | Determines whether any registration/credential change is needed at all, and which internal lever is proportionate. |
| Q2 | Is your backend a **confidential client** (does it hold a client secret or certificate)? If so, **which app registration / appId** does it authenticate as, and **against which scope or Application ID URI**? | A true OBO requires a confidential client + an exposed API. None of our three registrations have these, so a real OBO is currently impossible. |
| Q3 | What is the **audience (`aud`)** of the token your backend presents on the second request? Is it **Microsoft Graph**, or a **custom backend API**? | Our logs show Microsoft Graph as the resource. A real OBO would target your API, not Graph. |
| Q4 | What are your **published AWS egress IP ranges** for the GPD Central backend (all environments)? | Needed to scope any trusted-location CA exception precisely. We have observed `3.97.32.113` and `3.99.119.124`. |
| Q5 | Does your backend rely on the **device-compliance / "Azure AD joined" claims** carried in the user's token? Are you aware those claims are **tenant-specific** (a device compliant in our Prod tenant is not compliant in our Dev tenant)? | This cross-tenant device-compliance gap is the architectural root cause of the non-prod block. |
| Q6 | Is your token handling compatible with **Entra Token Protection / token binding**? Would enforcing a token-binding CA control break your flow? | We intend to deny device-unbound tokens; we need to know if that affects your design. |
| Q7 | For one **successful** and one **failing** transaction, can you share the redacted `/authorize` and `/token` requests — in particular whether the `/token` POST carries an **`Origin` header** (browser) or not (server-to-server), the `grant_type`, `client_id`, redirect URI, and client-authentication method? Send **presence indicators or SHA-256 hashes only** for `code`, `code_verifier`, `refresh_token`, `assertion`, `client_secret`, and `client_assertion` — never raw values. | The `Origin` header is the single fact that distinguishes a browser redemption (consistent with your `spa` registrations) from a genuine server-side redemption (which would need a `web` confidential-client registration). It classifies the grant without any credential exposure. |
| Q8 | Can you confirm the **complete list of app registrations and service principals** GPD Central uses across environments — including any **confidential `web`/API registration** not among the three SPA exports we hold? | The three exports are public SPA clients only. If a real backend redeems the code, it must authenticate as a separate registration we have not yet seen. |
| Q9 | Do **Central** and **Conseiller** use the **same** client, redirect URI, backend, and token handling, or do they differ? | You asked whether they behave the same way; if they diverge, we must scope any accommodation per product. |

---

## 3. Evidence to request from Croesus

* A written/sequence-diagram **definition of the post-login token flow** (both interactive and server-side legs).
* One **successful** and one **failing** correlated transaction, redacted per Q7 (parameter names and non-secret identifiers preserved; secrets and raw tokens replaced with presence indicators or stable SHA-256 fingerprints). Please include, where safely available: `iss`, `aud`, `azp`/`appid`, `scp`/`roles`, `iat`, `exp`, `uti`/`jti`, and `cnf` presence for both the token returned by `/token` and the token sent to Microsoft Graph, plus the Graph URL, HTTP result, `request-id`, and `client-request-id`.
* The **confidential-client configuration** (appId, credential type, target scope/audience) if an OBO or server-side redemption is intended, and the **complete app-registration / service-principal inventory** (Q8).
* Whether **Central and Conseiller** share or diverge in client, redirect, backend, and token handling (Q9).
* The **complete AWS egress IP range list** for the GPD Central backend per environment.

> Please do **not** send raw authorization codes, PKCE verifiers, access or refresh tokens, client secrets, certificates, private keys, or cookies. Presence indicators and SHA-256 fingerprints are sufficient for every field above.

---

## 4. What we will do with the answers

* **Classify the grant first** from the captured `/token` request (Q7). An authorization-code grant with a matching PKCE verifier, followed by Graph use of the newly returned token, supports an ordinary authorization-code-with-PKCE design; `assertion` plus `requested_token_use=on_behalf_of` establishes OBO; the same bearer fingerprint crossing the boundary without a fresh issuance would indicate relay or replay.
* If a **true OBO is intended**: we will require an **exposed-API scope + confidential-client credential** on the Croesus backend, then apply a scoped internal accommodation.
* If a **server-side redemption is intended**: note that a `spa`-platform registration cannot service a no-`Origin` server-side redemption; the supported shape is a **`web` confidential-client** registration with a certificate. We will then enforce a **Token Protection / token-binding CA control** and apply the cross-tenant device-trust accommodation only for the legitimate non-prod path.
* Either way, we will prefer **Entra B2B "Trust compliant devices"** (addresses the root cause) over a broad CA exception, and we will keep any accommodation **scoped to the non-prod app**. We will not change Conditional Access, allowlist AWS IPs, or mandate OBO before the grant is classified.

---

*Reference analysis: `assets/app-registration-analysis-findings.md`. Verification steps: `assets/app-registration-verification.md`.*
