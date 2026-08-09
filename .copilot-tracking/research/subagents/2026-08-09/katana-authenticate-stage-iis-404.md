---
title: Katana Authenticate Stage IIS 404 Research
description: Investigation of a successful Katana OIDC callback followed by an IIS 404 response
author: GitHub Copilot
ms.date: 2026-08-09
ms.topic: troubleshooting
---

## Research scope

Determine the most likely control flow that allows a successful Katana 4.2.3
OpenID Connect callback to finish without authentication failure markers, but
still return an IIS 404.0 with Win32 status 2 for `/signin-oidc`.

The investigation covers:

* Katana `OwinHttpModule` integrated-pipeline stage registration and execution
* `OpenIdConnectAuthenticationHandler` callback-success control flow
* IIS and ASP.NET handler selection for extensionless virtual paths
* Whether an OWIN response produced at `AuthenticateRequest` can be replaced by
  a later static-file 404
* Small corrections that retain early cookie grant application
* Security implications and focused validation checks

## Local evidence

* `poc/legacy-net452/Startup.cs` registers cookie middleware, OIDC middleware,
  and then `UseStageMarker(PipelineStage.Authenticate)`
* `poc/legacy-net452/web.config` has no explicit module or handler mapping
* Middleware after the marker is assigned to a later default stage
* `poc/legacy-net452/Tests/StartupIntegrationTests.cs` uses Katana TestServer,
  which does not model IIS integrated-pipeline handler selection
* Commit `f86c6d6` moved the authentication middleware into the explicit
  `Authenticate` stage according to the supplied incident context

## Working hypothesis

The callback is recognized and redeemed during `AuthenticateRequest`, but the
application replaces Katana's validated ticket with a projected ticket that
drops `AuthenticationProperties.RedirectUri`. Katana queues the cookie sign-in,
finds no return URI, and returns `false` from its callback handler. That return
value deliberately invokes the next OWIN component and reaches the stage exit
point. IIS then maps the nonphysical `/signin-oidc` request to StaticFile, which
produces 404.0 with Win32 status 2.

## Findings

### Most likely precise control flow

1. The `/signin` branch challenges OIDC with
  `AuthenticationProperties.RedirectUri = "/"`.
2. Katana protects those properties in OIDC `state`. The callback unprotects the
  same properties, including the return URI.
3. Katana redeems the authorization code, removes the transient PKCE verifier,
  validates the ID token, and raises `SecurityTokenValidated`.
4. `OidcOptionsFactory` replaces `notification.AuthenticationTicket` with the
  result of `SessionIdentityProjector.CreateMinimalTicket`.
5. `CreateMinimalTicket` creates new `AuthenticationProperties` containing only
  persistence, refresh, issue, and expiry settings. It does not copy
  `source.Properties.RedirectUri`.
6. `OpenIdConnectAuthenticationHandler.InvokeReplyPathAsync` receives the
  projected ticket. Because the identity is present, it calls `SignIn`, which
  lets the cookie middleware issue the session cookie during teardown.
7. The same method redirects and returns `true` only when
  `ticket.Properties.RedirectUri` is nonempty. Here it is empty, so the method
  returns `false`.
8. `AuthenticationMiddleware.Invoke` calls the next OWIN component. The
  Authenticate-stage exit point clears Katana's `PreventNextStage` and
  `_responseShouldEnd` flags, so the integrated IIS pipeline continues.
9. The later application middleware has no `/signin-oidc` branch and eventually
  reaches the host default application. IIS executes the selected StaticFile
  handler for the nonphysical path and reports 404.0 with Win32 status 2.

This sequence explains the otherwise unusual combination of successful protocol
processing, a session-cookie grant, no authentication-failure marker, and a
client-visible StaticFile 404.

### Why response overwrite is not the primary mechanism

Katana 4.2.3 does not merely set a redirect and then allow IIS to continue when
OIDC returns `true`. `IntegratedPipelineContextStage` starts each segment with
`_responseShouldEnd = true`. If middleware handles the callback without invoking
the stage exit point, the segment completion callback calls
`HttpApplication.CompleteRequest()`. Microsoft documents that method as jumping
directly to `EndRequest`, bypassing the remaining handler pipeline.

`IOwinResponse.Redirect` itself only sets status 302 and the `Location` header.
The request termination comes from the System.Web host's segment completion, not
from the redirect helper. Therefore, a later StaticFile 404 is evidence that the
OIDC callback returned `false`, the OWIN module did not execute for the request,
or another component explicitly re-entered the pipeline. The local property
projection provides a direct, source-visible reason for the first case.

### Smallest direct correction

Preserve the validated ticket's return URI when constructing the minimal ticket:

```csharp
var properties = new AuthenticationProperties
{
   AllowRefresh = false,
   IsPersistent = false,
   IssuedUtc = source.Properties.IssuedUtc,
   ExpiresUtc = source.Properties.ExpiresUtc,
   RedirectUri = source.Properties.RedirectUri
};
```

This is the smallest correction because it restores the exact condition Katana
uses to redirect and return `true`, while retaining the existing decision not to
copy the source property dictionary into the session cookie. It avoids retaining
tokens, the PKCE verifier, session-state metadata, or other protocol properties.

The existing `MinimalTicketDropsProtocolClaimsAndAuthenticationProperties` test
does not set or assert `RedirectUri`, so it protects the minimization policy but
does not cover the routing property required to finish the callback.

## Option comparison

1. Preserve `source.Properties.RedirectUri` in the projected ticket.
  This directly corrects the demonstrated control flow and has the smallest
  behavioral and security scope.
2. Add a regression test for return-URI preservation and an OIDC callback test
  that asserts a redirect instead of downstream execution. This prevents the
  same projection defect from returning.
3. Add an exact managed handler mapping for `signin-oidc` only if host evidence
  shows that `OwinHttpModule` does not run for that path. A suitable diagnostic
  mapping in Integrated .NET 4 mode is:

  ```xml
  <handlers>
    <add name="CroesusOidcCallback"
       path="signin-oidc"
       verb="GET"
       type="System.Web.Handlers.TransferRequestHandler"
       resourceType="Unspecified"
       preCondition="integratedMode,runtimeVersionv4.0" />
  </handlers>
  ```

  IIS handler paths omit the leading slash. `resourceType="Unspecified"`
  permits a virtual path with no physical file. This mapping can make the
  `managedHandler` precondition true for the callback, but it does not restore
  the dropped return URI. In the locally demonstrated flow, OIDC already ran,
  so this is not the first correction.
4. Add callback-specific terminal middleware only as a defensive fallback after
  the property fix. It would need an explicit safe redirect and evidence that
  OIDC completed successfully. A path-only terminal response can hide protocol
  failures and create misleading success responses.
5. Call `CompleteRequest` only when a component has positively handled the
  callback. Calling it unconditionally can terminate skipped or invalid OIDC
  messages before error handling runs. Katana already calls it when OIDC
  returns `true`.
6. Do not create a physical `signin-oidc` file. It can replace 404 with static
  content but does not restore Katana's post-authentication redirect and expands
  the callback surface unnecessarily.
7. Do not enable `runAllManagedModulesForAllRequests` as the initial fix. It
  invokes every managed module for every request, increasing performance and
  behavioral scope. An exact callback handler mapping is narrower when managed
  handler ownership is genuinely absent.
8. Do not add another stage marker. Cookie authentication already inserts an
  Authenticate marker, and commit `f86c6d6` explicitly places OIDC at that
  stage. Stage placement cannot restore a property removed after validation.

### Security implications

* Copy only `RedirectUri`, not the complete source dictionary. This preserves the
  existing no-token and no-protocol-secret cookie policy.
* Continue relying on Katana's protected `state` as the source of the return URI.
  Do not accept a new callback query parameter as an arbitrary redirect target.
* Keep the challenge return URI application-relative or validate it as a local
  destination before issuing a challenge to prevent open redirects.
* Restrict any IIS handler mapping to the exact callback path and required GET
  verb. Avoid wildcard handler mappings and broad managed-module activation.
* Do not record callback query strings, authorization codes, `state`, cookies,
  tokens, claims, raw exception messages, or stack traces during validation.

## Focused validation

1. Add a unit test that supplies a source ticket with `RedirectUri = "/"` and
  protocol-like dictionary entries. Assert that the projected ticket preserves
  `/` while still dropping those dictionary entries.
2. Run the focused projection and OIDC option tests.
3. Deploy only the projection correction. Recycle the application pool so the
  updated assembly and OWIN startup pipeline load together.
4. Start one fresh browser session and perform one sign-in. Record only the
  callback's HTTP status, whether a `Location` header is present, the redirect
  path, and whether a session cookie was set. Do not record header values other
  than the non-sensitive redirect path.
5. Expect `/signin-oidc` to return 302 to `/`, followed by a successful root
  request. Confirm that IIS no longer records 404.0/2 for the callback.
6. Confirm the authenticated session endpoint succeeds after the redirect and
  that no authentication-failure classification was emitted.
7. If the callback still returns 404, use Failed Request Tracing or IIS handler
  diagnostics to record only the selected handler name and whether
  `OwinHttpModule` reached `AuthenticateRequest`. If the module did not execute,
  test the exact `TransferRequestHandler` mapping above as one isolated host
  change.
8. If the exact mapping changes module participation but the callback still
  falls through, instrument a boolean or categorical marker around ticket
  creation and OIDC handling. Record only whether the return URI was present
  and whether downstream middleware ran.

Katana TestServer can validate property preservation and OWIN short-circuiting,
but it cannot validate IIS stage transitions, handler selection, StaticFile
behavior, or `CompleteRequest`. The final host check must run under IIS Integrated
mode with CLR v4.

## References

* Local startup pipeline: `poc/legacy-net452/Startup.cs`
* Local ticket projection: `poc/legacy-net452/Authentication/SessionIdentityProjector.cs`
* Local OIDC configuration: `poc/legacy-net452/Authentication/OidcOptionsFactory.cs`
* Local projection tests: `poc/legacy-net452/Tests/SessionProjectionTests.cs`
* Local IIS configuration: `poc/legacy-net452/web.config`
* [Katana 4.2.3 OIDC handler](https://raw.githubusercontent.com/aspnet/AspNetKatana/v4.2.3/src/Microsoft.Owin.Security.OpenIdConnect/OpenidConnectAuthenticationHandler.cs)
* [Katana 4.2.3 OIDC middleware](https://raw.githubusercontent.com/aspnet/AspNetKatana/v4.2.3/src/Microsoft.Owin.Security.OpenIdConnect/OpenIdConnectAuthenticationMiddleware.cs)
* [Katana 4.2.3 integrated stage context](https://raw.githubusercontent.com/aspnet/AspNetKatana/v4.2.3/src/Microsoft.Owin.Host.SystemWeb/IntegratedPipeline/IntegratedPipelineContextStage.cs)
* [Katana 4.2.3 System.Web module registration](https://raw.githubusercontent.com/aspnet/AspNetKatana/v4.2.3/src/Microsoft.Owin.Host.SystemWeb/PreApplicationStart.cs)
* [ASP.NET integration with IIS](https://learn.microsoft.com/en-us/iis/application-frameworks/building-and-running-aspnet-applications/aspnet-integration-with-iis)
* [IIS handler mapping reference](https://learn.microsoft.com/en-us/iis/configuration/system.webserver/handlers/add)
* [IIS managed modules reference](https://learn.microsoft.com/en-us/iis/configuration/system.webserver/modules/)
* [HttpApplication.CompleteRequest](https://learn.microsoft.com/en-us/dotnet/api/system.web.httpapplication.completerequest?view=netframework-4.8.1)

## Clarifying questions

None. The proposed first edit remains falsifiable: if preserving the protected
return URI does not change the callback from fall-through to 302, IIS module and
handler participation becomes the next isolated variable.
