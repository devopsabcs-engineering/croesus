---
title: Croesus OBO Evidence Narrative
description: Maps the Desjardins escalation-packet questions to the concrete evidence the mock OBO demo produces and explains why audience-binding is the headline proof
author: Croesus Demo Team
ms.date: 2026-06-29
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

A real OBO produces two different tokens. The middle tier receives a token whose audience is the API, then exchanges it for a brand-new token whose audience is Microsoft Graph. The second token carries a fresh `jti` and `iat`. A replay produces one token used twice, with one audience and one `jti`.

The demo makes that boundary observable in three reinforcing ways:

* The Application Insights claim logs show leg 1 with `aud == API` and leg 2 with `aud == Microsoft Graph` and a different `jti`.
* The negative tests show that neither token works against the other resource: a Graph-audience token is rejected by the API with `401`, and the API-audience token is rejected by Graph with `401`.
* The Entra non-interactive sign-in logs show two correlated legs sharing a `CorrelationId` but naming two different resources.

This is the strongest possible evidence because it is intrinsic to the protocol. Two distinct audiences with two distinct token identifiers cannot be produced by reusing a single token. The negative tests close the loop by proving the boundary is enforced rather than incidental.

## Question-by-question mapping

| Question | What the demo shows | Where to see it |
| --- | --- | --- |
| Q1: Is the second request an OBO exchange or a server-side replay? | Two distinct tokens with distinct audiences and `jti` values, issued by the OBO exchange. The gated Tier 2a control shows the contrast directly: one token forwarded from server-side context, with no fresh issuance. | App Insights leg 1 and leg 2 claim logs, plus the distinct `ReplayAttempt` event; [obo-demo-guide.md](obo-demo-guide.md) steps 5 and 7. |
| Q2: Is the backend a confidential client, and which app and scope? | The API authenticates as a confidential client using a certificate in Key Vault, exposes `access_as_user`, and is pre-authorized for the SPA. | Provisioning output and [configuration-contract.md](configuration-contract.md); the OBO request shape in the App Insights log records the certificate thumbprint. |
| Q3: What audience does the second token carry? | Leg 2 targets Microsoft Graph through a freshly issued token, distinct from the API-audience token of leg 1. | App Insights leg 2 claim log (`aud == Microsoft Graph`); KQL row with `ResourceDisplayName == "Microsoft Graph"`. |
| Q4: What are the backend egress IP ranges? | Out of scope for the demo. The demo proves the flow shape; egress ranges remain a vendor-supplied fact for the Conditional Access exception. | Vendor input; tracked in the escalation packet, not reproduced here. |
| Q5: Does the backend rely on tenant-specific device-compliance claims? | The corrected flow depends on the OBO exchange, not on relaying device-compliance claims. The single-tenant design keeps every leg intra-tenant. | Architecture in [obo-demo-guide.md](obo-demo-guide.md); tenancy section of the same guide. |
| Q6: Is the token handling compatible with token binding or Token Protection? | The OBO flow issues a new token per leg, so a token-binding control denies replays without breaking a correct OBO. Tier 2b reproduces the real 1008 "unbound" signal on a supported resource (Exchange Online) with a report-only policy, kept as a labeled exhibit rather than the proof. | Negative tests; the report-only policy and Query 4; the Token Protection section below. |

## Token Protection 1008: a labeled Tier 2b exhibit, not the OBO proof

The original analysis observed the blocked event as a Token Protection "unbound" signal, event code 1008. It is tempting to make code 1008 the centerpiece, but presenting it as the OBO proof would be technically incorrect for this application shape and would undermine credibility with a security-literate customer.

Token Protection token binding, and therefore the 1008 "unbound" signal, applies to native-application clients reaching specific resources: Exchange Online, SharePoint Online, and Teams. It does not fire for a browser-based SPA calling a custom API that then calls Microsoft Graph. Asserting 1008 as the OBO proof for this app shape would claim a signal the platform does not emit here.

Tier 2b reproduces the customer's exact telemetry on the terms the platform supports. It stands up a report-only Conditional Access Token Protection policy (a Microsoft Entra ID P1 capability) scoped to a native mobile-and-desktop client reaching Exchange Online, then reads the resulting `signInSessionStatusCode == "1008"` from the non-interactive sign-in logs (Query 4 in [../scripts/evidence-kql.kusto](../scripts/evidence-kql.kusto)). Report-only mode records the binding evaluation without blocking anyone, so the exhibit is safe to run and trivial to reverse. This is the real 1008, obtained on a supported resource, and it stays clearly separate from the OBO proof.

The audience boundary carries the argument on its own. Two distinct audiences, two distinct `jti` values, and enforced rejection in both directions demonstrate a standards-compliant OBO regardless of whether any Token Protection signal is present. Tier 2a adds the mirror image: a gated server-side replay that forwards a real Graph token from the middle tier and emits a distinct `ReplayAttempt` event, reproducing the replay shape the customer's logs attribute to the vendor backend so the wrong and right flows sit side by side.

## Reversibility and residual token validity

Every Tier 2 change is reversible, and the teardown scripts restore the tenant to its prior state (see the demo guide, step 8). One honest caveat matters for the security conversation: revoking the SPA-to-Graph delegated grant stops the SPA from acquiring new Graph tokens, but it does not invalidate Graph access tokens that were already issued. Those remain valid until their natural expiry, which is short (on the order of an hour). Revocation cuts off future issuance and refresh, not the lifetime of a token already in hand. That property is exactly why short token lifetimes and, where supported, token binding are the durable controls, rather than after-the-fact consent removal.

## How this resolves the escalation

The escalation packet offered Desjardins two paths: if Croesus confirms a true OBO is intended, require an exposed-API scope and a confidential-client credential on the backend; if a server-side replay is intended, enforce a token-binding Conditional Access control. This demo realizes the first path concretely. It stands up the exposed-API scope and the confidential-client credential, then proves the resulting flow is a real OBO using evidence that does not depend on any single platform signal.

For the operational walk-through that produces this evidence, see [obo-demo-guide.md](obo-demo-guide.md). For the configuration values every component reads, see [configuration-contract.md](configuration-contract.md).
