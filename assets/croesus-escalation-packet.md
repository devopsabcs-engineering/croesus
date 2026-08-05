# Croesus / GPD Central — Vendor Escalation Packet

**To:** Croesus (GPD Central / Central GPD vendor team)
**From:** Desjardins identity / security
**Date:** 2026-06-15 (updated 2026-08-05)
**Subject:** Authoritative definition of the GPD Central server-side token flow and AWS egress ranges
**Scope:** **GPD Central only.** Conseiller is a separate product and is not covered by anything in this document.

---

## 0. Update — what Croesus has since confirmed (read with section 1)

Several facts have landed since the first draft. Most of them close questions; one opens a new one that is ours to answer, not yours.

**A. GPD Central is a server-side multi-page application, not a browser SPA.** Croesus confirms Central is roughly 130 server-rendered ASP.NET (`.aspx`) pages. This is the most useful fact we have received, because it explains the shape of everything else.

**B. The `/token` redemption runs server-side.** Croesus states it, and a Desjardins browser trace (HAR) of a real Central sign-in corroborates it: the capture contains **only `/oauth2/v2.0/authorize`** and **no `/oauth2/v2.0/token`**. The authorization code leaves the browser and is redeemed off the user's machine. The browser-versus-server half of **Q7** is closed, and the `Origin` fact now exists only on your outbound request.

**C. The failure is confined to our non-prod (Dev) tenant.** Prod is unaffected. Our managed workstations can only be joined to one tenant, and that tenant is **Prod**, so in the Dev tenant those same machines are unknown devices.

**D. A site-to-site VPN already exists between Croesus and Desjardins.** This matters more than it first appears, and section 4 puts it to work.

**E. Central runs on .NET Framework 4.5.2.** This changes how we are reading your effort estimate, and we address it directly below.

**F. All of this concerns Central, not Conseiller.** We have scoped this packet accordingly and we are not generalising any of it to your other products.

### What these facts change

**Central is already a Backend-for-Frontend; the registration simply does not say so.** A server-rendered application that redeems the authorization code on the server, keeps the tokens on the server, and hands the browser nothing but a session cookie **is** the BFF pattern. That is the shape Microsoft recommends for server-side web applications, and the Entra **`web`** platform exists to describe it. We are not asking Croesus to adopt a new architecture. We are asking the registration to declare the architecture Central already has.

**The `spa` platform on the three registrations is a type mismatch, not a description of Central.** A server-side web application is a **confidential client** and belongs under the Entra **`web`** platform with a certificate credential. The `spa` platform describes a public browser client that holds no credential and redeems its authorization code cross-origin. Central is not that. Today the registration and the application disagree about what Central is.

> [!IMPORTANT]
> **This is a registration change, not an application rewrite.** We have heard the concern about touching 130+ pages, and that is not what this asks for. Moving from `spa` to `web` affects the app registration and the single code path that redeems the authorization code: register the redirect URI under the `web` platform, add a certificate credential, send `client_assertion` on the `/token` POST, and drop any `Origin` header. The `.aspx` pages sit behind whatever session that module establishes and are untouched. If that estimate is wrong, **Q10** asks you to tell us where.

We expect a second objection internally at Croesus — that Central serves many customers — so we want to address it up front.

> [!NOTE]
> **This need not be Desjardins-specific, nor a big-bang migration.** Each customer tenant holds its own registrations, so the platform move is per-tenant and can be staged. If the confidential-client redemption sits behind a per-tenant configuration flag, Desjardins can move first while every other customer stays on the current path until you are ready. The code path is shared; the rollout does not have to be. The benefits are not Desjardins-specific either — every tenant gains a real workload identity, and refresh tokens stop being capped at 24 hours (**Q11**).

**The .NET Framework 4.5.2 fact changes how we read your estimate, and we want to say so plainly.** We had been reading "27 files, not an easy change" as caution. On 4.5.2 we think it is probably an accurate estimate, and we should have asked about the runtime sooner. There is no current `Microsoft.Identity.Web` on that framework, current MSAL.NET targets 4.6.2 and later, and ADAL — which does support 4.5 — was retired in June 2023. If Central's authentication sits on OWIN plus a retired library, then any change there is unsupported work on unsupported ground, and that is a reasonable thing to be careful about. **Q13** asks which library you are actually on, because that is what really sets the cost.

> [!TIP]
> **That said, the first step needs no library and no framework uplift.** A `web` confidential client can authenticate with a **client secret**, which is one additional form field on the `/token` POST. No MSAL, no `Microsoft.Identity.Web`, no new dependency, nothing that 4.5.2 cannot do today. That is enough to prove the shape works, in our Dev tenant only. Moving to a certificate assertion is the hardening step and can follow later on your own timeline. We would rather stage this than ask you for the whole thing at once.

**Q8 is now the primary ask.** Entra rejects a plain server-side redemption of a `spa` authorization code with `AADSTS9002327`. Central works in Prod, so the redemption is evidently **succeeding**. Only two explanations fit: the backend sends a synthetic `Origin` header on a server-to-server call, or it authenticates as a **`web` registration outside the three exports we hold**. Q8 distinguishes them; the client-authentication method in **Q7** confirms which.

**One thing the new facts do not yet explain — and it is ours to resolve.** The blocked leg is non-interactive, arrives from your AWS egress, and therefore carries **no device context at all** — in *either* tenant. A leg with no device claim fails a compliant-device requirement everywhere, not only in Dev. Prod nonetheless passes. So the prod/non-prod difference cannot rest on device posture alone. Either the blocked leg is not the one we believe it is, or **our two tenants scope Conditional Access differently for Central**. We will settle that from our own policy inventory and sign-in logs. It does not block your answers.

**Priority asks: Q8, then Q7, then Q10.** The remaining questions stay valuable for scoping.

---

## 1. Framing (read first)

This is a request for design clarification, **not** a defect report against your product or our Conditional Access policy.

Our Conditional Access policy is behaving **correctly by design**: in our non-prod (Dev) tenant it denies a non-interactive token request that arrives from an untrusted AWS IP with no compliant-device context. Our analysis of the three app registrations (dev-dev, dev-prod, prod-prod) confirms they **cannot perform a standards-compliant Entra On-Behalf-Of (OBO) flow** as configured — they have no client secret, no certificate, and no exposed API scope.

We want to be precise about what our logs do and do not prove. The blocked event shows Token Protection status "unbound" (code 1008). That status means the client is **not integrated with the platform broker** (Windows Account Manager) — a device- and session-binding signal — and does **not** by itself prove that an access token was replayed, nor does it identify the OAuth grant. The grant remains unclassified from a captured request, and we are not asserting one.

What has changed is the client shape. Your registrations are declared under the Entra `spa` (public client) platform, but Central is a **server-rendered multi-page application** that redeems the authorization code **on your backend**. Those two descriptions are incompatible: Entra only redeems a `spa`-platform authorization code from a cross-origin request carrying an `Origin` header, and rejects a plain server-side redemption with `AADSTS9002327`. Since Central works in Prod, the redemption is succeeding, so something outside the three exports is making it succeed. **Q8** and **Q7** identify what.

To choose the **most proportionate** internal accommodation, we need you to confirm your intended flow. The questions below give us exactly what we need.

---

## 2. Questions for Croesus

> **Priority:** Q8, Q7, Q10, and Q13 resolve the open items described in section 0. **Q12** is the fastest short-term unblock. The others remain useful for scoping the accommodation. All questions concern **Central**; **Q9** is the only one that touches Conseiller, and only to establish the boundary.

| # | Question | Why we need it |
| --- | --- | --- |
| Q1 | What is the **exact OAuth/OIDC flow** GPD Central performs after the user's interactive sign-in? Specifically, is the second, server-initiated `/token` call an **authorization-code-with-PKCE** redemption, an **On-Behalf-Of (OBO)** exchange, a **refresh-token** grant, or a **reuse/replay** of an existing token? | Determines whether any registration/credential change is needed at all, and which internal lever is proportionate. |
| Q2 | Is your backend a **confidential client** (does it hold a client secret or certificate)? If so, **which app registration / appId** does it authenticate as, and **against which scope or Application ID URI**? | A true OBO requires a confidential client + an exposed API. None of our three registrations have these, so a real OBO is currently impossible. |
| Q3 | What is the **audience (`aud`)** of the token your backend presents on the second request? Is it **Microsoft Graph**, or a **custom backend API**? | Our logs show Microsoft Graph as the resource. A real OBO would target your API, not Graph. |
| Q4 | What are your **published AWS egress IP ranges** for the GPD Central backend (all environments)? | Needed to scope any trusted-location CA exception precisely. We have observed `3.97.32.113` and `3.99.119.124`. |
| Q5 | Does your backend rely on the **device-compliance / "Azure AD joined" claims** carried in the user's token? Are you aware those claims are **tenant-specific** (a device compliant in our Prod tenant is not compliant in our Dev tenant)? | A server-side call carries no device context of its own, and cross-tenant trust settings only honour device claims on a **B2B guest** sign-in. Our managed workstations can be joined to only one tenant, and that is **Prod** — so in our Dev tenant they are unknown devices and no tenant setting can change that. A backend that depends on these claims cannot be accommodated in non-prod. |
| Q6 | Is your token handling compatible with **Entra Token Protection / token binding**? Would enforcing a token-binding CA control break your flow? | We intend to deny device-unbound tokens; we need to know if that affects your design. |
| Q7 | **(Priority)** For one **successful** and one **failing** transaction, can you share the redacted `/authorize` and `/token` requests — in particular **whether the `/token` POST carries an `Origin` header**, the `grant_type`, `client_id`, redirect URI, and client-authentication method? Send **presence indicators or SHA-256 hashes only** for `code`, `code_verifier`, `refresh_token`, `assertion`, `client_secret`, and `client_assertion` — never raw values. | The call is confirmed server-side, so this request exists **only on your side**. A server-to-server POST sets no `Origin` of its own: if one is present, your code is adding it, and that is how a `spa`-typed code is surviving redemption. The client-authentication method classifies the grant without exposing any credential. |
| Q8 | **(Priority)** Can you confirm the **complete list of app registrations and service principals** GPD Central uses across environments — including any **confidential `web`/API registration** not among the three `spa` exports we hold? | Central works in Prod, so a server-side redemption is succeeding against registrations that should reject it. Either a `web` registration exists that we have never been shown, or the `Origin` header is being synthesised. This question separates the two, and it is the fastest route to a supported configuration. |
| Q9 | Do **Central** and **Conseiller** share any app registration, redirect URI, backend, or token-handling code, or are they fully separate? | We understand Conseiller is a separate product and we are deliberately **not** generalising any of this to it. We need to know only whether the two share anything, so that we can be certain our scoping is correct. |
| Q10 | **(Priority)** Given Central is server-rendered, what specifically blocks registering it as a **`web` confidential client**? You have mentioned roughly 27 files. Of those, how many **acquire** tokens (redeem the authorization code, refresh) versus merely **consume** a token or session the auth module already produced? | You have told us an architectural change is not easy, and we do not want to assume our way past that. Only the acquiring code changes; consumers keep receiving what they receive today. Note that a **client secret** requires no library and no framework change, so the first step is smaller than it may appear. If the acquiring surface really is 27 files, that is worth knowing, because it changes our recommendation. |
| Q11 | Does Central depend on **refresh tokens** to maintain the user session, and if so over what lifetime? | Refresh tokens issued to a `spa`-platform client are capped at **24 hours** and cannot slide beyond it. A `web` confidential client is not capped that way. If long sessions matter to you, the current registration shape is working against you independently of our Conditional Access. |
| Q12 | Can the Central backend route its Microsoft Entra calls (`login.microsoftonline.com`) over the existing **site-to-site VPN**, or through a forward proxy on the Desjardins side, so that the `/token` request egresses from a **Desjardins-owned address** instead of an AWS public IP? | This is the most promising short-term workaround and it needs **no change to your application code**. It converts the problem from "Desjardins allowlists a vendor's public cloud IPs" into "Desjardins recognises its own network", which is a far smaller trust concession for us to make. See section 4. |
| Q13 | **(Priority)** Which **authentication library** does Central use for the OIDC sign-in and the `/token` redemption — OWIN (`Microsoft.Owin.Security.OpenIdConnect`) with ADAL, MSAL.NET, `System.IdentityModel`, or hand-rolled protocol code? And is a **.NET Framework uplift** already on your roadmap? | You have told us Central is on .NET Framework 4.5.2. Current MSAL.NET targets 4.6.2 and later, `Microsoft.Identity.Web` targets 4.7.2 and later, and ADAL was retired in June 2023. Knowing which of these you are on tells us whether the confidential-client change is genuinely constrained or merely unfamiliar, and lets us stop guessing at your effort estimate. |
| Q14 | Does Central currently force **TLS 1.2** for its outbound calls to Microsoft Entra (for example via `ServicePointManager.SecurityProtocol` or the `SchUseStrongCrypto` switch)? | .NET Framework 4.5.2 does not negotiate TLS 1.2 by default. Central plainly reaches Entra today, so you must already handle this, but we would like it confirmed rather than assumed, since Entra continues to retire older TLS versions. |

---

## 3. Evidence to request from Croesus

* A written/sequence-diagram **definition of the post-login token flow** (both interactive and server-side legs).
* One **successful** and one **failing** correlated transaction, redacted per Q7 (parameter names and non-secret identifiers preserved; secrets and raw tokens replaced with presence indicators or stable SHA-256 fingerprints). Please include, where safely available: `iss`, `aud`, `azp`/`appid`, `scp`/`roles`, `iat`, `exp`, `uti`/`jti`, and `cnf` presence for both the token returned by `/token` and the token sent to Microsoft Graph, plus the Graph URL, HTTP result, `request-id`, and `client-request-id`.
* The **confidential-client configuration** (appId, credential type, target scope/audience) if an OBO or server-side redemption is intended, and the **complete app-registration / service-principal inventory** (Q8).
* Whether **Central and Conseiller** share or diverge in client, redirect, backend, and token handling (Q9).
* The **authentication library and .NET Framework version** in use, and any planned uplift (Q13).
* The **complete AWS egress IP range list** for the GPD Central backend per environment.

> Please do **not** send raw authorization codes, PKCE verifiers, access or refresh tokens, client secrets, certificates, private keys, or cookies. Presence indicators and SHA-256 fingerprints are sufficient for every field above.

---

## 4. What we will do with the answers

* **Resolve the registration shape first**, from the inventory (Q8) and the client-authentication method in the captured `/token` (Q7). If a `web` confidential registration already exists, we scope the accommodation to it. If instead the redemption succeeds because your code adds an `Origin` header to a server-to-server call, we will ask you to move to a **`web` confidential client** (Q10) — the present arrangement depends on Entra continuing to accept a request its `spa` rules are written to reject.
* **Classify the grant** from the same capture. An authorization-code grant with a matching PKCE verifier, followed by Graph use of the newly returned token, supports an ordinary authorization-code design; `assertion` plus `requested_token_use=on_behalf_of` establishes OBO; the same bearer fingerprint crossing the boundary without a fresh issuance would indicate relay or replay.
* **Check our own tenant parity, in parallel and without waiting on you.** The blocked leg carries no device context in **either** tenant, yet Prod passes and Dev does not. Device posture alone therefore cannot be the whole explanation. We will compare the Conditional Access policies assigned to Central in both tenants, and read both sign-in logs to confirm which leg is actually being blocked. If the two tenants merely scope Central differently, the proportionate fix is ours and the accommodation is small.
* **Treat device trust as unavailable in non-prod, not merely unpreferred.** The cross-tenant access **inbound trust settings** ("Trust compliant devices", "Trust hybrid joined devices", "Trust MFA") take effect only when the user authenticates as a **cross-tenant B2B guest**, because they instruct the resource tenant to honour a device-compliance claim carried **in the guest's token from their home tenant**. They cannot make a Prod-compliant device count as compliant when the user signs in with a **Dev tenant member account** from that same machine: that sign-in is native to Dev and is evaluated against Dev's own device registry. Compounding this, a workstation can be joined to **only one tenant**, and ours are joined to **Prod** — so no Desjardins-managed machine will ever appear as a compliant device in Dev. In our non-prod tenant, "require compliant device" is an **unsatisfiable** grant for this user population, not a tunable one. *(Correction supplied by Desjardins, 2026-08-05.)*
* **Then apply the minimum lever.** We separate what unblocks non-prod quickly from what makes the design correct, because they are not the same work and do not need the same timeline.

  **Short term — no Croesus code change required:**

  1. **Route the Entra calls over the existing site-to-site VPN (Q12).** If the Central backend reaches `login.microsoftonline.com` through the tunnel, or through a forward proxy on our side, the `/token` request egresses from a **Desjardins-owned address**. That converts the Conditional Access question from *"will Desjardins allowlist a vendor's public cloud IPs?"* into *"will Desjardins recognise its own network?"* — a materially smaller concession, anchored to infrastructure we operate and reachable only across an authenticated IPsec tunnel. It remains IP-based trust and therefore a compensating control rather than a security boundary, but it is the best-shaped one available and it asks nothing of your application code.
  2. **Align non-prod Conditional Access scoping to Prod** for the Central application only, if our parity check shows the two tenants diverge.

  > [!NOTE]
  > A trusted named location does **not** satisfy a grant control. If our non-prod policy *grants* on "require compliant device", adding a trusted location changes nothing, because grant controls combine with AND. The location has to be used as a **condition** — excluding that location from the policy's scope for the non-prod Central application. We will confirm which shape our policy actually uses before promising an outcome.

  **Durable — the fix that removes the mismatch instead of working around it:**

  1. **Correct the registration shape** (Q8 / Q10 / Q13) so the backend authenticates as a workload holding its own credential. Stage it: a **client secret** proves the shape with no library change and nothing that .NET Framework 4.5.2 cannot do, and a certificate assertion hardens it afterwards on your timeline. Everything above is scaffolding around a registration that describes an application Central is not.
  2. **Re-shape non-prod so users authenticate as B2B guests homed in our Prod tenant.** The only configuration in which cross-tenant inbound trust settings can carry a Prod compliant-device claim into Dev. A genuine identity-model change on our side, and it does not fix the registration shape. Last resort.

* We will keep any accommodation **scoped to the non-prod Central application**. We will not change Conditional Access, allowlist AWS public IPs, or mandate OBO before the registration shape and the grant are settled, and we will not extend any of this to Conseiller.
* We will raise the **.NET Framework 4.5.2 support status** separately, through vendor risk review rather than through this escalation. It is a legitimate concern on its own merits and we do not intend to use it as leverage here.

---

*Working brief behind this packet: [`assets/croesus-3way-session-findings.md`](croesus-3way-session-findings.md). Reference analysis: `assets/app-registration-analysis-findings.md`. Verification steps: `assets/app-registration-verification.md`.*
