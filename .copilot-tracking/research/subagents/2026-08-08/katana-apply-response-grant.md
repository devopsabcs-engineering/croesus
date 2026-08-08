---
title: Katana ApplyResponseGrant callback failure research
description: Evidence review of the legacy Katana 4.2.3 cookie grant failure
ms.date: 2026-08-08
ms.topic: troubleshooting
---

## Research questions

* What exact operations run inside Katana 4.2.3 `ApplyResponseGrant`?
* Which operations can throw `System.Web.HttpException` with HRESULT `0x80004005`?
* What root cause best fits a queued sign-in followed by a response-start timing failure?
* What is the smallest direct correction, and what safe probe would discriminate further?
* Which files and tests should a later implementation change?

## Local evidence

* `poc/legacy-net452/Authentication/CookieOptionsFactory.cs` explicitly sets `CookieManager = new SystemWebCookieManager()`. Katana host defaults therefore do not decide session-cookie behavior in this application.
* The cookie provider already records `context.Location` and the exception. The reported phase is `ApplyResponseGrant`, after OIDC has accepted the callback and queued the session sign-in.
* `poc/legacy-net452/Authentication/SessionIdentityProjector.cs` deliberately creates a small identity, making cookie chunking or header-size overflow a poor fit for the first successful session cookie.
* `poc/legacy-net452/web.config` has no explicit `OwinHttpModule` registration. The earlier duplicate-module hypothesis is therefore falsified for the current deployment shape.
* `poc/legacy-net452/Tests/CookieOptionsFactoryTests.cs` currently requires a `SystemWebCookieManager`. That assertion fixes the application to the exact 4.2.3 behavior implicated below; it does not execute under real System.Web.
* The in-memory startup tests cannot reproduce IIS/System.Web response commit semantics. The deployment script is the available host-level validation surface.

## Katana source evidence

`CookieAuthenticationHandler.ApplyResponseGrantAsync` does the following for a
queued sign-in:

1. Reads the sign-in/sign-out instructions for the configured authentication type and authenticates the current request when needed.
2. Clones the ticket identity and properties, fills issued/expiry timestamps, and optionally invokes the session store.
3. Builds `CookieOptions` from the configured domain, path, HTTP-only, secure, SameSite, and persistent-expiry settings.
4. Invokes the cookie provider's `ResponseSignIn` callback.
5. Protects the authentication ticket.
6. Calls `Options.CookieManager.AppendResponseCookie` for sign-in, or `DeleteCookie` for sign-out.
7. Writes `Cache-Control`, `Pragma`, and `Expires` response headers.
8. Optionally applies the cookie middleware's login redirect.

The observed `System.Web.HttpException` is most naturally thrown at operation
6. `SystemWebCookieManager.AppendResponseCookie` creates an `HttpCookie` and
calls `HttpResponse.AppendCookie`. System.Web throws
`HttpException (0x80004005)` when headers have already been written. A delete
uses the same append path with an expired cookie. Operations 7 and 8 can also
fail after response commit, but the specific "cannot append cookies" message
and upstream stack identify operation 6.

Katana 4.2.3 introduced the behavior change that matters here. Commit
`423f80e7f82708e3f78a596cf405a2a5595c12f1` made middleware obtain default
cookie managers from the host, and `OwinAppContext` began registering
`SystemWebCookieManager` and `SystemWebChunkingCookieManager`. In 4.2.2, cookie
authentication defaulted directly to header-based `ChunkingCookieManager`.

AspNetKatana issue 555 reports the same 4.2.3-only exception and HRESULT from
`HttpResponse.AppendCookie` inside `ApplyResponseGrantAsync` on IIS. A Katana
contributor explains that 4.2.3 now uses stricter System.Web-aware managers and
recommends overriding them with `CookieManager` and `ChunkingCookieManager` as
the diagnostic workaround. The issue remains open, and one reporter says
downgrading to 4.2.2 restored service. This is strong corroboration, not proof
that every occurrence has the same preceding response writer.

## Ranked findings

1. **Explicit `SystemWebCookieManager` meets a response already committed by System.Web/IIS.** This directly explains the exception type, HRESULT, message, phase, and why the OIDC failure notification does not run. Katana documents that response application can occur either when a write/flush starts headers or during teardown. Confidence: high.
2. **Katana 4.2.3's manager-default change exposes a compatibility regression or stricter late-write check.** The exact upstream report, 4.2.2 comparison, and contributor workaround make this highly likely as the enabling change. The application opts into the same manager explicitly. Confidence: high.
3. **An unidentified System.Web module or application operation commits the callback response before cookie teardown.** This remains the likely trigger if replacing the manager only changes the exception surface or does not restore a usable session. No local callback redirect/body write was found, so any such writer is outside the reviewed callback code or is host timing. Confidence: medium.
4. **Ticket protection, cookie attributes, or ticket size.** Ticket protection occurs before `AppendCookie`, while the exception specifically requires a late response mutation. The ticket is intentionally small and the cookie attributes are ordinary. Confidence: low.

## Falsifiable next edit

Change only `CookieOptionsFactory.Create` from
`new SystemWebCookieManager()` to `new CookieManager()`. Keep Katana at 4.2.3
and keep the cookie name, path, security attributes, lifetime, and identity
unchanged.

This is both the smallest correction and the cleanest probe:

* **Confirmed:** an IIS-hosted callback returns to the original URI, emits the session `Set-Cookie`, and a subsequent `/me` request is authenticated. The direct correction is to retain the header-based manager or explicitly set Katana's header-based defaults before authentication middleware.
* **Falsified:** the same callback still fails in `ApplyResponseGrant` or no session cookie is issued. Revert the probe and instrument the existing cookie exception event with only response-start/header-written booleans and a fixed operation stage; then identify the earlier response writer.

A broader package downgrade to 4.2.2 is corroborated by the upstream report,
but it changes all Katana packages and behavior. It is a safe rollback option,
not the best first discriminator when one manager substitution isolates the
relevant change.

A later implementation should touch:

* `poc/legacy-net452/Authentication/CookieOptionsFactory.cs`
* `poc/legacy-net452/Tests/CookieOptionsFactoryTests.cs`
* `scripts/test-classic-net-bff-deployment-entra-static.ps1`, or the equivalent IIS-hosted callback smoke test

The unit test should require `CookieManager` and preserve all cookie security
settings. The decisive test must run through real IIS/System.Web and verify both
callback completion and authenticated replay; `TestServer` is insufficient.

## References

* `poc/legacy-net452/Authentication/CookieOptionsFactory.cs`
* `poc/legacy-net452/Authentication/SessionIdentityProjector.cs`
* `poc/legacy-net452/Tests/CookieOptionsFactoryTests.cs`
* `poc/legacy-net452/web.config`
* `scripts/test-classic-net-bff-deployment-entra-static.ps1`
* [Katana 4.2.3 comparison](https://github.com/aspnet/AspNetKatana/compare/v4.2.2...v4.2.3)
* [Katana default cookie-manager commit](https://github.com/aspnet/AspNetKatana/commit/423f80e7f82708e3f78a596cf405a2a5595c12f1)
* [AspNetKatana issue 555](https://github.com/aspnet/AspNetKatana/issues/555)
* [Katana 4.2.3 CookieAuthenticationHandler](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security.Cookies/CookieAuthenticationHandler.cs)
* [Katana 4.2.3 SystemWebCookieManager](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Host.SystemWeb/SystemWebCookieManager.cs)
* [Katana 4.2.3 AuthenticationHandler](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security/Infrastructure/AuthenticationHandler.cs)
* [HttpResponse.AppendCookie](https://learn.microsoft.com/en-us/dotnet/api/system.web.httpresponse.appendcookie?view=netframework-4.8.1)

## Clarifying questions

None. The next edit has a single-variable, host-level falsification test.
