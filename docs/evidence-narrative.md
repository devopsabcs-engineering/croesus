---
title: Croesus OBO Evidence Narrative
description: Maps the Desjardins escalation-packet questions to the concrete evidence the mock OBO demo produces and explains why audience-binding is the headline proof
author: Croesus Demo Team
ms.date: 2026-08-05
ms.topic: concept
keywords:
  - obo
  - evidence
  - entra
  - token protection
  - audience binding
estimated_reading_time: 8
---

## Purpose

The escalation packet in [../assets/croesus-escalation-packet.md](../assets/croesus-escalation-packet.md) asked Croesus six questions to settle one dispute: is the second, server-initiated token request a standards-compliant On-Behalf-Of (OBO) exchange, or a server-side replay of the user's token? This document answers each question with the concrete evidence the demo produces, and explains why the audience boundary, not Token Protection event code 1008, is the proof that decides the case.

## The headline proof: audience-binding

A real OBO produces two different tokens. The middle tier receives a token whose audience is the API, then exchanges it for a brand-new token whose audience is Microsoft Graph. At the protocol level the second token carries a fresh `jti` and `iat`; a replay reuses one token, with one audience and one `jti`. The demo does not decode token B, so it evidences the distinct issuance through the OBO correlation id, token source, and expiry rather than by reading token B's `jti`.

The demo makes that boundary observable in three reinforcing ways:

* The Application Insights claim logs show leg 1 with `aud == API`. Leg 2 records `aud == Microsoft Graph` by construction (the API does not decode token B), and distinct issuance is shown through the OBO correlation id, token source, and expiry rather than a decoded `jti`.
* The negative tests show that neither token is honored across the boundary. A Graph-audience token presented to the API is rejected locally with `401` by the audience middleware. The reverse direction, the API-audience token sent to Graph, is expected to return `401` by audience validation; that outcome is asserted structurally from how Entra audiences tokens rather than proven by a captured live Graph call in this document.
* The Entra non-interactive sign-in logs show two correlated legs sharing a `CorrelationId` but naming two different resources.

This is strong evidence because it is intrinsic to the protocol: a single reused token cannot present two distinct audiences, and the middle tier obtains token B through a fresh OBO acquisition rather than forwarding token A. The negative tests close the loop by proving the boundary is enforced rather than incidental.

## Question-by-question mapping

| Question | What the demo shows | Where to see it |
| --- | --- | --- |
| Q1: Is the second request an OBO exchange or a server-side replay? | Two tokens with distinct audiences, issued by the OBO exchange: token A is audienced to the API and token B is acquired fresh for Microsoft Graph. The gated Tier 2a control shows the contrast directly: one token forwarded from server-side context, with no fresh issuance. | App Insights leg 1 and leg 2 claim logs, plus the distinct `ReplayAttempt` event; [obo-demo-guide.md](obo-demo-guide.md) steps 5 and 7. |
| Q2: Is the backend a confidential client, and which app and scope? | The API authenticates as a confidential client using a certificate in Key Vault, exposes `access_as_user`, and is pre-authorized for the SPA. | Provisioning output and [configuration-contract.md](configuration-contract.md); the OBO request shape in the App Insights log records the credential source and name (the credential `SourceType` and the Key Vault certificate name), not a thumbprint. |
| Q3: What audience does the second token carry? | Leg 2 targets Microsoft Graph through a freshly issued token, distinct from the API-audience token of leg 1. | App Insights leg 2 claim log (`aud == Microsoft Graph`); KQL row with `ResourceDisplayName == "Microsoft Graph"`. |
| Q4: What are the backend egress IP ranges? | Out of scope for the demo. The demo proves the flow shape; egress ranges remain a vendor-supplied fact for the Conditional Access exception. | Vendor input; tracked in the escalation packet, not reproduced here. |
| Q5: Does the backend rely on tenant-specific device-compliance claims? | The corrected flow depends on the OBO exchange, not on relaying device-compliance claims. The single-tenant design keeps every leg intra-tenant. | Architecture in [obo-demo-guide.md](obo-demo-guide.md); tenancy section of the same guide. |
| Q6: Is the token handling compatible with token binding or Token Protection? | The OBO flow issues a new token per leg, so a token-binding control denies replays without breaking a correct OBO. Tier 2b reproduces the real 1008 "unbound" signal on a supported resource (Exchange Online) with a report-only policy, kept as a labeled exhibit rather than the proof. | Negative tests; the report-only policy and Query 4; the Token Protection section below. |

## Token Protection 1008: a labeled Tier 2b exhibit, not the OBO proof

The original analysis observed the blocked event as a Token Protection "unbound" signal, event code 1008. It is tempting to make code 1008 the centerpiece, but presenting it as the OBO proof would be technically incorrect for this application shape and would undermine credibility with a security-literate customer.

Token Protection token binding, and therefore the 1008 "unbound" signal, applies to native-application clients reaching specific resources: Exchange Online, SharePoint Online, and Teams. It does not fire for a browser-based SPA calling a custom API that then calls Microsoft Graph. Asserting 1008 as the OBO proof for this app shape would claim a signal the platform does not emit here.

Tier 2b reproduces the customer's exact telemetry on the terms the platform supports. It stands up a report-only Conditional Access Token Protection policy (a Microsoft Entra ID P1 capability) scoped to a native mobile-and-desktop client reaching Exchange Online, then reads the resulting `signInSessionStatusCode == "1008"` from the non-interactive sign-in logs (Query 4 in [../scripts/evidence-kql.kusto](../scripts/evidence-kql.kusto)). Report-only mode records the binding evaluation without blocking anyone, so the exhibit is safe to run and trivial to reverse. This is the real 1008, obtained on a supported resource, and it stays clearly separate from the OBO proof.

The audience boundary carries the argument on its own. Two distinct audiences and enforced rejection in both directions demonstrate a standards-compliant OBO regardless of whether any Token Protection signal is present. Tier 2a adds the mirror image: a gated server-side replay that forwards a real Graph token from the middle tier and emits a distinct `ReplayAttempt` event, reproducing the replay shape the customer's logs attribute to the vendor backend so the wrong and right flows sit side by side.

## Registration correctness

> [!IMPORTANT]
> Updated 2026-08-05 after the three-way session with Croesus. **Scope: GPD Central only.** The authoritative current analysis is [../assets/croesus-3way-session-findings.md](../assets/croesus-3way-session-findings.md).

The three exported registrations (`dev-dev`, `dev-prod`, `prod-prod`) declare their redirect URIs under the Microsoft Entra `spa` (public-client) platform node, with no client secret, no certificate, and no exposed API scope. That shape is correct for a browser-driven authorization-code redemption, where the browser redeems the code cross-origin with an `Origin` header.

Croesus reports that Central runs on .NET Framework 4.5.2 with roughly 130 `.aspx` pages, uses one URL for functionality, has a BFF, and redeems `/token` on the backend. Croesus has also alternated between SPA and multi-page descriptions. The UI topology is therefore unresolved and may be SPA, multi-page Web Forms, or hybrid. A single visible URL, `.aspx` redirect paths, and PKCE do not classify it. PKCE is recommended for public and confidential authorization-code clients.

A Desjardins browser trace contains only `/oauth2/v2.0/authorize` and no `/oauth2/v2.0/token`. That proves `/token` did not occur in the sampled browser transaction. It does not prove the UI is not a SPA or independently identify the redeemer. SPA and BFF are compatible: a JavaScript SPA can be served by .NET Framework 4.5.2 while a backend retains OAuth tokens and the browser holds only a session cookie. Treat the BFF label as provisional until Q15 confirms that browser JavaScript receives no OAuth tokens, the session cookie is appropriately protected, and downstream API calls are mediated by the backend.

The registration conclusion follows from who redeems the code. If the same backend redeems the authorization code and retains a credential and tokens for the browser session, that component is a confidential client and belongs under the Entra `web` platform regardless of whether the UI is SPA, multi-page, or hybrid. If the complete Q8 inventory identifies a separate browser public client, its `spa` registration may be legitimate.

Microsoft Entra enforces the distinction at the token endpoint. A `spa` authorization code may only be redeemed by a cross-origin browser request; a plain server-side redemption is rejected:

> Tokens issued for the 'Single-Page Application' client-type may only be redeemed via cross-origin requests. (`AADSTS9002327`)

If Croesus's reported backend redemption is confirmed, Central's Prod success leaves two explanations: the backend synthesises an `Origin` header on a server-to-server call, or it authenticates as a **`web` registration outside the three exports we hold**. The decisive artifacts are a captured `/token` request, the complete Q8 registration inventory, and the Q15 component-boundary evidence. A confirmed backend redeemer should use a `web`-platform confidential client, provable first with a client secret and hardened afterwards with a certificate credential.

One guard belongs alongside this verdict: `1008` is out of Token Protection scope for any flow reaching Microsoft Graph and is not replay evidence. Token Protection supports native applications only and does not cover Microsoft Graph, so an `Unbound (1008)` line against a sign-in that later reaches Graph is expected and benign. Keep that statement in view so the analysis does not drift back to treating `1008` as proof of token replay.

## Reversibility and residual token validity

Every Tier 2 change is reversible, and the teardown scripts restore the tenant to its prior state (see the demo guide, step 8). One honest caveat matters for the security conversation: revoking the SPA-to-Graph delegated grant stops the SPA from acquiring new Graph tokens, but it does not invalidate Graph access tokens that were already issued. Those remain valid until their natural expiry, which is short (on the order of an hour). Revocation cuts off future issuance and refresh, not the lifetime of a token already in hand. That property is exactly why short token lifetimes and, where supported, token binding are the durable controls, rather than after-the-fact consent removal.

## How this resolves the escalation

The escalation packet offered Desjardins two paths: if Croesus confirms a true OBO is intended, require an exposed-API scope and a confidential-client credential on the backend; if a server-side replay is intended, enforce a token-binding Conditional Access control. This demo realizes the first path concretely. It stands up the exposed-API scope and the confidential-client credential, then proves the resulting flow is a real OBO using evidence that does not depend on any single platform signal.

For the operational walk-through that produces this evidence, see [obo-demo-guide.md](obo-demo-guide.md). For the configuration values every component reads, see [configuration-contract.md](configuration-contract.md).
