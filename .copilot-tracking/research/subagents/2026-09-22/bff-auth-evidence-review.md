---
title: BFF Authentication Evidence Review
description: Critical and high severity review of the Croesus BFF remediation evidence gates.
ms.date: 2026-09-22
---

## Scope and Status

Status: Complete as a documentary review. Nine high-severity findings; no critical finding established from the available evidence. Research only; no Azure operations or application changes.

Target: .copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md.

Only this review artifact was written. The target plan remains unchanged, and no deleted documents were restored. Each finding below supplies a proposed correction and acceptance gate for the plan owner. Security acceptance remains unverified until those changes are implemented and the relevant tests are run.

## Research Questions

* Can audience differences, token identifiers, or client authentication establish the OAuth grant?
* Which request identifiers and resource identifiers support defensible log joins?
* Where are optional access-token claims configured, and can a client inspect Graph tokens?
* What can BFF custody, Conditional Access report-only evaluation, and claim presence actually establish?
* Which acceptance gates avoid token disclosure, personal-data leakage, and false proof?

## Findings and Replacement Gates

### H1 High: Token differences and credentials do not establish OBO

Locations: target line 312 (resource-count proof), line 710 (two-audience assertion), and line 730 (credential type replaces grant evidence); also Scenario 2 and Scenario 5 assertions.

Different access-token audiences demonstrate different resource targets, not the grant that obtained them. Different token IDs or expiries do not establish OBO or eliminate replay risk. An ID token for the BFF and an access token for an API already have different audiences in an ordinary authorization-code flow. Normal cached bearer-token reuse at its intended resource is not evidence of malicious replay.

Replace the audience-count gate with an explicit flow classification. OBO requires an API receiving a user access token intended for that API and requesting a token for a further downstream API using `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, `requested_token_use=on_behalf_of`, and that inbound token as `assertion`. Client authentication is distinct from the user assertion. A client assertion can authenticate an authorization-code, refresh, or OBO request; it does not identify which grant occurred.

A BFF calling Graph directly, or a BFF acquiring an access token for API A and calling only API A, does not require OBO. API A calling API B on the user's behalf is the additional boundary that requires the delegated OBO exchange in this topology. An app-only call is a separate client-credentials scenario.

Acceptance: verify supported-library configuration and a sanitized acquisition trace from the actual execution path, not a hard-coded event label. Record the observed grant shape, assertion-presence flags, API A validation outcome, target scope alias, acquisition result, and token source. Distinguish a network OBO acquisition from a cache hit. Require positive and wrong-audience negative tests at the owned API. Audience enforcement proves audience enforcement, not grant classification. Missing grant evidence leaves the grant unclassified.

Sources: [Microsoft OBO protocol](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow), [token ownership](https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens), and [sign-in credential and protocol fields](https://learn.microsoft.com/en-us/graph/api/resources/signin?view=graph-rest-beta).

### H2 High: A universal CorrelationId join is not an evidence contract

Locations: target line 44 (universal join), line 614 (resource request IDs), line 702 (CA approach), and Scenario 5 evidence step.

Microsoft states that correlation IDs are based on client parameters and their accuracy is not guaranteed. Authorization, code redemption, OBO acquisition, refresh, Graph resource calls, and managed-identity credential acquisition are distinct operations. Do not require them to share a correlation ID or require four log categories for one delegated flow. Cache hits do not require new token issuance. A Graph API request ID is not automatically an Entra sign-in request ID.

Acceptance: assign an application-owned run ID and trace/span relationship; map each operation to its own actual MSAL/OIDC correlation ID, Entra response/request identifiers when available, and separate Graph client-request-id/request-id. Preserve the identifier's source and namespace. Join candidate Entra rows using tenant, expected client, request/correlation identifier, and bounded time; use OriginalRequestId only within its documented authentication sequence. Flag missing or multiple matches rather than guessing from user name or time. A correlation match corroborates a request, not the grant semantics.

Sources: [sign-in activity identifiers and aggregation caveats](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-sign-in-log-activity-details), [non-interactive grouping](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-noninteractive-sign-ins), and [table schema](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/aadnoninteractiveusersigninlogs).

### H3 High: Optional claims are placed on the wrong registration

Locations: target line 345 (BFF optionalClaims block), Scenario 2 evidence table, and line 712 (CAE assertion).

Access-token claims are configured on the resource API's registration, not the calling BFF's registration. Configuring BFF `optionalClaims.accessToken` cannot change tokens for Graph or a separate API. The resource also owns its access-token version. Microsoft explicitly says clients must treat Graph access tokens as opaque and must not depend on decoding their claims.

Acceptance: configure any required optional claims on the owned API, inspect them only after that API validates its inbound token, and return only a minimal evidence verdict. Configure BFF ID-token claims separately where justified. For Graph, use requested resource/scope metadata, acquisition result metadata, the fixed destination, response status, and request IDs. Label Graph as the requested/observed destination, never as a locally validated token audience.

Declare `cp1` in the acquiring client's library configuration only once challenge handling is implemented. The owned resource may request `xms_cc` as an optional claim, but its presence is capability signaling, not a successful challenge test or proof that CAE applied. Exercise the challenge/reacquisition path and prevent retry loops; do not decode Graph tokens to check `cp1`.

Sources: [optional claims resource ownership](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims), [opaque access tokens](https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens), and [claims challenges and client capabilities](https://learn.microsoft.com/en-us/entra/identity-platform/claims-challenge).

### H4 High: BFF adoption is presented as a device-context and CA fix

Locations: target line 711 (device context disappears), line 720 (compliant-device and location recommendation), and line 728 (customer-ready paragraph).

Moving token acquisition to a confidential client does not establish that the originating user's device context disappears. The backend host is not the user's device, but that distinction does not establish absence of user-session device evidence. The existing local analysis, `assets/app-registration-analysis-findings.md` lines 143-144, even reports matching device identity and compliant/joined state on both observed legs. That observation is not proof of a particular grant either.

Remove claims that device evidence necessarily proves relay, that the BFF structurally closes this finding, or that a backend delegated request necessarily fails a compliant-device grant. Also remove the proposed replacement of a device requirement with a location condition: these are different security controls, not equivalent remediations. A policy change needs the policy owner's separate decision and evidence of the actual failure.

Microsoft documents a specific logging caveat: confidential-client non-interactive refresh sign-ins can show the IP of original token issuance rather than the source of the refresh request. This does not mean vendor egress disappears from all telemetry, or that CA ignores network conditions. Distinguish observed browser/source IP, actual backend egress, and the IP reported on each sign-in row.

Acceptance: keep device and network outcomes unknown until the exact policy, evaluated resource, user/session context, applicable grant controls, and per-policy result are available. Test approved managed-device and unmanaged-device cases separately. Do not label a 403, device-binding status, or absent claim as evidence of replay. Token Protection's current browser preview applies to selected ARM web apps, not a general Graph BFF; remove predictions such as "always will be" and avoid treating unsupported protection as proof that the session is safe.

Sources: [device and other grant controls](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-grant), [refresh sign-in IP caveat](https://learn.microsoft.com/en-us/entra/identity/monitoring-health/concept-noninteractive-sign-ins), and [current Token Protection resource matrix](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-token-protection).

### H5 High: Report-only and client scoping can falsely pass the CA gate

Locations: target line 702 (BFF policy scope), line 734 (report-only sufficiency), line 762 (any report-only result passes), and CA KQL interpretation.

CA generally targets resources, not OAuth clients. Preserve Microsoft's exception: a confidential client requesting an ID token can also have policies for that client applied. A policy scoped to the BFF may therefore cover its OIDC sign-in, but does not automatically establish coverage of the owned downstream API or Graph operations. Microsoft Graph is an umbrella resource with targeting limitations; verify the effective audience and underlying service rather than assuming a selectable generic Graph target.

Report-only evaluates without enforcing. Its outcomes include Success, Failure, Not applied, and User action required. Success may rely on previously satisfied MFA; User action required does not cause report-only to perform MFA. Not applied is not protection. Device-compliance evaluation in report-only can still cause certificate-selection prompts on some platforms, so "zero risk/no user impact" is too strong.

Acceptance: identify the exact expected policy and evaluated resource for each test case; assert that policy's expected result, grant controls, and mode. Preserve condition-not-satisfied separately from grant-not-satisfied. Never pass merely because any `reportOnly*` value appears. Report-only supports a policy-evaluation verdict only. A completed MFA/challenge/enforcement claim requires a separately approved enforced test with recorded authentication outcomes. Headless CI and app-only tokens cannot substitute for a human delegated MFA test. No such policy change or live test is authorized by this research review.

Sources: [CA resource targeting and confidential-client exception](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-cloud-apps), [report-only behavior and outcomes](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-report-only), and [grant semantics](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-grant).

### H6 High: The audience query mixes identifier domains and unrelated traffic

Locations: target line 312 and query at line 403, especially `graphSp`, `ResourceIdentity`, and the client/resource OR filter.

`00000003-0000-0000-c000-000000000000` is Graph's application ID, not the tenant-specific Graph service-principal object ID. Microsoft documents service-principal `id` and `appId` as distinct. The Azure Monitor schema describes `ResourceIdentity` only as the resource ID; it does not establish a universal application-ID mapping. `ResourceServicePrincipalId` explicitly identifies the resource service principal. Do not replace the original assumption with an equally unverified assertion that every export's `ResourceIdentity` always has one particular meaning.

The OR filter also admits unrelated clients' Graph sign-ins if its resource comparison matches. Counting resources in that enlarged set cannot establish an exchange and can misattribute another request's CA result to this test.

Acceptance: use a verified tenant/application/service-principal mapping, then filter the expected client AND expected resource service principal AND the bounded operation identifiers/time. Account for the separate API A client identity when testing API A-to-API B OBO. Keep resource tenant, directory tenant, and Log Analytics workspace identifiers distinct; the table's `TenantId` denotes the workspace. Project only necessary fields and do not group the general evidence artifact by UPN. Treat multiple rows as an evidence inventory, never as an OBO proof. Queries still require validation against the actual exported schema; none was run in this review.

Sources: [non-interactive sign-in schema](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/aadnoninteractiveusersigninlogs) and [service principal identifiers](https://learn.microsoft.com/en-us/graph/api/resources/serviceprincipal?view=graph-rest-1.0).

### H7 High: Claim presence is substituted for security validation

Locations: target line 613 (`nonce` and `at_hash` presence), and Scenario 2 replay interpretation.

A nonce claim must match the original authentication request; presence alone says nothing about validation. Microsoft documents that `at_hash` is not included in ID tokens returned from the token endpoint, so requiring its presence falsely rejects the proposed code flow. Neither claim protects every subsequent use of a bearer access token against theft and replay.

Acceptance: retain supported middleware validation and test an initial OIDC callback with mismatched nonce, invalid state/correlation, invalid issuer/audience/signature, and expired ID token. Test PKCE mismatch and authorization-code reuse at the appropriate protocol boundary. A correct code-flow sign-in without `at_hash` must pass. Record only validation outcomes, not the nonce, state, code, verifier, or token. Keep refresh behavior separate; do not impose initial-login nonce requirements on every refresh response. Distinguish rejected authentication-response replay from bearer-token replay risk.

Sources: [ID-token claim semantics](https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference), [authorization-code and PKCE contract](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow), and [OIDC sample nonce/refresh distinction](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-oidc?view=aspnetcore-10.0).

### H8 High: Evidence collection lacks a sufficient secret and PII boundary

Locations: target Scenario 2 evidence page, claim projection, telemetry reuse, and Scenario 5 evidence artifacts. Local implementation anchor: `api/Telemetry/OboClaimLogger.cs`, `LogExchange` and `Redact`.

The existing logger accepts arbitrary claim dictionaries and emits their values. Its redactor recognizes only an entire three-segment JWT-shaped string. It does not exclude opaque tokens, five-segment encrypted tokens, bearer-prefixed or embedded tokens, cookies, secrets, and arbitrary personal data. "Not a raw JWT" is not equivalent to "safe to publish." Rendering claims server-side still sends the rendered content to the browser. The inspected code establishes a protection gap, not evidence that a real secret has already leaked.

Acceptance: use a typed allowlist constructed before serialization. The ordinary evidence record should contain random run/operation IDs, operation kind, target alias, validation booleans, acquisition source, bounded error codes, and outcome. Exclude raw access/refresh/ID tokens, assertions, authorization codes, PKCE verifiers, authentication cookies, credentials, full claims/challenges, Graph profile bodies, and user/device identifiers from page output, routine telemetry, CI logs, and exported artifacts. Keep any necessary diagnostic request IDs and tenant/app/policy mapping in restricted, retention-limited operational records. Use aliases and verdicts in customer-shareable artifacts.

Require authenticated and authorized access to the evidence endpoint, per-run ownership checks, and `Cache-Control: no-store`. Disable identity-library PII logging and broad request/response-body capture. Exercise synthetic sentinel values for JWT, JWE, opaque, prefixed, nested, and exception-message cases across every output sink. Do not use real tokens in leakage tests or preserve full network captures as build artifacts. Reject unexpected evidence fields rather than depending on regex redaction.

Sources: inspected logger; [Microsoft warning against production token/content logging](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-oidc?view=aspnetcore-10.0#inspect-the-access-token), [token formats](https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens), and [Graph data minimization and retention](https://learn.microsoft.com/en-us/graph/best-practices-concept). The Graph article's broad support-capture recommendation is not permission to put secrets or personal data in this demonstration's artifacts.

### H9 High: SaveTokens is mislabeled as strict server-side custody

Locations: target success criteria at line 43, OIDC configuration near line 276, Scenario 2 custody statement and literal evidence indicator, and optional SessionStore sizing mitigation at line 630.

`SaveTokens=true` stores tokens in authentication properties. In the proposed default cookie-ticket configuration, those properties are serialized into the protected browser cookie. The Microsoft sample explicitly describes the access token as stored in that cookie. Encryption and HttpOnly prevent ordinary browser-script access to its contents, but do not mean the tokens remain exclusively on the server. A literal "token store: server-side" label would therefore be false.

Acceptance: make a server-side ticket store or server-side token cache a required architecture choice, not a fallback for large cookies. If using SaveTokens, configure an appropriate `ITicketStore` through SessionStore so only a session identifier is sent to the client. Alternatively keep tokens exclusively in the server cache and out of the cookie ticket. Test the serialized ticket/store boundary using synthetic credentials, logout invalidation, expiry, and fail-closed behavior when a referenced session is missing. Do not infer custody by looking for readable JWT text in an encrypted cookie. The browser session cookie remains a sensitive bearer credential and still needs CSRF protection and hardened cookie settings.

Sources: [Microsoft OIDC sample and SaveTokens behavior](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-oidc?view=aspnetcore-10.0) and [SessionStore contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.cookieauthenticationoptions.sessionstore?view=aspnetcore-10.0).

## Replacement Acceptance Contract

These are recommended changes to the implementation plan, not tests executed during this review. Prioritize H8/H9 before building the evidence surface, H1/H4/H5 before presenting a customer conclusion, and the remaining findings before implementing the assertions.

| Gate | Required observation | Permitted conclusion |
| --- | --- | --- |
| Confidential BFF login | Actual authorization-code redemption, PKCE and client authentication, successful middleware validation | The demonstrated BFF uses confidential authorization code; no conclusion about historical customer requests |
| Server-side custody | Tokens absent from serialized browser ticket; actual server store/cache and invalidation tests | OAuth tokens are retained server-side in the tested configuration; cookie theft remains a risk |
| Owned API boundary | Valid owned-resource token accepted; wrong-audience token rejected | Audience validation works, not proof of OBO |
| Optional OBO extension | API A validates its inbound user token and performs the observed OBO request for API B | This execution used OBO; cache hits reported separately |
| Graph access | Fixed Graph destination, acquisition metadata, status and operation request IDs | Graph accepted or rejected the request; no locally decoded Graph-token claim assertions |
| Correlation | App trace maps distinct protocol and resource operations to identifiers in their own namespaces | Supported trace attribution with missing/ambiguous matches explicit |
| CA evaluation | Exact policy/resource/mode and expected per-policy result | Report-only evaluation only; Not applied is not protection |
| Enforced MFA/device behavior | Separately authorized live test and corresponding user/session authentication outcomes | Only the observed enforced case passed or failed |
| OIDC validation | Positive code flow and protocol-specific negative cases | Tested authentication checks reject invalid responses; not universal bearer replay prevention |
| Evidence privacy | Typed allowlist, authorization, no-store, synthetic leakage tests over all sinks | Tested output contract excludes prohibited values |

Use `Pass`, `Fail`, `Inconclusive`, `NotRun`, and `NotApplicable` explicitly. Missing permission, missing logs, unsupported fields, or skipped live prerequisites cannot pass a required gate. NotApplicable requires a documented topology decision, such as no API-to-API delegated hop. Synthetic CI results and live Entra evidence must be labeled separately. A passed build does not mean all security claims were verified.

For correlation, the implementable mapping is `run -> application operation/span -> protocol-specific request IDs -> verified candidate sign-in rows`, plus a separate mapping from each outbound Graph request to its own client-request-id and returned request-id. Microsoft recommends a unique client-request-id for each Graph request. Do not reuse a single identifier as if it were authoritative across unrelated services, and do not silently fabricate missing response headers.

## Replacement Customer Narrative

The proposed confidential-client BFF moves OAuth acquisition into the server and, with a required server-side store, reduces browser exposure to OAuth tokens. It does not make bearer credentials replay-proof, erase the originating user's device context, or guarantee Conditional Access success. The current evidence does not establish the customer's original OAuth grant or prove token replay. We will classify the grant from sanitized execution evidence, validate the owned API's audience boundary, and examine the exact policy/resource outcomes. OBO is a separate demonstration only if a protected API calls a further API on the user's behalf. Graph access tokens remain opaque, and report-only findings remain evaluation results rather than proof of enforced MFA.

## References and Uncertainty

Official references are linked beside each finding. Local anchors reviewed were the target document, `api/Telemetry/OboClaimLogger.cs`, `assets/croesus-escalation-packet.md`, `assets/app-registration-analysis-findings.md`, and `docs/evidence-narrative.md`. Existing narrative documents repeat some disputed assumptions; they are not independent platform evidence.

* The customer's original grant remains unresolved. Q7's sanitized request shape and Q8's complete application/service-principal inventory are still necessary; three SPA exports do not inventory all possible middle tiers or client authentication arrangements.
* ResourceIdentity's actual representation, optional-field availability, log ingestion delay, and request-ID coverage require tenant evidence. No live query was performed. Do not assert unconditional equality between token `uti` and every log's UniqueTokenIdentifier representation.
* The Microsoft Graph beta signIn schema now lists protocol values including `onBehalfOf`, `authorizationCodeWithPkce`, and `refreshTokenGrant`. These can corroborate the grant when actually present. They are not guaranteed in the Azure Monitor schema or every tenant export; beta is not a production API contract, and newer enum values may need `Prefer: include-unknown-enum-members`.
* The reviewed Token Protection matrix does not list a general Graph BFF scenario. This review does not independently resolve the customer's particular binding status code or determine that any failure is benign.
* A working BFF demonstration cannot retrospectively prove the customer's architecture or close the customer's CA incident. Actual policy scope, resource, permissions, and session evidence remain necessary.

## Follow-up Checklist and Questions

* [ ] Obtain sanitized request-field presence and grant labels for the original failing operation, plus the complete app/resource inventory. No token, cookie, code, assertion, or credential values.
* [ ] Validate the tenant-specific resource mapping and log-field availability using authorized read-only evidence.
* [ ] Choose the concrete server-side token/ticket store and confirm its lifecycle requirements before implementation.
* [ ] After separate approval, test the relevant resource policy, managed/unmanaged device cases, and claims-challenge/MFA path; retain only approved evidence.

Clarifying questions: Is the intended topology BFF-to-Graph, BFF-to-API A only, or API A-to-API B? Which exact resource and CA policy govern the failing call? Who can provide sanitized original-request evidence and approve any later enforced-policy test? These answers are not available from the reviewed files.
