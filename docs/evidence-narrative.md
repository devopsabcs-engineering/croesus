---
title: Croesus OBO Evidence Narrative
description: Maps the Desjardins escalation-packet questions to the concrete evidence the mock OBO demo produces and explains why audience-binding is the headline proof
author: Croesus Demo Team
ms.date: 2026-09-22
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

Token Protection token binding, and therefore the 1008 "unbound" signal, does not cover Microsoft Graph. Its generally available scope is native-application clients reaching Exchange Online, SharePoint Online, and Teams. A preview extends evaluation to browser-based web applications on Windows and macOS, scoped to Azure Resource Manager. Neither surface reaches a browser SPA that calls a custom API that then calls Microsoft Graph, so asserting 1008 as the OBO proof for this application shape would claim a signal the platform does not emit here.

> [!IMPORTANT]
> The browser-based web application coverage is in preview. Treat it as a direction of travel, not as an available control, and do not design a Conditional Access requirement around it.

A sign-in status code reports the outcome of a policy evaluation. It does not name the grant the client used, so no status code on its own separates an On-Behalf-Of exchange from a forwarded token. The audience pair does that work, and nothing in the log schema substitutes for it.

Tier 2b reproduces the customer's exact telemetry on the terms the platform supports. It stands up a report-only Conditional Access Token Protection policy (a Microsoft Entra ID P1 capability) scoped to a native mobile-and-desktop client reaching Exchange Online, then reads the resulting `signInSessionStatusCode == "1008"` from the non-interactive sign-in logs (Query 4 in [../scripts/evidence-kql.kusto](../scripts/evidence-kql.kusto)). Report-only mode records the binding evaluation without blocking anyone, so the exhibit is safe to run and trivial to reverse. This is the real 1008, obtained on a supported resource, and it stays clearly separate from the OBO proof.

The audience boundary carries the argument on its own. Two distinct audiences and enforced rejection in both directions demonstrate a standards-compliant OBO regardless of whether any Token Protection signal is present. Tier 2a adds the mirror image: a gated server-side replay that forwards a real Graph token from the middle tier and emits a distinct `ReplayAttempt` event, reproducing the replay shape the customer's logs attribute to the vendor backend so the wrong and right flows sit side by side.

## A measured base rate for Unbound

The scope argument above establishes that 1008 cannot be the OBO proof. A measurement establishes something a reader can verify independently: how often the unbound status appears in traffic that has nothing to do with this escalation.

On 2026-09-23 we sampled the 200 most recent sign-in rows in the demonstration tenant and grouped them by binding status, status code, and incoming token type. The tenant hosts no back-end-for-frontend, and none of the sampled rows belong to the proof-of-concept application. Query 5 in [../scripts/evidence-kql.kusto](../scripts/evidence-kql.kusto) reproduces the grouping against a Log Analytics workspace.

| Binding status and code | Incoming token type   | Share of sample |
|-------------------------|-----------------------|-----------------|
| `bound` / `0`           | Primary refresh token | 74.5%           |
| `unbound` / `1002`      | None                  | 11.5%           |
| `none` / `1002`         | None                  | 9.0%            |
| `none` / `0`            | None                  | 2.0%            |
| `none` / `1006`         | None                  | 1.0%            |
| `none` / `1006`         | Primary refresh token | 1.0%            |
| `bound` / `0`           | None                  | 0.5%            |
| `unbound` / `1008`      | None                  | 0.5%            |

Two readings follow, and both support the position this document already takes.

An unbound status occupied roughly twelve percent of ordinary traffic, code 1008 among it, on applications unrelated to the escalation and with no BFF anywhere in the tenant. Unbound is a routine binding diagnostic in a working tenant, which is what the scope argument predicts, and observing it does not single out a vendor backend.

Every bound row carried a primary refresh token, and no unbound row carried one. Binding status tracks whether the client presented a primary refresh token, which is a property of how the client obtained its session on the device and of the broker that issued it. Server-side token custody sits elsewhere in the flow entirely. A back-end-for-frontend therefore cannot convert an unbound sign-in into a bound one, and no one should promise the customer that adopting one will make the status change.

> [!IMPORTANT]
> Reading this field through Microsoft Graph requires the beta sign-in log endpoint. `https://graph.microsoft.com/v1.0/auditLogs/signIns` returns `tokenProtectionStatusDetails` as null on the same rows where beta returns a populated status. A v1.0 reader collects nothing and produces output that looks like a clean binding result, which inverts the finding. Treat a null as evidence not collected.

A base rate is context, not a verdict. Nothing here establishes that any particular unbound row in the customer's logs is benign, and nothing here closes the original observation. The finding stays open until grant and policy evidence explains it. What the measurement removes is the assumption that the status is unusual enough to be suspicious on its own.

One further observation belongs with this one. The proof-of-concept application registration has never recorded a delegated user sign-in, so the pipeline's interactive, delegated-acquisition, Conditional Access, and sign-in-log-correlation criteria correctly report `not-executed`. Those verdicts describe an absent interactive session rather than a control that was tested and passed.

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

One guard belongs alongside this verdict: `1008` is out of Token Protection scope for any flow reaching Microsoft Graph and is not replay evidence. Microsoft Graph falls under neither the generally available native-application scope nor the preview browser-based web application scope, so an `Unbound (1008)` line against a sign-in that later reaches Graph is expected and benign. It is also an evaluation outcome rather than a grant classification, so it cannot identify which OAuth flow produced the sign-in. Keep that statement in view so the analysis does not drift back to treating `1008` as proof of token replay.

## What the reference BFF changes, and what it does not

A reference back-end-for-frontend lives in [../poc/bff-yarp-net10](../poc/bff-yarp-net10). Its one security claim is token custody. `SaveTokens` is false and the cookie authentication handler is given a distributed `ITicketStore`, so the browser holds an opaque session reference while the tokens stay server-side. The application carries 56 passing tests, 6 of them on custody specifically, including one that decrypts the issued cookie and asserts it carries only a session reference rather than asserting a configuration flag.

Reducing token exposure is not the same as changing the protocol. The BFF narrows where a token can be observed; it does not alter the audience, the issuer, or the type of the token the downstream leg receives. A correct OBO exchange behind a BFF is still an OBO exchange, and a forwarded token behind a BFF is still a forwarded token. The audience-boundary evidence above decides the case either way.

Two boundaries limit what this reference application demonstrates. It forwards a single `/api` route to one configured downstream, and its destination allowlist is empty until deployment supplies it, so the application ships pointing at nothing in particular. No bridge to the classic .NET Framework host was built, and nothing in this repository should be read as claiming one exists. The site is also not hosted by default, because `deployBffSite` in [../infra/poc/main.bicep](../infra/poc/main.bicep) is false. Its hostname, `croesus-bff-a3v24wppuvd34-bff.azurewebsites.net`, is a reserved name the registration can claim rather than a running endpoint.

These properties are independent of network posture. Server-side token custody, audience validation, and the delegated-scope policy behave identically whether a site is reachable publicly or through a private endpoint. The comparison PoC runs on public ingress by explicit decision, recorded in [classic-net-bff-poc.md](classic-net-bff-poc.md), and that choice neither strengthens nor weakens any conclusion in this document.

## Reversibility and residual token validity

Every Tier 2 change is reversible, and the teardown scripts restore the tenant to its prior state (see the demo guide, step 8). One honest caveat matters for the security conversation: revoking the SPA-to-Graph delegated grant stops the SPA from acquiring new Graph tokens, but it does not invalidate Graph access tokens that were already issued. Those remain valid until their natural expiry, which is short (on the order of an hour). Revocation cuts off future issuance and refresh, not the lifetime of a token already in hand. That property is exactly why short token lifetimes and, where supported, token binding are the durable controls, rather than after-the-fact consent removal.

## How this resolves the escalation

The escalation packet offered Desjardins two paths: if Croesus confirms a true OBO is intended, require an exposed-API scope and a confidential-client credential on the backend; if a server-side replay is intended, enforce a token-binding Conditional Access control. This demo realizes the first path concretely. It stands up the exposed-API scope and the confidential-client credential, then proves the resulting flow is a real OBO using evidence that does not depend on any single platform signal.

For the operational walk-through that produces this evidence, see [obo-demo-guide.md](obo-demo-guide.md). For the configuration values every component reads, see [configuration-contract.md](configuration-contract.md).
