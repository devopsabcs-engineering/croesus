---
title: Legacy Katana OIDC callback InvalidOperationException research
description: Focused investigation of the deployed .NET Framework 4.5.2 callback failure
ms.date: 2026-08-08
ms.topic: troubleshooting
---

## Research questions

* What local code path controls the legacy OIDC callback and its exception handling?
* Which one or two local hypotheses best explain an `InvalidOperationException`
  after the OIDC-specific failure notification is bypassed?
* What is the cheapest secret-safe discriminating check?
* What is the smallest direct correction or diagnostic edit, including exact tests?

## Scope and safety

Review is limited to controlling legacy source, package versions, focused tests,
and directly relevant tracking evidence. No callback parameters, authorization
codes, state, nonce, tokens, cookies, credentials, raw exception messages, or
stack traces are collected or recorded.

## Findings

### Controlling path

1. `poc/legacy-net452/web.config` explicitly registers
   `Microsoft.Owin.Host.SystemWeb.OwinHttpModule` under `system.webServer/modules`.
2. `Microsoft.Owin.Host.SystemWeb` 4.2.3 also has an assembly-level
   `PreApplicationStartMethod`. Its `PreApplicationStart.Initialize` calls
   `HttpApplication.RegisterModule(typeof(OwinHttpModule))` whenever automatic
   OWIN startup is enabled. This application relies on automatic startup through
   the `OwinStartup` assembly attribute and does not disable it.
3. `poc/legacy-net452/Startup.cs` installs the outer exception boundary first,
   then cookie middleware, then OIDC middleware. On a successful callback,
   Katana OIDC completes `AuthenticateCoreAsync`, queues
   `Authentication.SignIn`, and redirects from `InvokeReplyPathAsync`.
4. The upstream cookie handler applies the queued sign-in later in
   `ApplyResponseGrantAsync`. It protects the ticket, appends the cookie, and
   sets no-cache response headers during teardown.
5. `AuthenticationMiddleware.Invoke` awaits `handler.TeardownAsync` after the
   inner middleware returns. The SystemWeb host awaits the original OWIN task
   in `IntegratedPipelineContext.DoFinalWork` and surfaces a fault from
   `EndFinalWork`, which is attached to ASP.NET `EndRequest`.

The OIDC `AuthenticationFailed` notification surrounds only failures within
`AuthenticateCoreAsync`. It cannot observe a duplicate-host finalization fault
or a later cookie `ApplyResponseGrantAsync` fault. The deployed outer-boundary
classification is therefore consistent with both hypotheses below.

### Package and runtime evidence

* `poc/legacy-net452/LegacyNet452.csproj` targets `net452` and references
  `Microsoft.Owin.Host.SystemWeb`, `Microsoft.Owin.Security.Cookies`, and
  `Microsoft.Owin.Security.OpenIdConnect` 4.2.3.
* `poc/legacy-net452/obj/project.assets.json` resolves the complete Katana set
  to 4.2.3, IdentityModel and JWT dependencies to 5.3.0, and Newtonsoft.Json to
  13.0.1.
* `poc/legacy-net452/web.config` redirects Newtonsoft.Json through assembly
  version 13.0.0.0.
* Tracking evidence states that the deployed `net452` application runs on the
  App Service .NET Framework 4.8 runtime and that the failure surfaced during
  `OwinHttpModule` `EndRequest` after bypassing the OIDC notification.

### Ranked hypotheses

1. Duplicate `OwinHttpModule` registration causes two SystemWeb OWIN pipeline
   attachments or otherwise violates Katana's final-work state invariants.
   This is the strongest local hypothesis because the package auto-registers
   the module while `web.config` registers it again, and Katana 4.2.3's
   `IntegratedPipelineContext` contains bare `InvalidOperationException`
   invariant failures in `DefaultAppInvoked`, `ExitPointInvoked`, and
   `PushLastObjects`. The observed category and `EndRequest` boundary match.
   It is falsified if removing the explicit module block leaves exactly the
   same callback failure after `/signin` still proves automatic startup.
2. Successful OIDC authentication queues a cookie grant, then cookie ticket
   protection or `SystemWebCookieManager.AppendResponseCookie` fails inside
   `CookieAuthenticationHandler.ApplyResponseGrantAsync`. This also bypasses
   the OIDC notification and reaches the outer boundary during unwind. It is
   less likely than hypothesis 1 because challenge-time machine-key protection
   already works, the ticket is deliberately minimal, and the generic outer
   response indicates headers had not started. It is confirmed by a fixed-field
   cookie exception event with location `ApplyResponseGrant`.

### Cheapest discriminating check

Remove only the explicit `system.webServer/modules` block from
`poc/legacy-net452/web.config`, publish, deploy to the same protected validation
path, verify `/signin` still produces the expected challenge, and perform one
user-controlled callback retry. Katana's pre-application startup remains the
module registration mechanism. No additional runtime data collection is needed.

If the callback still fails, add a `CookieAuthenticationProvider.OnException`
callback in `poc/legacy-net452/Authentication/CookieOptionsFactory.cs`. Emit
only fixed component and phase values, the bounded exception type chain, and
HResult through `poc/legacy-net452/Telemetry/AuthenticationEventLogger.cs`.
Keep `CookieExceptionContext.Rethrow` unchanged. Do not emit exception messages,
request data, response headers, stack traces, tickets, identities, or cookies.
The presence or absence of `phase=ApplyResponseGrant` separates hypothesis 2
from a SystemWeb host finalization fault.

### Smallest recommended edit and tests

The first edit should be the direct one-file correction: delete the redundant
`modules` block from `poc/legacy-net452/web.config`.

Extend `scripts/test-classic-net-bff-deployment-entra-static.ps1`, which already
parses this configuration, to assert that no explicit
`Microsoft.Owin.Host.SystemWeb.OwinHttpModule` entry exists. Existing hosted
challenge checks then prove that automatic registration still activates OWIN.
No application unit-test change is required for this correction.

If hypothesis 1 is falsified, the fallback diagnostic touches exactly these
files:

* `poc/legacy-net452/Authentication/CookieOptionsFactory.cs`
* `poc/legacy-net452/Telemetry/AuthenticationEventLogger.cs`
* `poc/legacy-net452/Tests/CookieOptionsFactoryTests.cs`
* `poc/legacy-net452/Tests/AuthenticationEventLoggerTests.cs`

The cookie test should prove that the provider callback is registered and does
not suppress rethrow. The logger test should prove that phase, bounded type
chain, and HResult are retained while all exception text is absent.

Current tests do not cover the failing boundary:

* `StartupIntegrationTests.UnhandledDownstreamFailureReturnsGenericResponse`
  uses `Microsoft.Owin.Testing.TestServer`, not SystemWeb `EndRequest`.
* `StateProtectionTests.KatanaPipelineInitializesProtectedStateFormat` only
  builds the pipeline.
* `CookieOptionsFactoryTests.CreateUsesSecureSystemWebCookieCustody` checks
  option shape, not cookie response-grant execution.

## References

* `poc/legacy-net452/Startup.cs`
* `poc/legacy-net452/Authentication/OidcOptionsFactory.cs`
* `poc/legacy-net452/Authentication/CookieOptionsFactory.cs`
* `poc/legacy-net452/Telemetry/AuthenticationEventLogger.cs`
* `poc/legacy-net452/LegacyNet452.csproj`
* `poc/legacy-net452/web.config`
* `poc/legacy-net452/Tests/StartupIntegrationTests.cs`
* `poc/legacy-net452/Tests/StateProtectionTests.cs`
* `poc/legacy-net452/Tests/CookieOptionsFactoryTests.cs`
* `poc/legacy-net452/Tests/AuthenticationEventLoggerTests.cs`
* `scripts/test-classic-net-bff-deployment-entra-static.ps1`
* `.copilot-tracking/changes/2026-08-07/classic-net-bff-appservice-deployment-changes.md`
* `.copilot-tracking/plans/logs/2026-08-07/classic-net-bff-appservice-deployment-log.md`
* [Katana 4.2.3 PreApplicationStart](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Host.SystemWeb/PreApplicationStart.cs)
* [Katana 4.2.3 IntegratedPipelineContext](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Host.SystemWeb/IntegratedPipeline/IntegratedPipelineContext.cs)
* [Katana 4.2.3 AuthenticationHandler](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security/Infrastructure/AuthenticationHandler.cs)
* [Katana 4.2.3 OpenIdConnectAuthenticationHandler](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security.OpenIdConnect/OpenidConnectAuthenticationHandler.cs)
* [Katana 4.2.3 CookieAuthenticationHandler](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security.Cookies/CookieAuthenticationHandler.cs)
* [Katana 4.2.3 CookieExceptionContext](https://github.com/aspnet/AspNetKatana/blob/v4.2.3/src/Microsoft.Owin.Security.Cookies/Provider/CookieExceptionContext.cs)
* [HttpApplication.RegisterModule](https://learn.microsoft.com/en-us/dotnet/api/system.web.httpapplication.registermodule?view=netframework-4.8.1)

## Clarifying questions

None. Both hypotheses have local, secret-safe falsification paths.
