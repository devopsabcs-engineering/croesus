# Croesus / GPD Central — Three-Way Session Findings and Available Routes

**Participants:** Desjardins (identity / security) · Croesus (GPD Central vendor team) · Microsoft
**Recorded:** 2026-08-05
**Purpose:** One self-contained record of everything established in the three-way session, what it changed, what remains open, and every route available to us.

> [!IMPORTANT]
> **Scope: GPD Central only.** Every finding, conclusion, and route in this document concerns **Central**. **Conseiller is a separate product with a separate architecture**, and nothing here may be assumed to carry over to it. Conseiller needs its own assessment, and until that happens we should not describe any of this as a "Croesus" problem.

Within that scope, this document is authoritative.

> [!NOTE]
> This document supersedes the running commentary spread across the README, the findings analysis, and the evidence narrative. Read it alone and you are current. The [escalation packet](croesus-escalation-packet.md) remains the artifact we send to Croesus; this is the working brief behind it.

---

## 1. What the session established

Findings carry an ID so the rest of this document can reference them precisely.

| ID | Finding | Source |
| --- | --- | --- |
| F1 | GPD Central is a **server-rendered multi-page application**, roughly 130 ASP.NET (`.aspx`) pages. It is **not** a browser SPA. | Croesus |
| F2 | The `/oauth2/v2.0/token` redemption runs **server-side** on the Croesus AWS backend. | Croesus, corroborated by Desjardins HAR |
| F3 | Reworking the authentication is described as touching roughly **27 files**, and Central serves **other customers**, so a Desjardins-specific change is unattractive. | Croesus |
| F4 | The AWS servers sit **behind a VPN**. | Croesus |
| F5 | A **site-to-site VPN already exists** between Croesus and Desjardins. | Desjardins (Mathieu Santerre) |
| F6 | The failure occurs **only in the non-prod (Dev) tenant**. Prod is unaffected. | Desjardins |
| F7 | Desjardins-managed workstations can be joined to **only one tenant**, and that tenant is **Prod**. In the Dev tenant those same machines are unknown devices. | Desjardins |
| F8 | Cross-tenant inbound trust settings ("Trust compliant devices", "Trust hybrid joined devices", "Trust MFA") apply **only to B2B guest sign-ins**. | Desjardins (Mathieu Santerre) |
| F9 | A browser HAR of a real Central sign-in contains **only `/authorize`** and **no `/token`**. | Desjardins |
| F10 | All of the above concerns **Central**. **Conseiller is a separate product** and a separate assessment. | Croesus |
| F11 | Central's server-rendered layer runs on **.NET Framework 4.5.2**. | Croesus |

### The decisive one

**F1 is the finding that reorganises everything else.** Every prior document in this repository reasoned about Central as though the `spa` platform on its app registrations described the application. It does not. It describes an application Central is not.

**F11 is the finding that explains the resistance.** It reframes F3 from reluctance into constraint, and it is treated at length in section 2.

---

## 2. What these findings changed

| ID | Consequence |
| --- | --- |
| A1 | The `spa` platform on all three registrations is a **type mismatch**, not a description. A server-rendered app that redeems its code server-side is a **confidential client** and belongs under the Entra **`web`** platform with a certificate. |
| A2 | **Central is already a Backend-for-Frontend.** Tokens live on the server, the browser holds a session cookie. That is the pattern Microsoft recommends for server-side web apps, and `web` is the platform that describes it. Nobody is being asked to adopt a new architecture, only to declare the existing one. |
| A3 | Entra rejects a plain server-side redemption of a `spa` code with `AADSTS9002327`. **Central works in Prod**, so the redemption is **succeeding**. Exactly two explanations survive: the backend **synthesises an `Origin` header**, or it authenticates as a **`web` registration outside the three exports we hold**. |
| A4 | **The prod/non-prod split is not yet explained.** See below. |
| A5 | Refresh tokens issued to a `spa`-platform client are capped at **24 hours** and cannot slide. A `web` confidential client is not capped that way. The current shape is costing Croesus session longevity independently of Conditional Access. |
| A6 | A trusted named location does **not** satisfy a grant control. Grant controls combine with **AND**. Location has to be used as a **condition** (an exclusion from policy scope), not as a grant. |
| A7 | **.NET Framework 4.5.2 left support on 26 April 2022.** Central runs on an unsupported runtime that receives no security fixes. This is a finding in its own right, independent of SSO. |
| A8 | 4.5.2 does not negotiate **TLS 1.2 by default**. Central reaches Entra today, so Croesus must already be forcing it, but the platform is exposed as Entra retires older TLS. |
| A9 | **Modern Microsoft authentication libraries do not run on 4.5.2.** Current MSAL.NET targets 4.6.2 and later; Microsoft.Identity.Web targets 4.7.2 and later; ADAL, which does support 4.5, was **retired in June 2023**. Croesus is therefore on OWIN with a retired library or on hand-rolled protocol code. |
| A10 | **The 27-file estimate is probably honest.** F11 reframes F3 from commercial reluctance into technical constraint, and that should change our posture. |

### A4 in full, because it is actionable and it is ours

The blocked leg is non-interactive, arrives from the Croesus AWS egress, and therefore carries **no device context at all**. That is true in **both** tenants. A leg with no device claim fails a compliant-device requirement everywhere, not only in Dev.

Yet Prod passes and Dev does not.

Device posture alone therefore cannot be the whole story, and F7 (however true) does not by itself close the gap. Exactly one of these must hold:

1. **The blocked leg is not the one we believe it is.** It may be the interactive `/authorize` leg from the workstation, where F7 bites directly and completely.
2. **The two tenants scope Conditional Access differently for Central.** Prod may simply not apply a compliant-device grant to that application, while Dev does.

Both are answerable from Desjardins' own policy inventory and sign-in logs, with no dependency on Croesus. If explanation 2 holds, the proportionate fix is entirely internal and small. **This is the highest-value thing Desjardins can do this week.**

### What .NET Framework 4.5.2 changes

F11 arrived last and matters more than its size suggests.

**It makes the vendor's objection credible.** We had been reading F3 ("27 files, not an easy change") as commercial reluctance. On 4.5.2 it is likelier to be an accurate estimate. There is no `Microsoft.Identity.Web`, no current MSAL, no dependency injection, and quite possibly no test harness. If the authentication is OWIN middleware plus ADAL, then ADAL is a **retired** library and any change to it is unsupported work on unsupported ground. That is a genuinely uncomfortable place for a vendor to be asked to make a security-sensitive change, and we should say so out loud rather than press harder.

**It does not, however, block the fix.** Three things remain true on 4.5.2:

1. A **client secret** requires no library at all. It is one extra form field on the `/token` POST. This is the cheapest possible way to prove a `web` confidential client works, and it can be done in an afternoon.
2. A **certificate assertion** is a signed JWT. `System.IdentityModel.Tokens.Jwt` runs on 4.5, and RS256 signing is available. Fiddlier than a secret, but not blocked.
3. The **`.aspx` pages are untouched either way.** They consume whatever session the auth module establishes.

So the honest sizing is: *the protocol change is small even on 4.5.2; the supported, maintainable version of it wants a framework uplift.* Those are two different conversations and we should not let the second hold the first hostage.

**It is also a separate finding Desjardins should register.** Independent of this escalation, a vendor-supplied application handling Desjardins identity is running on a runtime that has received no security patches since April 2022. That belongs in vendor risk review whatever happens with the SSO issue. Raise it as a distinct item, not as leverage in this negotiation.

---

## 3. What is still open

| # | Open question | Owner | Blocks |
| --- | --- | --- | --- |
| Q7 | Redacted `/token` capture: is an `Origin` header present, what is the `grant_type`, and what client-authentication method is used? | Croesus | Grant classification |
| Q8 | Complete app-registration and service-principal inventory, including any confidential `web` registration outside the three exports. | Croesus | A3 |
| Q10 | Of the ~27 files, how many **acquire** tokens versus merely **consume** what the auth module produced? | Croesus | Sizing R4 |
| Q11 | Does Central depend on refresh tokens, and over what lifetime? | Croesus | A5 leverage |
| Q12 | Can Central route its Entra calls over the site-to-site VPN or a Desjardins-side proxy? | Croesus | R1 |
| Q4 | Complete AWS egress ranges per environment. | Croesus | R3 only |
| Q13 | Which authentication library does Central use (OWIN + ADAL, hand-rolled, other), and is a framework uplift already on the roadmap? | Croesus | Sizing R4 realistically |
| Q9 | Does Conseiller share any of Central's registrations, backend, or auth code? | Croesus | Scope boundary (F10) |
| — | Which leg is actually blocked, and do the two tenants scope CA differently for Central? | **Desjardins** | A4, R2 |
| — | Does Entra accept a `spa` code redeemed server-side with a **synthesised** `Origin`? | **Microsoft / demo tenant** | A3, without waiting on Croesus |
| — | Vendor risk review of an unsupported runtime (A7), tracked separately from this escalation. | **Desjardins** | Nothing here |

---

## 4. Routes and workarounds

Sized relatively. "Croesus code change" is the column that usually decides how fast something can happen.

| ID | Route | Who acts | Croesus code change | Size | Durability |
| --- | --- | --- | --- | --- | --- |
| **R1** | Route Entra calls over the **site-to-site VPN** or a Desjardins forward proxy | Both | **None** | Small | Workaround |
| **R2** | **Align non-prod CA scoping to Prod** for the Central app only | Desjardins | None | Small | Possibly the actual fix |
| **R3** | Scoped CA exception using an **AWS named location** | Desjardins | None | Small | Weakest workaround |
| **R4** | **Correct the registration shape**: `spa` → `web` confidential client | Both | Yes, narrow | Medium | **Durable fix** |
| **R5** | **Workload identity federation** from AWS, no stored credential | Both | Yes, narrow | Medium | Durable, best-in-class |
| **R6** | Re-shape non-prod so users authenticate as **B2B guests** homed in Prod | Desjardins | None | Large | Fixes device claims only |
| **R7** | Consolidate non-prod into a single tenant | Desjardins | None | Large | Last resort |
| **R8** | **Uplift Central from .NET Framework 4.5.2** to a supported version | Croesus | Yes, broad | Large | Prerequisite for R5, and a standalone risk item |

### R1 — Route the Entra calls over the existing VPN

The most promising short-term move, and the one Mathieu surfaced. If the Central backend reaches `login.microsoftonline.com` through the tunnel or through a forward proxy on the Desjardins side, the `/token` request egresses from a **Desjardins-owned address**.

That reframes the entire Conditional Access conversation. It stops being *"will Desjardins allowlist a vendor's public cloud IPs?"* and becomes *"will Desjardins recognise its own network?"* The second question is far easier to answer yes to, because the address belongs to infrastructure Desjardins operates, reachable only across an authenticated IPsec tunnel.

Honest caveats:

* It is still IP-based trust, which makes it a **compensating control, not a security boundary**.
* Per **A6**, this only works if the CA policy uses location as a **condition**. If the policy *grants* on compliant device, a trusted location changes nothing.
* Routing all of `login.microsoftonline.com` over a tunnel by IP is awkward, since the endpoint is CDN-fronted with large, changing ranges. An **explicit forward proxy** on the Desjardins side is the cleaner shape than route-table entries.

### R2 — Check tenant parity first

Follows directly from **A4**. Compare the CA policies assigned to Central in Prod and Dev. If Prod simply does not apply the grant that Dev applies, the disparity is configuration drift between environments and the fix is internal, immediate, and needs nothing from Croesus.

Do this before committing to any other route. It costs almost nothing and it may dissolve the problem.

One discipline point: if the parity check shows Prod is more permissive, the correct response is to confirm Prod's posture is intentional, not to assume Dev is wrong.

### R3 — AWS named location

Works, but it is the weakest form of R1: Desjardins would be extending trust to a vendor's public cloud addresses rather than to its own network. Requires Q4. Keep it as the fallback if R1 proves impractical, and do not extend it to Prod.

### R4 — Correct the registration shape

The only route that removes the mismatch rather than working around it. Stage it in two steps, because F11 makes the second step more expensive than the first:

**Step 1, prove it with a secret.** Register the redirect URI under the **`web`** platform, add a client **secret**, and send `client_id` plus `client_secret` on the `/token` POST. This needs **no library, no framework uplift, and no new dependency** on .NET Framework 4.5.2. It is one additional form field. Do this in Dev only, and it settles the argument empirically.

**Step 2, harden it with a certificate.** Replace the secret with a certificate credential and send `client_assertion`. Still feasible on 4.5.2 via `System.IdentityModel.Tokens.Jwt`, though this is the point at which a framework uplift (R8) starts paying for itself.

Either way, drop any synthesised `Origin` header.

Against F3 and F11, three points matter:

* **Only the acquiring code changes.** The `.aspx` pages consume whatever session the auth module establishes. They do not change. Q10 asks Croesus how much of the 27-file surface actually acquires tokens; Q13 asks which library is involved, because that is what really sets the cost.
* **It need not be Desjardins-specific.** Registrations are per customer tenant, so the platform move is per-tenant. Behind a per-tenant configuration flag, Desjardins migrates first and every other customer stays on the current path.
* **It is not a favour to Desjardins.** Every Croesus tenant gains a real workload identity, the 24-hour refresh cap disappears (A5), and Croesus stops depending on Entra continuing to tolerate a request its `spa` rules are written to reject (A3).

### R8 — Framework uplift

Not a route to fixing the SSO issue, and we should be careful not to present it as one. R4 step 1 does not need it.

It appears here for two reasons. It is a **prerequisite for R5** and for any use of current Microsoft authentication libraries. And per A7 it is a **standalone vendor-risk item**: Central handles Desjardins identity on a runtime that has been out of support since April 2022. Raise it in vendor risk review on its own merits, on its own timeline, and deliberately not as leverage in this escalation.

### R5 — Workload identity federation

A variant of R4 worth raising because it removes credential management entirely. Entra supports federation from external OIDC-compliant issuers, so an AWS-hosted workload may be able to authenticate with a federated token instead of a stored certificate.

Treat this as directional rather than actionable today. It needs current MSAL.NET, which does not run on .NET Framework 4.5.2 (A9), so R8 gates it. Raise it as the destination, not the next step.

### R6 and R7 — identity-model changes

R6 makes non-prod users **B2B guests homed in Prod**, which is the only configuration where cross-tenant inbound trust settings (F8) can carry a Prod compliant-device claim into Dev. It is a genuine identity-model change on the Desjardins side, and it does **not** fix the registration shape. R7 consolidates tenants outright. Both are last resorts, listed for completeness.

---

## 5. Settling the open questions without waiting

Two experiments are available to us using our own demo tenant. Neither needs Croesus.

### Experiment A — the redemption probe (recommended first)

Provision two registrations that differ only in platform, then attempt a server-side redemption of a real authorization code three ways:

| Attempt | Setup | Expected | What it tells us |
| --- | --- | --- | --- |
| 1 | `spa` code, **no** `Origin` | `AADSTS9002327` | Confirms the documented behaviour |
| 2 | `spa` code, **synthesised** `Origin` | Unknown | **The decisive test** |
| 3 | `web` code + `client_assertion` | Success | Confirms the supported path |

Attempt 2 is the point of the exercise. If Entra accepts a spoofed `Origin`, we have explained how Croesus Prod works today and **A3 collapses from two explanations to one**, which effectively answers Q8 by deduction. If Entra rejects it, an undisclosed `web` registration must exist and we can state that as fact rather than ask. Recording the refresh-token lifetime from attempts 2 and 3 also demonstrates A5 directly.

Requires one interactive sign-in per registration to capture a real code, so this is a semi-interactive script rather than a fully automated one.

### Experiment B — a server-rendered mock

Build a multi-page, server-rendered BFF that mirrors Central's shape and run it against both registration types.

A literal ASP.NET Web Forms mock on .NET Framework is possible, and F11 makes it more tempting than before. Resist it as a first move: **Entra cannot distinguish Web Forms from Razor Pages.** It observes a server-side POST to `/token`, with or without a credential, with or without `Origin`. ViewState, postbacks, and the runtime version contribute nothing to the evidence. A Razor Pages MPA on the existing Linux plan reproduces the identical OAuth surface at a fraction of the cost.

Reach for a genuine `.aspx` build on 4.5.2 only if Croesus disputes the equivalence, or if the point being tested becomes *framework* feasibility rather than *protocol* feasibility. Those are different claims and only the second one needs the matching runtime.

---

## 6. Recommended sequence

1. **Run the tenant parity check (R2).** Internal, immediate, and it may dissolve the problem outright.
2. **Run Experiment A.** Answers Q8 by deduction from our own tenant, before Croesus replies.
3. **Ask Croesus Q12 (VPN routing) in parallel.** It is the fastest unblock that needs no vendor code change.
4. **Send the remaining questions** (Q7, Q8, Q10, Q11, Q13, Q9) via the [escalation packet](croesus-escalation-packet.md).
5. **Apply R1 or R2** as the short-term unblock for non-prod, scoped to the non-prod application only.
6. **Propose R4 step 1 (secret, Dev only)** as the durable fix's cheapest first move, staged per-tenant so Croesus carries no multi-customer risk.
7. **Track R8 and the Conseiller assessment separately**, on their own timelines, outside this escalation.

---

## 7. Guardrails

Until the registration shape and the grant are settled:

* Do **not** change Conditional Access outside a scoped, non-prod, single-application exception.
* Do **not** allowlist AWS public IPs while R1 remains viable.
* Do **not** mandate On-Behalf-Of. Nothing yet establishes that a middle-tier requirement exists, and OBO is not required for ordinary authorization-code redemption.
* Do **not** treat `Unbound (1008)` as replay evidence. It is a device and session binding status, it does not identify a grant, and Token Protection does not cover Microsoft Graph.
* Do **not** extend any non-prod accommodation to Prod.
* Do **not** generalise any of this to **Conseiller**. It is a separate product and a separate assessment (F10).
* Do **not** use the unsupported-runtime finding (A7) as leverage in this escalation. It is a legitimate risk item and it deserves its own track.

---

## 8. Related documents

* [Vendor escalation packet](croesus-escalation-packet.md) — the artifact sent to Croesus, carrying Q1 through Q12.
* [App registration analysis findings](app-registration-analysis-findings.md) — the registration-level evidence.
* [App registration verification](app-registration-verification.md) — reproduction steps.
* [Evidence narrative](../docs/evidence-narrative.md) — what the evidence proves and what it does not.
* [OBO demo guide](../docs/obo-demo-guide.md) — the reference implementation.
* [Repository README](../README.md) — overall analysis entry point.
