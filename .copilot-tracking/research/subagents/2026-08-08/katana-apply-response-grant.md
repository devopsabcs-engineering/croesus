---
title: Katana ApplyResponseGrant callback failure research
description: Source-level localization after the cookie-manager correction
ms.date: 2026-08-08
ms.topic: troubleshooting
---

## Research questions

* Why does Katana 4.2.3 still report `System.Web.HttpException` from
  `ApplyResponseGrant` after commit `450a1c8` replaced
  `SystemWebCookieManager` with `CookieManager`?
* Which operation inside Katana's broad exception boundary is most likely to
  throw?
* What secret-safe instrumentation distinguishes cookie append failure from a
  later response-header failure?
* Does IIS integrated-pipeline staging provide a stronger correction
  hypothesis?

## Scope and safety

The review covers the controlling legacy authentication source, focused tests,
the supplied live result for commit `450a1c8`, Katana 4.2.3 source, and
System.Web response-header documentation. No callback parameters,
authorization codes, state, nonce, tokens, cookies, ticket data, identities,
credentials, URLs, raw headers, exception messages, or stack traces are
collected or recorded.

## Decisive live evidence

The callback still reaches the cookie provider's fixed
`ApplyResponseGrant` failure marker after commit `450a1c8` changed
`CookieOptionsFactory` to `new CookieManager()`. The recorded exception remains
`System.Web.HttpException` with HRESULT `0x80004005`, and the request still ends
as IIS `404.0`.

This result falsifies the earlier claim that `SystemWebCookieManager` itself was
the cause. `CookieManager` avoids `HttpResponse.AppendCookie`, but it still
calls `IOwinResponse.Cookies.Append`. Under the SystemWeb host, that operation
ultimately mutates the System.Web-backed response header collection. A
header-based manager therefore does not permit a cookie to be added after IIS
or System.Web has committed the response.

`CookieExceptionContext.Rethrow` defaults to `true`. The local exception
callback logs the bounded category but does not change `Rethrow`, so the
original failure continues through the Katana and System.Web host path.

## Controlling pipeline

`Startup.Configuration` registers the relevant components in this order:

1. The outer authentication failure boundary
2. Cookie authentication
3. OpenID Connect authentication

This order is correct at the OWIN middleware level. Reversing cookie and OIDC
would allow OIDC to handle the callback before the inner cookie handler had
initialized its authentication type, so no cookie handler would consume the
queued sign-in grant.

The IIS integrated-pipeline staging is more important than the visible order:

* Katana's `UseCookieAuthentication(options)` overload adds
  `UseStageMarker(PipelineStage.Authenticate)` immediately after cookie
  middleware.
* The outer boundary and cookie middleware therefore run in the
  `AuthenticateRequest` segment.
* `UseOpenIdConnectAuthentication` adds no stage marker.
* OIDC is registered after the cookie marker and therefore remains in Katana's
  default `PreRequestHandlerExecute` segment.
* The earlier cookie segment is suspended at Katana's stage exit point. OIDC
  later queues the sign-in and sets the callback redirect. Cookie teardown then
  applies that sign-in while the earlier segment unwinds.

Microsoft's integrated-pipeline guidance confirms that unmarked OWIN
middleware defaults to `PreRequestHandlerExecute` and that a marker applies to
all preceding middleware since the prior split. Katana's cookie extension
source independently confirms that cookie authentication is intended to run at
`AuthenticateRequest` by default.

This split-stage arrangement is the leading explanation for why the cookie
grant encounters a response that System.Web already considers committed. The
IIS `404.0` is also consistent with the callback path being treated as an
unmapped resource while OIDC runs at the late pre-handler stage, although the
available evidence does not prove which IIS component first commits the
headers.

Adding `app.UseStageMarker(PipelineStage.Authenticate)` after OIDC is the
narrowest stage correction hypothesis. Because the current stage is already
`Authenticate`, Katana's stage-marker implementation does not create an
out-of-order split. It groups the subsequently registered OIDC middleware into
the existing authentication segment. This hypothesis should be validated after
the operation-level marker run rather than applied as an unexplained fix.

## ApplyResponseGrant operation order

For the successful callback's queued sign-in, Katana 4.2.3 performs these
operations:

1. Look up the sign-in and sign-out grants and determine whether work exists.
2. Await `AuthenticateAsync()` before entering the method's local `try` block.
3. Construct `CookieOptions`.
4. Construct `CookieResponseSignInContext`.
5. Populate issued and expiry properties.
6. Invoke `Options.Provider.ResponseSignIn`.
7. Set persistent-cookie expiry when requested.
8. Create the `AuthenticationTicket`.
9. Update the optional session store when configured.
10. Call `Options.TicketDataFormat.Protect(model)`.
11. Call `Options.CookieManager.AppendResponseCookie`.
12. Construct `CookieResponseSignedInContext`.
13. Invoke `Options.Provider.ResponseSignedIn`.
14. Set `Cache-Control: no-cache`.
15. Set `Pragma: no-cache`.
16. Set `Expires: -1`.
17. Evaluate cookie login and logout redirect conditions.
18. Optionally invoke `Options.Provider.ApplyRedirect`.

One broad `try` covers operations 3 through 18. Any exception from that range
is reported as `ApplyResponseGrant`, so the existing phase marker cannot locate
the failing operation more precisely.

The local cookie provider's sign-in callbacks are no-ops apart from the current
exception diagnostic. `LoginPath` and `LogoutPath` are unset, and the OIDC
callback response is already a 302. The cookie middleware's optional redirect
branch is therefore not reachable in this flow.

## Ranked hypotheses

1. The split-stage pipeline lets IIS or System.Web commit the callback response
   before the earlier cookie segment applies its grant. The first attempted
   late mutation can be `Set-Cookie` or, if cookie append succeeds at the
   commitment boundary, the immediately following cache headers. Confidence:
   high.
2. `CookieManager.AppendResponseCookie` throws while adding `Set-Cookie` to the
   System.Web-backed response headers. `HttpResponse.AppendHeader` documents a
   `HttpException` when a header is appended after headers have been sent.
   Confidence: high, pending one marker.
3. Cookie append succeeds, then `Cache-Control` throws. The SystemWeb response
   header adapter's set path removes the existing value and appends the new
   header through `HttpResponse.AppendHeader`. This is the strongest candidate
   if cookie append success is observed. Confidence: high under that condition.
4. `Pragma` or `Expires` throws after the preceding mutations succeed. Both use
   the same response-header facade. Confidence: medium, because
   `Cache-Control` executes first.
5. Ticket protection or a provider callback throws. `MachineKey.Protect`
   documents argument and cryptographic failures rather than the observed
   late-header `HttpException`; the local sign-in callbacks contain no work.
   Confidence: low.

Changing cookie-manager implementation again does not address the leading
cause. Reversing cookie and OIDC middleware is invalid. Suppressing the
exception through `Rethrow = false` would hide a failed session-cookie grant and
must not be used as a correction.

## Smallest secret-safe instrumentation

Add a temporary `ICookieManager` decorator around the current `CookieManager`.
It must delegate all arguments unchanged and emit only these fixed markers:

* `CookieAppendBegin` immediately before `AppendResponseCookie`
* `CookieAppendSucceeded` immediately after the delegated call returns
* `CookieAppendFailed` in a catch block that immediately rethrows with `throw;`

Add fixed provider markers at the two public boundaries surrounding Katana's
inaccessible internal work:

* `ResponseSignInEntered` from `OnResponseSignIn`, before ticket protection
* `ResponseSignedInEntered` from `OnResponseSignedIn`, after cookie append

Retain the existing fixed `ApplyResponseGrantFailed` marker. The logger must not
receive the cookie name, value, options, path, domain, request URL, redirect
URI, response headers, identity, ticket, exception object, message, HResult, or
stack trace for this discriminating run.

The decorator must not inspect or transform its arguments, retry writes,
suppress exceptions, set `Rethrow`, or add response headers. Tests should use a
recording inner manager and a throwing inner manager to prove exact
single-call delegation, fixed marker order, and preservation of the original
exception instance.

## Marker interpretation

1. `ResponseSignInEntered`, `CookieAppendBegin`, then `CookieAppendFailed`
   means the `Set-Cookie` mutation failed.
2. `CookieAppendSucceeded`, then failure before `ResponseSignedInEntered` means
   context construction or callback entry failed. This is unlikely with the
   fixed callback.
3. `ResponseSignedInEntered`, then `ApplyResponseGrantFailed` means cookie
   append succeeded and one of the three cache-header writes failed.
4. No `ResponseSignInEntered`, followed by `ApplyResponseGrantFailed`, means
   options or sign-in context construction failed.
5. `ResponseSignInEntered` with no `CookieAppendBegin`, followed by
   `ApplyResponseGrantFailed`, means ticket construction, session storage, or
   ticket protection failed.

If `ResponseSignedInEntered` is the final success marker, `Cache-Control` ranks
first because it is the next operation. Distinguishing the three cache writes
individually would require replacing or instrumenting the host response-header
facade, which is a larger and more behavior-sensitive diagnostic than the
evidence currently warrants.

## Recommended validation sequence

1. Deploy only the fixed-marker instrumentation and perform one
   user-controlled callback attempt.
2. Classify the failure using the marker sequence without collecting any
  variable authentication or response data.
3. Add `UseStageMarker(PipelineStage.Authenticate)` after OIDC registration.
4. Repeat the challenge and callback checks.
5. Confirm that the callback redirects successfully, the authenticated endpoint
   recognizes the session, and no `ApplyResponseGrantFailed` marker appears.
6. Remove the temporary operation markers and decorator after validation.

The focused local tests should prove middleware order remains cookie before
OIDC, the post-OIDC stage marker is present, cookie security and lifetime
settings remain unchanged, and the diagnostic decorator is transparent. A
real IIS integrated-pipeline callback remains the decisive validation because
`Microsoft.Owin.Testing.TestServer` does not reproduce System.Web stages or
header commitment.

## Key findings

* Commit `450a1c8` falsified the manager-specific diagnosis.
* Header-based `CookieManager` still reaches System.Web response headers.
* `ApplyResponseGrant` covers cookie append, provider callbacks, three cache
  header writes, and redirect evaluation under one catch.
* The current pipeline splits cookie authentication at `AuthenticateRequest`
  while leaving OIDC at `PreRequestHandlerExecute`.
* Fixed markers can distinguish cookie append from post-cookie cache-header
  failure without handling authentication data.
* A post-OIDC `Authenticate` stage marker is the leading direct correction
  hypothesis once the operation boundary is confirmed.

## References

* `poc/legacy-net452/Authentication/CookieOptionsFactory.cs`
* `poc/legacy-net452/Authentication/OidcOptionsFactory.cs`
* `poc/legacy-net452/Startup.cs`
* `poc/legacy-net452/Telemetry/AuthenticationEventLogger.cs`
* `poc/legacy-net452/Tests/CookieOptionsFactoryTests.cs`
* `.copilot-tracking/changes/2026-08-07/classic-net-bff-appservice-deployment-changes.md`
* [Katana 4.2.3 CookieAuthenticationHandler](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security.Cookies/CookieAuthenticationHandler.cs)
* [Katana 4.2.3 CookieAuthenticationExtensions](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security.Cookies/CookieAuthenticationExtensions.cs)
* [Katana 4.2.3 OpenIdConnectAuthenticationExtensions](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security.OpenIdConnect/OpenIdConnectAuthenticationExtensions.cs)
* [Katana 4.2.3 CookieManager](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin/Infrastructure/CookieManager.cs)
* [Katana 4.2.3 CookieExceptionContext](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security.Cookies/Provider/CookieExceptionContext.cs)
* [Katana 4.2.3 OwinHttpModule](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Host.SystemWeb/OwinHttpModule.cs)
* [Katana 4.2.3 IntegratedPipelineContextStage](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Host.SystemWeb/IntegratedPipeline/IntegratedPipelineContextStage.cs)
* [OWIN middleware in the IIS integrated pipeline](https://learn.microsoft.com/en-us/aspnet/aspnet/overview/owin-and-katana/owin-middleware-in-the-iis-integrated-pipeline)
* [HttpResponse.AppendHeader](https://learn.microsoft.com/en-us/dotnet/api/system.web.httpresponse.appendheader?view=netframework-4.8.1)
* [MachineKey.Protect](https://learn.microsoft.com/en-us/dotnet/api/system.web.security.machinekey.protect?view=netframework-4.8.1)

## Follow-on research

* Inspect the fixed-marker sequence from one controlled deployment callback.
* If cookie append fails, identify which IIS or System.Web event committed the
  response before the grant only if the stage correction does not resolve it.
* If `ResponseSignedInEntered` appears, avoid deeper cache-header
  instrumentation until the post-OIDC stage marker has been tested.

## Clarifying questions

None. The next discriminating step is fully defined and does not require
additional authentication data.
