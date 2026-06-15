# Croesus / GPD Central — Vendor Escalation Packet

**To:** Croesus (GPD Central / Central GPD vendor team)
**From:** Desjardins identity / security
**Date:** 2026-06-15
**Subject:** Authoritative definition of the GPD Central server-side token flow and AWS egress ranges

---

## 1. Framing (read first)

This is a request for design clarification, **not** a defect report against your product or our Conditional Access policy.

Our Conditional Access policy is behaving **correctly by design**: in our non-prod (Dev) tenant it denies a non-interactive token request that arrives from an untrusted AWS IP with no compliant-device context. Our analysis of the three app registrations (dev-dev, dev-prod, prod-prod) confirms they **cannot perform a standards-compliant Entra On-Behalf-Of (OBO) flow** as configured — they have no client secret, no certificate, and no exposed API scope. Our raw Entra sign-in logs show the blocked event as a **server-side token replay** (Token Protection status "unbound", code 1008) targeting Microsoft Graph.

To choose the **most proportionate** internal accommodation, we need you to confirm your intended flow. The questions below give us exactly what we need.

---

## 2. Questions for Croesus

| # | Question | Why we need it |
| --- | --- | --- |
| Q1 | What is the **exact OAuth/OIDC flow** GPD Central performs after the user's interactive sign-in? Is the second, server-initiated token request an intended **On-Behalf-Of (OBO)** exchange, or a **server-side reuse/replay** of the user's token? | Determines whether any registration/credential change is needed at all, and which internal lever is proportionate. |
| Q2 | Is your backend a **confidential client** (does it hold a client secret or certificate)? If so, **which app registration / appId** does it authenticate as, and **against which scope or Application ID URI**? | A true OBO requires a confidential client + an exposed API. None of our three registrations have these, so a real OBO is currently impossible. |
| Q3 | What is the **audience (`aud`)** of the token your backend presents on the second request? Is it **Microsoft Graph**, or a **custom backend API**? | Our logs show Microsoft Graph as the resource. A real OBO would target your API, not Graph. |
| Q4 | What are your **published AWS egress IP ranges** for the GPD Central backend (all environments)? | Needed to scope any trusted-location CA exception precisely. We have observed `3.97.32.113` and `3.99.119.124`. |
| Q5 | Does your backend rely on the **device-compliance / "Azure AD joined" claims** carried in the user's token? Are you aware those claims are **tenant-specific** (a device compliant in our Prod tenant is not compliant in our Dev tenant)? | This cross-tenant device-compliance gap is the architectural root cause of the non-prod block. |
| Q6 | Is your token handling compatible with **Entra Token Protection / token binding**? Would enforcing a token-binding CA control break your flow? | We intend to deny "unbound" (replayed) tokens; we need to know if that affects your design. |

---

## 3. Evidence to request from Croesus

* A written/sequence-diagram **definition of the post-login token flow** (both interactive and server-side legs).
* The **confidential-client configuration** (appId, credential type, target scope/audience) if an OBO is intended.
* The **complete AWS egress IP range list** for the GPD Central backend per environment.
* Confirmation of whether the **token replay is by design** or an implementation defect.

---

## 4. What we will do with the answers

* If Croesus confirms a **true OBO is intended**: we will require an **exposed-API scope + confidential-client credential** on the Croesus backend, then apply a scoped internal accommodation.
* If Croesus confirms a **server-side replay is intended**: we will enforce a **Token Protection / token-binding CA control** and apply the cross-tenant device-trust accommodation only for the legitimate non-prod path.
* Either way, we will prefer **Entra B2B "Trust compliant devices"** (addresses the root cause) over a broad CA exception, and we will keep any accommodation **scoped to the non-prod app**.

---

*Reference analysis: `assets/app-registration-analysis-findings.md`. Verification steps: `assets/app-registration-verification.md`.*
