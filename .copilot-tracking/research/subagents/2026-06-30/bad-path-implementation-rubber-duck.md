---
title: Bad-Path Implementation Rubber Duck
description: Red-team review of the Croesus token replay bad-path implementation plan
author: GitHub Copilot
ms.date: 2026-06-30
ms.topic: research
keywords:
  - croesus
  - token replay
  - obo
  - graph
  - conditional access
estimated_reading_time: 9
---

## Scope

Status: Complete

This is research only. No production code changes were made. The goal is to
red-team the current implementation plan for adding a bad-path demo that
reproduces the Croesus unbound token replay shape and explains the observed
Token Protection "1008" issue without overstating what this mock can emit.

## Bottom Line

Use a two-tier plan:

1. Minimum viable bad-path contrast: keep the current good path intact and add a
   client-side negative-control UX around the already-proven mismatched-audience
   failures. This requires no new app registration permission, no new API route,
   and no dangerous replay endpoint.
2. Faithful server replay lab: implement Variant C only behind explicit local or
   non-production gates. Variant C is feasible, but not with the existing route,
   CORS method set, registration permissions, or configuration contract. It must
   be treated as an intentionally vulnerable lab, not a default feature.

The previous plan's selection of Variant C is technically workable, but it is
not the minimum viable implementation. It also needs stronger safety controls:
the replay endpoint should not be anonymous, should accept only a fixed Graph
`/me` replay, should never log raw tokens, and should be disabled by default in
source, infrastructure, and CI.

## Minimum Viable Implementation

The safest minimum viable implementation is a "bad path" button and panel that
executes and explains the two negative controls already central to the demo:

* Token A, audienced to the API, replayed directly to Microsoft Graph, returns
  `401` because its audience is not Graph.
* A Graph-audienced token presented to the API returns `401` because the API only
  accepts tokens audienced to itself.

This minimum implementation needs only SPA changes:

* Add a `callGraphWithApiTokenWrongWay()` helper in `spa/src/api.ts` that reuses
  the existing `getApiToken()` flow, then calls fixed
  `https://graph.microsoft.com/v1.0/me` with that API token. It returns status,
  error body, and decoded non-sensitive token claims.
* Add a bad-path result state and button in `spa/src/App.tsx`, near the existing
  static `ContrastPanel()` and `handleCallApi()` handler.
* Add a `ReplayPanel` or extend the contrast panel to show: attempted target,
  token `aud`, expected audience, HTTP result, and interpretation.

This does not faithfully reproduce the real Croesus backend replay succeeding,
but it is the best first increment because it proves the boundary the corrected
architecture relies on. It also maps to the existing tests and documentation:
`api/Tests/NegativeControlTests.cs:22` and
`api/Tests/NegativeControlTests.cs:25` already assert the two audience-mismatch
controls, while `docs/obo-demo-guide.md:130` and
`docs/obo-demo-guide.md:131` describe the same two negative outcomes.

## Variant C Feasibility

Variant C is feasible only as an explicit lab. It cannot be implemented by
posting a Graph token to the existing `GET /api/me` route.

Current blockers:

* The SPA requests only `VITE_API_SCOPE`; no Graph request object exists.
  Evidence: `spa/src/authConfig.ts:11`, `spa/src/authConfig.ts:23`,
  `spa/src/authConfig.ts:25`, `spa/src/authConfig.ts:31`.
* `getApiToken()` only acquires `apiRequest`, with silent acquisition and popup
  fallback. A Graph variant needs a sibling request/helper with the same MSAL UX
  behavior. Evidence: `spa/src/getApiToken.ts:21`,
  `spa/src/getApiToken.ts:24`, `spa/src/getApiToken.ts:25`.
* The only API fetch is `GET /api/me` with a Bearer API token. Evidence:
  `spa/src/api.ts:63`, `spa/src/api.ts:68`, `spa/src/api.ts:70`,
  `spa/src/api.ts:71`.
* The existing API controller is protected by `[Authorize]` and
  `[RequiredScope("access_as_user")]`, then defensively rejects non-API
  audiences. Evidence: `api/Controllers/MeController.cs:22`,
  `api/Controllers/MeController.cs:25`, `api/Controllers/MeController.cs:55`,
  `api/Controllers/MeController.cs:61`.
* The current CORS policy allows only `GET`, so a JSON `POST /api/replay` from
  the SPA will fail preflight until the API allows `POST`. Evidence:
  `api/Program.cs:36`, `api/Program.cs:41`, `api/Program.cs:51`.
* The deployment workflow bakes Vite variables at build time and currently only
  injects SPA client ID, tenant ID, API scope, and API base URL. Evidence:
  `.github/workflows/deploy-croesus.yml:40`,
  `.github/workflows/deploy-croesus.yml:43`,
  `.github/workflows/deploy-croesus.yml:44`,
  `.github/workflows/deploy-croesus.yml:45`,
  `.github/workflows/deploy-croesus.yml:46`.
* The configuration contract explicitly says no Graph scope belongs in SPA
  configuration. A Variant C lab must update that contract as an exception, not
  silently violate it. Evidence: `docs/configuration-contract.md:65`,
  `docs/configuration-contract.md:67`, `docs/configuration-contract.md:69`.

Exact Variant C changes required:

* Add `VITE_GRAPH_SCOPE`, defaulting to `https://graph.microsoft.com/User.Read`,
  and optionally `VITE_GRAPH_BASE_URL`, defaulting to
  `https://graph.microsoft.com/v1.0`. Update `spa/.env.example`,
  `docs/configuration-contract.md`, and the SPA build env block in
  `.github/workflows/deploy-croesus.yml`.
* Update the provisioning script if the lab should be self-provisioning. Today it
  grants Graph `User.Read` and admin consent to the API, not the SPA. Evidence:
  `scripts/provision-app-registrations.sh:232`,
  `scripts/provision-app-registrations.sh:236`,
  `scripts/provision-app-registrations.sh:237`,
  `scripts/provision-app-registrations.sh:242`,
  `scripts/provision-app-registrations.sh:247`.
* Add `graphRequest` and a `getGraphToken()` helper mirroring `getApiToken()`.
* Add `POST /api/replay` or `POST /api/demo/replay-graph-token` in a new
  controller. Do not use `MeController.Get()`, because it is intentionally
  audience-bound to the API path.
* Make the replay endpoint require the normal API token on the request
  `Authorization` header, plus the Graph token in the JSON body. This preserves
  a caller-auth gate while still demonstrating that the backend can replay the
  user-supplied Graph bearer to Graph.
* Restrict the replay target to `GET https://graph.microsoft.com/v1.0/me`. Do
  not accept an arbitrary URL, path, method, or header set.
* Extend CORS to permit `POST` only for the SPA origin. `AllowAnyHeader()` is
  already present, so the important method change is `POST`.
* Add a replay-specific telemetry event that records only derived claims and
  result metadata. Reuse the redaction guard pattern in `OboClaimLogger`, which
  redacts JWT-shaped strings before telemetry or logs. Evidence:
  `api/Telemetry/OboClaimLogger.cs:16`,
  `api/Telemetry/OboClaimLogger.cs:20`,
  `api/Telemetry/OboClaimLogger.cs:59`,
  `api/Telemetry/OboClaimLogger.cs:61`,
  `api/Telemetry/OboClaimLogger.cs:82`,
  `api/Telemetry/OboClaimLogger.cs:89`.

Recommended endpoint shape:

```text
POST /api/demo/replay-graph-token
Authorization: Bearer <token A, aud = API>
Content-Type: application/json

{ "graphAccessToken": "<token G, aud = Graph>" }
```

The response should include only safe evidence:

```json
{
  "replay": {
    "enabled": true,
    "target": "https://graph.microsoft.com/v1.0/me",
    "source": "api-server",
    "resultStatus": 200,
    "sameTokenReused": true,
    "tokenClaims": {
      "aud": "https://graph.microsoft.com",
      "scp": "User.Read",
      "jti": "...",
      "hasCnf": false
    },
    "note": "This reproduces server-side bearer-token replay shape. It does not guarantee a Token Protection 1008 log in this SPA/custom-API/Graph mock."
  }
}
```

## Hidden Pitfalls

* Admin consent and registration drift: the current provisioning grants Graph
  `User.Read` to the API for OBO and grants the SPA only the API scope. Variant C
  needs Graph delegated permission and consent on the SPA. Without admin consent,
  the first interactive Graph-token request may show a consent prompt or fail
  with a tenant policy error.
* MSAL consent UX: requesting Graph for the first time is a separate resource
  request. The existing popup fallback pattern should be reused so the UX can
  handle `InteractionRequiredAuthError`. Mixing this into `loginRequest` would
  weaken the good-path claim that sign-in asks only for the API scope.
* Graph scope spelling: keep one canonical delegated scope,
  `https://graph.microsoft.com/User.Read`. Avoid `.default` in the SPA because
  it hides exactly which delegated permission is being requested.
* API route auth: making `/api/replay` anonymous would create a public token
  replay service. Require the normal API token to call the demo endpoint and
  still reject requests without `Demo:EnableReplay`.
* CORS preflight: `POST` plus `Authorization` and `Content-Type: application/json`
  is preflighted. The current policy already allows any header, but only `GET`.
  A browser failure here will look like `Failed to fetch`, not an API error.
* Sensitive token logging: raw Graph tokens must not appear in browser console,
  API logs, App Insights, exception messages, or failed fetch error strings.
  Return decoded non-sensitive claims, token length, SHA-256 token hash prefix if
  needed, and replay result. Never return or persist the raw token.
* Browser storage and COOP: MSAL uses popup flows and `sessionStorage`.
  Evidence: `spa/src/authConfig.ts:18`, `spa/src/App.tsx:60`. Modern browser
  COOP or popup restrictions can surface warnings when popup windows close. This
  is mostly a UX issue, but it can obscure the consent flow during demos.
* App Service outbound IP vs AWS analogy: the faithful analogy is "server replay
  from a different cloud egress," not "AWS specifically." The demo should label
  the replay source as Azure App Service outbound/server-side. The real evidence
  observed AWS IPs and asks Croesus for full ranges. Evidence:
  `assets/app-registration-analysis-findings.md:120`,
  `assets/croesus-escalation-packet.md:27`.
* Literal 1008 boundary: the mock should not promise it will emit Token
  Protection code 1008. The repo explicitly says 1008 does not fire for a
  browser SPA calling a custom API that calls Graph. Evidence:
  `docs/evidence-narrative.md:45`, `docs/evidence-narrative.md:47`,
  `docs/evidence-narrative.md:49`.
* Build-time Vite variables: changing `VITE_GRAPH_SCOPE` requires rebuilding and
  redeploying the SPA. Setting an App Service app setting after deployment will
  not change already-built Vite JavaScript.
* CI and deploy effects: the current workflow deploys to the `production`
  environment and has a manual environment gate. Evidence:
  `.github/workflows/deploy-croesus.yml:74`,
  `.github/workflows/deploy-croesus.yml:107`. Do not let a vulnerable lab route
  ride the normal production deployment path by default.
* Existing evidence jobs: the evidence job is built to prove OBO and audience
  mismatch, not to run an intentional replay. Evidence:
  `.github/workflows/deploy-croesus.yml:128`,
  `.github/workflows/deploy-croesus.yml:142`,
  `.github/workflows/deploy-croesus.yml:143`.

## Safety Controls And Defaults

Recommended gates:

* Code gate: `Demo:EnableReplay` must default to `false` in `api/appsettings.json`.
  The endpoint should return `404` or `403` when disabled, before reading the
  body token.
* Environment gate: set `Demo__EnableReplay=false` in Bicep by default. Add a
  Bicep parameter only if the lab must deploy it, and name it clearly, for
  example `enableUnsafeReplayDemo` with default `false`.
* Build gate: add `VITE_ENABLE_REPLAY_DEMO=false` by default so the SPA does not
  render the Variant C button unless explicitly enabled at build time.
* CI gate: do not enable the replay demo in the normal `production` environment.
  If it must be deployed, use a separate GitHub Actions environment such as
  `replay-lab` and require manual approval.
* Auth gate: require a valid API token with `access_as_user` to call the replay
  endpoint. Do not make the route anonymous.
* Target gate: hardcode Graph `/me`; do not build a general proxy.
* Token gate: reject tokens larger than a reasonable size, reject non-JWT-shaped
  values before making an outbound call, and never echo the token back.
* Logging gate: add a test that sends a JWT-shaped string through the replay
  telemetry path and asserts logs/telemetry contain `[REDACTED:token]`, not the
  token.
* Test gate: add unit/integration tests proving the endpoint is unavailable when
  disabled and available only with both an API-authenticated caller and a
  syntactically valid Graph token body.

## UX And Evidence Model

Recommended UX flow:

1. Sign in normally. Keep `loginRequest` scoped to the API only.
2. Run "Good path: Call API via OBO." Show the existing `EvidencePanel` with
   two distinct audiences and OBO correlation/expiry evidence.
3. Run "Bad path A: replay API token to Graph." Show Graph rejects the API token
   with `401`, because audience binding works.
4. If the lab gate is enabled, show "Bad path C: acquire Graph token and let the
   server replay it." Trigger a separate Graph-scope MSAL popup if needed, then
   post that Graph token to the gated API replay endpoint.
5. Show the Variant C evidence as a deliberately unsafe replay: same Graph token
   `jti`, Graph audience on both browser-held token and server-replayed token,
   no OBO request shape, no confidential-client credential used for the Graph
   call, server-side replay result, and a clear note that a real 1008 sign-in-log
   signal is tenant/resource/policy dependent.

Evidence should be honest about three separate facts:

* Protocol fact: replay means the same token is reused. OBO means the API receives
  token A and obtains a distinct token B.
* Mock fact: Variant C reproduces the unsafe bearer-token replay shape and can
  show a server-side Graph call using a token minted for the SPA.
* Platform fact: Token Protection 1008 is not guaranteed in this mock's
  SPA/custom-API/Graph shape. The real customer evidence observed 1008 in raw
  logs. Evidence: `assets/app-registration-analysis-findings.md:17`,
  `assets/app-registration-analysis-findings.md:94`,
  `assets/app-registration-analysis-findings.md:128`,
  `assets/app-registration-analysis-findings.md:132`,
  `assets/app-registration-analysis-findings.md:133`.

Avoid these UX claims:

* "This reproduces 1008" without qualification.
* "AWS is the problem." The issue is replay from a server egress location with no
  true device context, not the cloud provider by itself.
* "The API is broken." The current API is correctly audience-bound. The replay
  endpoint is an intentionally vulnerable demo branch.

## Recommended Implementation Order

1. Add the client-only negative-control UX first. It is the smallest useful
   demonstration and has no new security exposure.
2. Add replay lab documentation and configuration contract changes before adding
   the endpoint.
3. Add the disabled-by-default endpoint with auth, fixed target, redaction, and
   tests.
4. Add the SPA Graph-token acquisition and Variant C button behind
   `VITE_ENABLE_REPLAY_DEMO`.
5. Add provisioning support for SPA Graph `User.Read` only if the lab is meant to
   be self-contained. Keep it explicitly labeled as unsafe replay lab setup.
6. Keep CI evidence for the good path as the default. Add replay evidence only in
   a separate manually approved lab path.

## Unresolved Questions

* Should Variant C ever be deployed to the current `production` GitHub Actions
  environment, or should it be restricted to a separate lab environment only?
* Does the tenant admin want to grant Graph `User.Read` delegated permission to
  the SPA app registration for this lab, knowing that it intentionally weakens
  the original demo's "SPA never asks Graph" posture?
* Should the replay endpoint return `404` when disabled to reduce discoverability,
  or `403` to make operator troubleshooting clearer?
* Is a separate native-client Token Protection exhibit in scope if the user wants
  a literal 1008 demonstration rather than an honest replay-shape demonstration?