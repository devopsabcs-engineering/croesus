---
title: BFF Architecture Security Review
description: Critical and high severity review of Croesus BFF remediation research.
ms.date: 2026-09-22
---

## Status and scope

Complete as a research review, not implementation approval. Writes were restricted to this research document. No application changes, cloud writes, delegated agents, or restoration of deleted documents. No live tenant, browser, network, or application tests were performed. Earlier deployment-state claims in the primary research were not independently verified.

Primary input: `.copilot-tracking/research/2026-09-22/croesus-bff-oidc-remediation-research.md`.

## Research questions

* Does the proposed token storage meet its server-side custody requirement?
* Are CSRF, proxy header isolation, destinations, refresh, challenges, concurrent cache updates, and logout specified adequately?
* Is runtime selection based on supported operating systems?
* Does the migration distinguish legacy-owned remote authentication from Core-owned authentication and Forms Authentication from OWIN cookies?
* Are legacy bypass prevention, interactive Conditional Access evidence, release gates, and root-route checks correct?

## Evidence basis

The primary research proposes `SaveTokens = true` and explicitly describes tokens serialized into an encrypted cookie, while claiming tokens never enter the browser. Official cookie and OIDC documentation confirms this contradiction; H1 describes the required correction.

`poc/modern-net10/Program.cs` configures `SaveTokens = false`, has no downstream acquisition or proxy, and explicitly calls `ValidateRequestAsync` only on POST `/signout`. Adding proxy endpoints requires their own verified CSRF enforcement.

## Findings and recommendations

Ranked design findings, not claims of exploited or deployed vulnerabilities. Severity is High unless explicitly stated. No confirmed Critical vulnerability has been demonstrated.

### H1: Token custody contradicts the stated requirement

Primary evidence: primary research lines 184-186, 228, 242, 555, and 630. The proposed `SaveTokens = true` persists access and refresh tokens in authentication properties and, with ordinary cookie storage, in the protected browser-held ticket. `HttpOnly` and encryption prevent ordinary JavaScript access; they do not make storage server-side. Treating `SessionStore` solely as a cookie-size fallback misses the custody requirement.

The [OIDC sample](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-oidc?view=aspnetcore-10.0&pivots=with-yarp-and-aspire) explicitly documents cookie token storage. The [SessionStore contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.cookieauthenticationoptions.sessionstore?view=aspnetcore-10.0) instead sends only a session identifier to the client.

Choose one token-management owner: retain Microsoft.Identity.Web and use its server-side distributed token acquisition/cache with token-free tickets, or use generic OIDC with a distributed `ITicketStore` and a separately specified refresh implementation. A ticket store and a token cache are different responsibilities, not interchangeable registrations. Microsoft.Identity.Web plus a BFF-only ticket store is also possible when central session invalidation is required.

Acceptance: a controlled server-side test unprotects the issued browser ticket and proves no access, refresh, or ID token is embedded. The browser sees only a protected session reference for the recommended design. Verify responses, logs, browser storage, and rendered state contain no tokens. Browser inspection alone cannot establish the contents of encrypted tickets.

### H2: Proxy mutations lack an explicit CSRF enforcement contract

Primary evidence: primary research lines 186 and 555 rely on existing antiforgery scaffolding. In `poc/modern-net10/Program.cs`, only POST `/signout` explicitly calls `ValidateRequestAsync`; there are no proxy endpoints yet. `RequireAuthorization` authenticates an ambient cookie but does not prove request intent.

The [antiforgery documentation](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0#antiforgery-with-minimal-apis) states that middleware does not short-circuit the pipeline. Do not assume its presence or metadata alone rejects a YARP request. The fetched page includes multiple framework monikers; .NET 11 automatic CSRF behavior must not be attributed to the .NET 10 proposal.

Require explicit validation and rejection before forwarding every cookie-authenticated mutation, including POST, PUT, PATCH, DELETE, uploads, and legacy WebForms postbacks. Use a session-bound antiforgery token in a header or compatible form field. Prohibit state-changing GETs. SameSite, CORS, and origin checks are complementary controls, not substitutes for this contract. OIDC protocol callbacks use their protocol-specific state/correlation protections and must not be broken by a blanket application form-token requirement.

Acceptance: missing, malformed, cross-user, and cross-origin CSRF cases are rejected with no upstream invocation for every unsafe route and content type; valid same-origin cases succeed. Include same-site sibling-origin attacks and any method override behavior. An unchanged WebForms page does not automatically emit a Core antiforgery token; form integration is explicit scope.

### H3: Token forwarding omits credential isolation and destination checks

Primary evidence: primary research lines 184, 190, and the token transform example. [YARP authentication documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0#flowing-credentials) confirms cookies and bearer credentials flow by default. [Request transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms-request?view=aspnetcore-10.0) provide header removal and allowlisting.

On token-bearing API routes, strip inbound `Cookie` and `Authorization`, then attach only the BFF-acquired access token for that route's resource/scopes. Reject token-acquisition failure before forwarding. Remove spoofable identity and forwarding headers; regenerate only trusted forwarding metadata. Do not forward the BFF session cookie to downstream APIs. Apply an explicit response policy for upstream `Set-Cookie` and redirects as well.

Static YARP destinations can implement an allowlist, but only if configuration is trusted, validated on startup/reload, and not overridden from request data. Bind each route to approved HTTPS scheme, host, port, path boundary, and token resource. Preserve disabled automatic redirects or revalidate every hop; a destination table alone proves none of these additional conditions.

Acceptance: a recording upstream receives no browser credentials, exactly the intended bearer token, and no spoofed identity headers. Host, URL, path, configuration-reload, and redirect escape attempts never send credentials to an unapproved destination. Any legacy cookie-sharing exception must be isolated from API routes and separately reviewed.

### H4: Authentication ownership and legacy ingress are unresolved

Primary evidence: primary research lines 651-680 promises an unchanged legacy app behind a Core-owned BFF while recommending remote authentication and cookie sharing. The [authentication migration guidance](https://learn.microsoft.com/en-us/aspnet/core/migration/fx-to-core/areas/authentication?view=aspnetcore-10.0) describes the opposite ownership for remote authentication: Core asks ASP.NET to authenticate, and logon/logoff remain with ASP.NET. It does not automatically make legacy trust a Core-issued identity.

Select one architecture explicitly. A legacy-owned transitional proxy can use remote authentication, but cannot be represented as moving authentication and token custody into Core. A Core-owned BFF needs a supported, tested legacy identity integration and authorization mapping; this can require legacy changes. Simply adding YARP or System.Web.Adapters provides neither that bridge nor browser CSRF integration.

WebForms identifies the UI framework, not the authentication mechanism. Inventory Forms Authentication, OWIN cookies, Windows authentication, SiteMinder, and custom modules separately. The [shared-cookie recipe](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing?view=aspnetcore-10.0#share-authentication-cookies-between-aspnet-4x-and-aspnet-core-apps) requires compatible OWIN middleware and `Microsoft.Owin.Security.Interop`, matching authentication type/scheme, Data Protection purpose/application/keys, and cookie scope. Matching `machineKey` is not sufficient for Forms Authentication interop.

That shared-cookie recipe explicitly does not work with `ITicketStore`. Do not combine it unmodified with H1's BFF-only reference-cookie design. Sharing key material also makes the legacy application part of the session-issuance trust boundary. Prefer keeping BFF session keys private to Core and designing a separate authenticated, narrowly scoped legacy identity channel when this isolation is required.

The legacy origin must reject direct browser ingress through its default hostname, alternate hostname, IP, and unprotected routes. Use network restrictions plus an authenticated BFF-to-origin channel; do not trust an identity header solely because the caller knows its name. Strip client-supplied identity headers at the BFF and validate the bridge identity at legacy. Preserve legacy resource-level authorization, tenant boundaries, and logout behavior. Remote-authentication endpoints, if used, must be restricted to their authorized Core callers.

Primary line 680's fallback short-circuit suggestion is not a universal BFF configuration. Any optimization that bypasses Core authentication, authorization, or CSRF enforcement defeats the proposed boundary. Restoring public ingress to a standalone comparison demo is a different objective from securing a production legacy origin.

Acceptance: record the authentication owner and bridge protocol; prove authorized and forbidden legacy operations through the BFF, no direct-origin bypass, rejection of forged identity headers, and logout across both applications. Demonstrate fallback routes execute required security checks. Treat claims of zero legacy changes as unproven until the real authentication modules and WebForms postbacks pass these checks.

### H5: Refresh, claims challenges, and logout lack a lifecycle contract

Primary evidence: primary research lines 555-620 sketches a refresher and reads `GetTokenAsync("access_token")`, while lines 710-735 promise CAE capability. Reading a saved token does not acquire or refresh it. `cp1` advertises capability, but does not establish that the application correctly handles a challenge.

The [claims challenge protocol](https://learn.microsoft.com/en-us/entra/identity-platform/claims-challenge) requires handling `401`/`WWW-Authenticate` challenges, ceasing reuse of the rejected token, and obtaining a token satisfying the claims request. Microsoft.Identity.Web supports client capability configuration; the resource application's optional-claims configuration controls issuance of `xms_cc`. Presence of that claim is not an end-to-end functional test.

Use one acquisition/refresh owner per flow, preferably Microsoft.Identity.Web/MSAL for this existing app. Acquire for the configured resource/scopes using the authenticated account. Define expiry skew, refresh failure, consent/interaction-required responses, and bounded challenge retries. Accept challenges only from trusted configured resources; validate size/format and use the configured authority, not an arbitrary header-supplied redirect destination. Bind interactive continuation to the session and a local allowlisted return path.

For browser fetch calls, return an explicit reauthentication result that the UI handles through top-level navigation. Do not blindly redirect a proxy fetch into an identity-provider page. Never automatically replay an unsafe mutation after interaction or an ambiguous failure without an explicit idempotency contract. Test that a challenge cannot create a retry loop or silently bypass the downstream denial.

The [MSAL cache guidance](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization) distinguishes distributed caches, L1 behavior, encryption, eviction, and failures. `AddDistributedMemoryCache` is process-local despite its name. Specify an actual shared backend, protected transport/storage, least-privilege access, retention, and shared protected Data Protection keys for multiple BFF instances. Token cache keys must preserve account/tenant/resource isolation.

An `IDistributedCache` registration does not prove safe concurrent refresh or session updates. Validate the chosen provider/library's behavior under simultaneous requests and multiple instances; add coordination or versioned updates where required. For a custom refresher, make refresh-token replacement and ticket updates atomic. Define cache-loss/outage behavior that denies or requires reauthentication, never falls back to browser token storage or another user's entry.

Logout must invalidate the BFF server-side session and prevent a previously captured session cookie from restoring access. Define the token-cache removal scope, concurrent-session policy, and optional identity-provider logout separately. Ensure in-flight refresh cannot recreate an invalidated session. Cookie deletion or local cache removal does not revoke every already-issued bearer token globally; document that limit rather than promising global revocation.

Acceptance: test expired access tokens, revoked refresh capability, real claims step-up, cache eviction/outage, cross-user isolation, concurrent refresh across two instances, key rotation, and logout racing refresh. Replayed pre-logout cookies must fail after the specified invalidation bound. Unsafe operations must not execute twice during these paths.

### H6: CI identity and optional skips cannot establish release evidence

Primary evidence: primary research lines 742-776 requires unattended CI proof and applies a skip-never-fail convention to unavailable live prerequisites. `api/Tests/LiveRedemptionTests.cs` instead labels its suite opt-in and skipped by default, and documents fresh interactive authorization-code prerequisites. That developer-suite convention is not a release acceptance policy.

[GitHub OIDC](https://docs.github.com/en/actions/concepts/security/openid-connect) identifies a workflow job to a cloud provider. It can authorize deployment and evidence collection; it does not authenticate a user through the BFF, satisfy that user's MFA, or establish a compliant user device. A successful `azure/login` step cannot substitute for this evidence.

Separate deterministic offline tests, deployment challenge probes, and controlled interactive user evidence. CI can orchestrate and collect the latter, but an authorized user/device/policy context is still required. Do not weaken Conditional Access, persist human credentials, or substitute app-only grants merely to make the run unattended.

Acceptance: the protected release gate checks a versioned manifest tied to commit, deployment, tenant/app/resource, policy mode, scenario, timestamps, and sanitized evidence. Missing mandatory evidence, failed prerequisites, expired artifacts, or exhausted ingestion retries must fail or block approval as incomplete, never become a successful release through skipped tests. Keep optional local tests skippable. Name the interactive evidence owner and the approving security owner.

### H7: Proposed telemetry overstates security and protocol proof

Primary evidence: primary research lines 600-620 and 697-737 labels two audiences/token IDs as OBO proof, requires a universal correlation join, claims device-claim removal, and treats report-only results as sufficient. These can produce a false security sign-off even after the proxy is implemented correctly.

The [OBO protocol](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow#protocol-diagram) requires an API to receive a user access token for itself and exchange it for another API's token. A BFF acquiring a delegated token through its own authorization-code flow can call a separate API without OBO. Only that API's further delegated call requires the OBO pattern in this chain. Two audiences or two token IDs alone cannot distinguish OBO from independent token acquisitions.

Require sanitized acquisition-path evidence and validated receiving-API identity/audience/authorization evidence. Keep the existing SPA/OBO experiment distinct from the proposed cookie-based BFF. Do not inspect third-party access-token internals as an application contract; resources own their formats. Validate tokens at the receiving API and never substitute an ID token for API authorization.

[Report-only Conditional Access](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-report-only) evaluates but does not enforce controls or cause MFA step-up. Keep it for impact analysis and label it accurately. A scoped, explicitly authorized enforced-policy test with positive and negative user/device cases is required to claim enforcement; no such policy change was performed in this review.

Correlate each actual request using available request IDs, bounded time, app/resource/user identifiers, and an application trace identifier. Do not require one `CorrelationId` across all four sign-in categories or infer missing categories mean failure: delegated user flows need not generate every workload identity category. Absence or ambiguity must remain an evidence gap, not be repaired by an overly broad join.

Moving acquisition server-side does not prove device claims disappear, make a bearer token un-replayable, or turn an observed IP change into an access control. Retain downstream user/device policy requirements; do not recommend replacing a compliant-device grant with a location condition merely because the BFF host has no user device. Verify actual policy scope and user-flow behavior with the tenant owner.

Acceptance: demonstrate the selected grant, correct audience/scopes and rejected wrong-audience/unauthorized requests, successful and denied enforced-policy cases, and a handled claims challenge. Separate observations from causal conclusions and future product-support claims. A `cp1` claim, nonempty credential type, report-only result, or changed IP alone must not satisfy those gates.

### Runtime correction: select by OS and deployed runtime

Primary research lines 8 and 651-670 overgeneralizes the 4.8 recommendation. The [Framework compatibility table](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements) supports 4.8 on Server 2016/2019 and 4.8.1 on Server 2022/2025. The [.NET Framework support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework) ties support to the underlying supported Windows release and identifies 4.5.2 as out of support since 2022-04-26.

Inventory the installed runtime, OS edition/build/lifecycle, application target framework, hosting platform, and dependencies before selecting a target. Framework 4.x upgrades are in-place: a project targeting 4.5.2 does not alone prove the deployed runtime is 4.5.2. Conversely, editing the target framework does not prove a supported runtime was deployed or patched.

Choose 4.8 where it is the supported OS ceiling and 4.8.1 on supported compatible hosts when validated. Neither categorical rejection of 4.8.1 nor an unnecessary OS upgrade follows from the evidence. Verify OS lifecycle separately from installability. Treat confirmed unsupported deployed runtime/OS as High; choosing supported 4.8 rather than 4.8.1 alone is not a High vulnerability. Neither target change implements BFF security.

Acceptance: retain OS/runtime inventory, lifecycle evidence, dependency compatibility, and regression results for the chosen deployment target. Do not promise closure of an EOL finding based only on project configuration edits.

### Verified route correction

Do not report legacy `/signin` as incorrect. `poc/legacy-net452/Startup.cs` explicitly challenges on `/signin`; its `/` route is an anonymous HTML page. `poc/modern-net10/Program.cs` protects `/` and does not define `/signin` as a login route. `.github/workflows/classic-net-bff-poc.yml` lines 463-565 already disable redirect following, check the Entra host, client ID and callback, and use legacy `/signin` versus modern `/`.

The primary research line 499 assertion that this workflow needs upgrading from a non-403 check is stale. Preserve these checks and add authority-path/tenant and code-flow assertions where required; do not replace them with a weaker status-only test. A challenge redirect is readiness evidence, not completed user authentication or token-redemption evidence.

## Recommended architecture

1. Use a separately scoped Core BFF with tenant-restricted OIDC authorization code plus PKCE. Keep the SPA/OBO demo and legacy/modern comparison apps distinct from this proposed application.
2. Retain Microsoft.Identity.Web/MSAL as the acquisition owner, a real distributed server-side token cache, and token-free authentication properties. Use a BFF-only distributed ticket store for central session invalidation and a protected shared Data Protection key ring. Keep the browser limited to the hardened reference cookie and antiforgery material.
3. Enforce authentication, resource authorization, and explicit CSRF validation before every relevant proxy call. Define session-cookie SameSite separately from OIDC correlation/nonce cookie requirements; test the selected callback mode rather than assuming one SameSite setting fits all cookies.
4. Isolate approved API routes, strip browser credentials, and attach only the resource-specific access token. Deny invalid destinations and untrusted redirects; require the API to validate issuer, audience, expiry, scopes/roles, and business authorization.
5. Keep the legacy origin private to authenticated BFF callers. Design and validate the legacy principal bridge and postback integration before claiming Core owns authentication. If choosing legacy-owned remote authentication or OWIN shared cookies instead, document that as a distinct transitional architecture with different trust and session constraints.
6. Specify refresh, claims interaction, concurrent updates, outage behavior, and bounded logout invalidation as one lifecycle contract. Release only when the mandatory offline, network, browser, and controlled live evidence gates pass.

## Acceptance summary

* Custody: decrypted browser ticket contains no tokens; no token material appears in browser storage, responses, logs, or evidence pages.
* Request security: every cookie-authenticated mutation rejects invalid CSRF before any upstream invocation; no mutation relies on GET.
* Proxy boundary: no browser credentials or forged identity headers reach APIs; wrong resources, destinations, and redirect escapes are denied.
* Legacy boundary: direct ingress fails, bridge identity is authenticated, authorization survives, and fallback routes cannot bypass controls.
* Lifecycle: expiry, challenges, cross-instance races, cache failure, and logout cannot restore invalid sessions or duplicate unsafe operations.
* Compatibility: deployed OS/runtime support and actual Forms Authentication/OWIN integration are evidenced, not inferred from project names.
* Proof: correct existing challenge routes pass; controlled user tests prove intended policy enforcement; missing mandatory evidence blocks release.

## Constraints for the parent

* Markdown requires YAML frontmatter. With a frontmatter `title`, start at H2, not H1; use consecutive heading levels and blank lines around blocks.
* Use ASCII punctuation, consistent `*` list markers, language-tagged fences, no trailing whitespace, and one final newline.
* Use two-space nested-list indentation, ISO dates, headings under 80 characters where practical, and descriptive external link text. Avoid repeated blank lines and unnecessarily long prose lines.
* Use precise professional language, no em dashes, no bold-prefix list items, no filler, and distinguish verified facts from assumptions.
* Research artifacts cite workspace-relative paths as plain text, optionally in code spans, without Markdown links or `#file:` directives. External URLs may be links.
* `.github/copilot-instructions.md` referenced by the primary research is absent in this checkout. Do not claim it was read.

## Open questions

* What OS editions/builds and installed Framework runtimes actually host the customer estate?
* Which legacy authentication modules and cookie mechanisms are active, and may legacy principal/postback integration be changed?
* Which APIs, resource registrations, delegated scopes, and tenant boundaries belong to the target flow? Is API-to-API OBO actually required?
* What private ingress and authenticated BFF-to-origin mechanisms are available, including alternate hostnames and operational access?
* What cache/session backend, concurrent-session policy, and logout invalidation bound are required?
* Who supplies the controlled user/device cases, authorizes enforced-policy tests, and approves the release evidence?

## Recommended next research

These items require customer details or implementation evidence and were not completed in this research-only review.

* [ ] Verify customer OS/runtime inventory and actual legacy authentication configuration.
* [ ] Select and prototype the legacy bridge, including WebForms CSRF integration and private ingress denial tests.
* [ ] Validate the selected token/session cache provider under multi-instance refresh and logout races.
* [ ] Capture authorized interactive user/device/policy evidence and verify available telemetry joins for the chosen grant.
* [ ] Define the mandatory release-evidence manifest, freshness limits, owners, and approval gate.
