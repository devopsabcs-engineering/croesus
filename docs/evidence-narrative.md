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
| Q1: Is the second request an OBO exchange or a server-side replay? | Two distinct tokens with distinct audiences and `jti` values, issued by the OBO exchange. A replay would show one token and one `jti`. | App Insights leg 1 and leg 2 claim logs; [obo-demo-guide.md](obo-demo-guide.md) step 5. |
| Q2: Is the backend a confidential client, and which app and scope? | The API authenticates as a confidential client using a certificate in Key Vault, exposes `access_as_user`, and is pre-authorized for the SPA. | Provisioning output and [configuration-contract.md](configuration-contract.md); the OBO request shape in the App Insights log records the certificate thumbprint. |
| Q3: What audience does the second token carry? | Leg 2 targets Microsoft Graph through a freshly issued token, distinct from the API-audience token of leg 1. | App Insights leg 2 claim log (`aud == Microsoft Graph`); KQL row with `ResourceDisplayName == "Microsoft Graph"`. |
| Q4: What are the backend egress IP ranges? | Out of scope for the demo. The demo proves the flow shape; egress ranges remain a vendor-supplied fact for the Conditional Access exception. | Vendor input; tracked in the escalation packet, not reproduced here. |
| Q5: Does the backend rely on tenant-specific device-compliance claims? | The corrected flow depends on the OBO exchange, not on relaying device-compliance claims. The single-tenant design keeps every leg intra-tenant. | Architecture in [obo-demo-guide.md](obo-demo-guide.md); tenancy section of the same guide. |
| Q6: Is the token handling compatible with token binding or Token Protection? | The OBO flow issues a new token per leg, so a token-binding control denies replays without breaking a correct OBO. Token Protection code 1008 is an optional advanced exhibit, not the proof. | Negative tests; the Token Protection note below. |

## Why Token Protection 1008 is only an optional exhibit

The original analysis observed the blocked event as a Token Protection "unbound" signal, event code 1008. It is tempting to make code 1008 the centerpiece, but doing so would be technically incorrect for this application shape and would undermine credibility with a security-literate customer.

Token Protection token binding, and therefore the 1008 "unbound" signal, applies to native-application clients reaching specific resources (Exchange Online, SharePoint Online, and Teams services). It does not fire for a browser-based SPA calling a custom API that then calls Microsoft Graph. Presenting 1008 as the OBO proof for this app shape would assert a signal that the platform does not emit here.

The demo therefore demotes 1008 to a clearly-labeled advanced exhibit. If a real bound-versus-unbound signal is desired, it requires a separate native-application exhibit against one of the supported resources, scoped explicitly as advanced and kept distinct from the OBO proof.

The audience boundary carries the argument on its own. Two distinct audiences, two distinct `jti` values, and enforced rejection in both directions demonstrate a standards-compliant OBO regardless of whether any Token Protection signal is present.

## How this resolves the escalation

The escalation packet offered Desjardins two paths: if Croesus confirms a true OBO is intended, require an exposed-API scope and a confidential-client credential on the backend; if a server-side replay is intended, enforce a token-binding Conditional Access control. This demo realizes the first path concretely. It stands up the exposed-API scope and the confidential-client credential, then proves the resulting flow is a real OBO using evidence that does not depend on any single platform signal.

For the operational walk-through that produces this evidence, see [obo-demo-guide.md](obo-demo-guide.md). For the configuration values every component reads, see [configuration-contract.md](configuration-contract.md).
