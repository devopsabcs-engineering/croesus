---
title: 1008 Reasoning Rubber Duck
description: Reviewer critique of the Croesus unbound token replay reproduction research
author: GitHub Copilot
ms.date: 2026-06-30
ms.topic: concept
keywords:
  - croesus
  - token replay
  - token protection
  - obo
  - entra
estimated_reading_time: 9
---

## Research questions

* Identify claims in the current 1008 / replay research that are too strong,
  ambiguous, internally inconsistent, or likely wrong.
* Reconcile the README statement that the second sign-in is token replay with
  the caveat that the mock cannot literally emit 1008 in the SPA to API to Graph
  shape.
* Explain whether a demo endpoint that replays a Graph token from App Service
  would likely appear in Entra sign-in logs, and whether it would carry Token
  Protection status 1008, bound, unbound, or no signal.
* Clarify what the bad-path demo can honestly prove and what it cannot prove.
* Suggest sharper wording for
  .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md.

## Executive finding

The research is directionally sound, but it needs one sharper boundary: the
demo can reproduce the replay anti-pattern shape, not the real tenant's exact
Token Protection 1008 signal. The strongest evidence for the mock remains
audience binding and token identity: the good path produces two tokens with
different audiences and `jti` values, while a replay reuses one Graph-audience
bearer token. The 1008 claim is valid when describing the observed customer
prod-prod logs, but it should be explicitly framed as an external evidence point
and not as something the proposed App Service bad path is expected to emit.

## Evidence anchors

* README.md:8 says the vendor's second sign-in is confirmed by raw sign-in logs
  as token replay with Token Protection unbound code 1008 to Microsoft Graph.
* README.md:15 repeats the V1 security finding: token replay from an AWS IP
  carries compliant-device claims and is only flagged, not blocked, in prod.
* README.md:42 states the production registrations cannot perform standards
  OBO because they have no credential and expose no API scope.
* README.md:71 says the broken baseline reuses one token directly against Graph,
  while the corrected demo issues two tokens with distinct audiences and `jti`
  values.
* README.md:103 says the good-path evidence is decoded claims in Application
  Insights plus Entra non-interactive logs showing a fresh Graph token.
* docs/evidence-narrative.md:18 says audience boundary, not Token Protection
  1008, is the proof that decides the case for the demo.
* docs/evidence-narrative.md:22 defines the protocol distinction: OBO has two
  tokens, a replay has one token used twice.
* docs/evidence-narrative.md:47 says Token Protection 1008 applies to native
  clients reaching Exchange Online, SharePoint Online, and Teams, and does not
  fire for a browser SPA calling a custom API that then calls Microsoft Graph.
* docs/evidence-narrative.md:49 says a real bound-versus-unbound signal requires
  a separate native-application exhibit against a supported resource.
* assets/app-registration-analysis-findings.md:17 says the raw sign-in logs
  confirm the second event as server-side token replay, Token Protection
  unbound code 1008, targeting Microsoft Graph, not a custom backend API.
* assets/app-registration-analysis-findings.md:29 says the app registrations are
  SPA platform only, Microsoft Graph User.Read delegated only, with no exposed
  API, app roles, client secrets, certificates, or federated credentials.
* assets/app-registration-analysis-findings.md:94 says the replayed token
  inherits the original `deviceId` and compliant-device claims but is presented
  from an AWS IP with no real device context.
* assets/app-registration-analysis-findings.md:116 says a genuine Entra OBO
  requires a confidential client and an exposed API scope, which the three
  exports lack.
* assets/app-registration-analysis-findings.md:127-133 shows the prod-prod
  evidence table: the second event is non-interactive, from 3.97.32.113,
  Token Protection unbound code 1008, Microsoft Graph resource, ResultType 0.
* assets/croesus-escalation-packet.md:14 repeats the vendor-facing claim that
  the raw Entra sign-in logs show server-side token replay, Token Protection
  unbound code 1008, targeting Microsoft Graph.
* assets/croesus-escalation-packet.md:24-29 keeps the vendor questions open:
  exact flow, confidential-client configuration, token audience, AWS ranges, and
  token-binding compatibility.
* .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md:135-144
  already contains the critical caveat that the mock cannot literally emit 1008
  for the browser SPA to custom API to Graph shape.
* .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md:180-201
  selects Variant C: the SPA acquires a Graph token, posts it to the API, and
  the API replays it to Graph from App Service.
* .copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md:319-321
  overstates the proposed endpoint response by saying that, with Token
  Protection Conditional Access, this redemption is recorded as unbound code
  1008.
* .copilot-tracking/research/subagents/2026-06-30/1008-and-real-app-replay.md:23
  correctly identifies 1008 as a Token Protection sign-in-log signal, not an
  AADSTS, MSAL, HTTP, or app-specific code.
* .copilot-tracking/research/subagents/2026-06-30/1008-and-real-app-replay.md:35-41
  correctly records the scope limit, but line 41 adds an interpretation that the
  real Croesus flow targets Graph from a "native/server context". "Native" is
  not yet proven by the supplied evidence.
* .copilot-tracking/research/subagents/2026-06-30/1008-and-real-app-replay.md:76-86
  correctly distinguishes prod success with 1008 flagging from non-prod CA
  blocking, but line 86 calls 1008 the root security signal. That is too strong
  for the demo and should be scoped to the observed prod-prod evidence.

## Corrections and enhancements

### Correction 1: Separate real-log evidence from demo reproduction

The README and assets are allowed to say that the observed customer prod-prod
second event was token replay with Token Protection unbound code 1008, because
that is exactly what the cited sign-in table claims. The demo research must not
turn that into "our bad-path endpoint will reproduce 1008." The evidence
narrative explicitly warns that 1008 does not fire for this app shape.

Recommended framing:

```text
The real prod-prod logs report a second Microsoft Graph event as Token
Protection unbound code 1008. The mock cannot promise that platform signal. It
can reproduce the replay shape that the signal describes: a user Graph bearer
token is re-presented by a server without OBO.
```

### Correction 2: Do not call Variant C a literal 1008 reproduction

Variant C is the most faithful shape among the proposed demos because it moves
the same Graph token from browser to server and has App Service replay it to
Graph. It proves server-side bearer-token replay. It does not prove Token
Protection classification, and it does not prove that Graph or Entra will record
an unbound code 1008 event for that call.

The risky wording appears at
.copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md:319-321.
Replace "this redemption is recorded as status unbound (code 1008)" with
something conditional and scoped:

```text
Server re-presented the caller's Graph token with no OBO. This reproduces the
replay shape seen in the customer evidence. The literal Token Protection
unbound code 1008 is tenant/resource/client-shape dependent and is not expected
from this SPA to App Service to Graph mock.
```

### Correction 3: Be careful with "sign-in logs" for the App Service replay

A demo endpoint that accepts a Graph access token and calls Microsoft Graph from
App Service is not performing a new OAuth token request. It is presenting an
already-issued bearer token to the resource. That means the demo may produce a
Graph resource-access or non-interactive user-sign-in record depending on how
Graph/Entra logs that token use, but the research should not promise one.

If a log row appears, the safest expectation is:

* It would represent Microsoft Graph access using the SPA-acquired user token,
  likely with the SPA/client app identity and the App Service egress IP.
* It would not represent the API as a confidential client performing OBO.
* It would likely carry no Token Protection bound/unbound signal for this mock
  shape, because docs/evidence-narrative.md:47 says 1008 does not fire for the
  browser SPA to custom API to Graph shape, and Graph is not listed there as a
  supported Token Protection resource.
* Absence of 1008 should not be interpreted as "bound." It is more likely
  "not evaluated / no signal" for this scenario.

This is the single most important rubber-duck correction. The demo can log its
own replay result and App Service egress context, but external Entra logs should
be presented as opportunistic corroboration, not success criteria.

### Correction 4: Avoid implying the real client type is proven native

.copilot-tracking/research/subagents/2026-06-30/1008-and-real-app-replay.md:41
tries to reconcile the real 1008 observation by saying the real flow targets
Graph from a "native/server context." The "server" part is supported by the
AWS IP and non-interactive second event. The "native" part is not proven in the
available files.

Better wording:

```text
1008 is reported in the real prod-prod logs. The exact Token Protection path
that produced it still needs confirmation, including client type and supported
resource behavior. For the mock, do not rely on 1008; rely on audience and token
identity evidence.
```

### Correction 5: Qualify "the second sign-in is token replay"

The README statement is acceptable as a concise executive conclusion, but the
research should preserve the evidentiary nuance. The app registration shape
proves these three exported app registrations cannot perform OBO as configured.
The sign-in table says the second prod-prod event is unbound code 1008 against
Microsoft Graph. Together those make replay the best supported conclusion. They
do not prove vendor intent, exact implementation mechanics, full AWS egress
ranges, or the exact non-prod AADSTS failure code. Those remain open in
assets/croesus-escalation-packet.md:24-29 and
assets/app-registration-analysis-findings.md:200.

Recommended framing:

```text
The supplied manifests rule out standards OBO for the exported registrations,
and the raw prod-prod sign-in table labels the second Microsoft Graph event as
Token Protection unbound code 1008. We should describe the flow as observed
server-side token replay unless Croesus supplies evidence of a different middle
tier or app registration not present in the exports.
```

## Honest claim matrix

| Claim | Status | Safer wording |
| --- | --- | --- |
| The real prod-prod evidence includes Token Protection unbound code 1008. | Supported by supplied docs | The supplied raw sign-in table reports the second Microsoft Graph event as Token Protection unbound code 1008. |
| The exported registrations cannot perform standards OBO. | Supported | The three exported SPA registrations lack a credential and exposed API scope, so they cannot perform OBO as configured. |
| The mock good path proves real OBO. | Supported for the mock | The mock good path proves a standards OBO shape by showing token A with API audience and token B with Graph audience and a different `jti`. |
| Variant C proves server-side replay. | Supported if implemented | Variant C proves a server can replay a user Graph bearer token without OBO. |
| Variant C reproduces 1008. | Too strong | Variant C reproduces the replay shape associated with the 1008 finding, but not the literal Token Protection 1008 signal. |
| App Service replay will appear in Entra sign-in logs. | Ambiguous | It may appear as Graph/resource access or a non-interactive user event, but the research should not require that for success. |
| App Service replay will show 1008. | Likely wrong | The likely outcome is no Token Protection signal for this mock shape; absence of 1008 is not evidence of binding. |
| App Service replay will show bound. | Likely wrong | Bound/unbound should not be inferred unless Token Protection actually evaluates the scenario and logs a status. |

## Suggested primary-document edits

Apply these wording changes to
.copilot-tracking/research/2026-06-30/croesus-token-replay-bad-path-research.md
when the research document is next revised.

### Sharpen the title and success criteria

Current title says "Reproduce ... the '1008 issue'". That invites readers to
expect a literal sign-in-log code. Prefer:

```text
Reproduce the Croesus Unbound Token-Replay Anti-Pattern Behind the 1008 Finding
```

Current success criterion says "same observable issue". Prefer:

```text
The demo must reproduce the observable replay anti-pattern: a Graph-audience
user bearer token is presented by a server without OBO. Literal Token Protection
1008 emission is out of scope for this SPA to API to Graph mock and remains an
external customer-log exhibit.
```

### Refine the Variant C rationale

Replace "only one whose shape matches the real app" with:

```text
Variant C is the closest mock shape available in this repo: the server receives
a user Graph token it did not mint and re-presents that same token to Graph with
no OBO. It mirrors the suspected replay mechanics, while the literal 1008
classification remains dependent on Entra Token Protection support for the
client/resource shape.
```

### Replace endpoint response wording

Replace the note at lines 319-321 with:

```text
note = "Server re-presented the caller's Graph token with no OBO. This "
     + "reproduces the replay shape behind the customer 1008 finding; the "
     + "literal Token Protection 1008 signal is not expected from this mock."
```

### Make expected outcomes explicit

Replace "Annotate with the 1008 narrative" with:

```text
Annotate as replay-shape evidence only. Do not require or promise Token
Protection 1008 in Entra logs for this mock.
```

### Add a log-expectation caveat

Add a short section after expected outcomes:

```text
Entra-log caveat: POST /api/replay does not request a new token from Entra. It
only presents an existing Graph bearer token from App Service. If Graph/Entra
logs the resource access, the row may show the App Service egress IP, but it is
not an OBO issuance event and is not expected to carry Token Protection 1008 for
this SPA to API to Graph shape.
```

## What the bad-path demo proves

* It proves that a SPA can be configured to obtain a Graph-audience bearer token
  directly, which collapses the OBO boundary.
* It proves that the same Graph bearer token can be handed to a backend and
  re-presented to Graph from a server without using OBO.
* It proves the security difference between audience-bound OBO and replay: the
  good path never gives the SPA a Graph token and creates a new Graph token in
  the middle tier.
* It can show decoded token evidence, never raw token logs, that the replayed
  token has `aud = Microsoft Graph` and lacks a proof-of-possession binding
  claim if the token indeed has no `cnf` claim.
* It can show local app evidence that the backend replay succeeded or failed,
  based on Graph's HTTP response from the App Service call.

## What the bad-path demo does not prove

* It does not prove that Entra will emit Token Protection code 1008 for the
  mock. The repo's own evidence narrative says this app shape is outside the
  signal scope.
* It does not prove the exact Croesus implementation, only a faithful replay
  anti-pattern consistent with the supplied evidence.
* It does not prove vendor intent. assets/croesus-escalation-packet.md:38 still
  asks Croesus to confirm whether replay is by design or an implementation
  defect.
* It does not prove the non-prod Conditional Access AADSTS failure code. That
  remains an evidence gap in the earlier findings.
* It does not prove App Service replay will always show up in Entra sign-in
  logs. The endpoint presents an existing token to Graph rather than requesting
  a new token from Entra.
* It does not prove a token is "bound" simply because 1008 is absent. No signal
  and bound are different states.

## Unresolved questions

* What exact Token Protection support matrix applied to the real prod-prod 1008
  observation, including client type, resource, and policy configuration?
* Did the real 1008 event arise from Microsoft Graph resource access, from a
  token endpoint request, or from a resource-service sign-in record surfaced by
  Entra sign-in logs?
* Is there another Croesus backend/confidential app registration not included in
  the three supplied application exports?
* What is the verbatim non-prod AADSTS failure code and exact Conditional Access
  policy name?
* Will the sandbox tenant used for the mock log Graph access from App Service in
  the same table and schema as the customer prod-prod evidence?

## Recommended next research

* Confirm the current Microsoft Token Protection support matrix from first-party
  Entra documentation, specifically whether Microsoft Graph ever emits 0/1008
  for browser, native, confidential-client, or resource-access paths.
* Capture one controlled App Service Graph-token replay and inspect Entra logs
  for whether any row appears, which IP is shown, which app/resource are named,
  and whether Token Protection fields are populated.
* Compare the controlled replay row, if present, with the prod-prod table in
  assets/app-registration-analysis-findings.md:127-133.
* Ask Croesus for the authoritative post-login sequence diagram and whether a
  hidden backend app registration exists outside the three supplied exports.
