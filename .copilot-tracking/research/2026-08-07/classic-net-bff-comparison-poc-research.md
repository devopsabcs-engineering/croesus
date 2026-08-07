---
title: Classic .NET BFF Comparison PoC Research
description: Evidence and implementation constraints for the Croesus classic .NET and .NET 10 BFF comparison proof of concept
ms.date: 2026-08-07
ms.topic: reference
---

## Research scope

* Identify current Katana packages and APIs that remain compatible with .NET Framework 4.5.2 for classic ASP.NET cookie and OpenID Connect hosting
* Determine whether compatible Katana OpenID Connect middleware supports PKCE and identify safe extension points if it does not
* Establish current MSAL.NET and Microsoft.Identity.Web target-framework floors for .NET Framework 4.5.2, .NET Framework 4.8, and .NET 10
* Define a buildable local project and deterministic test shape using the installed .NET 10 SDK and .NET Framework 4.5.2 reference assemblies
* Confirm multi-tenant confidential web application requirements
* Review the proposed implementation details for security pitfalls

## Evidence collected

The investigation used package metadata, package contents, upstream source,
Microsoft documentation, and a disposable local build. No product code was
modified.

* Katana 4.2.3 packages contain direct `net45` assemblies for the System.Web
  host, cookie middleware, and OpenID Connect middleware. NuGet therefore
  resolves them for `net452`.
* `Microsoft.Owin.Security.OpenIdConnect` 4.2.3 documents and implements native
  PKCE. It creates a 32-byte random verifier, sends an S256 challenge, stores the
  verifier in protected authentication properties, adds it to the token request,
  and removes it after use.
* Native Katana PKCE runs only when `ResponseType` is exactly `code`.
  Katana's defaults are `ResponseType=code id_token`, `ResponseMode=form_post`,
  `UsePkce=true`, `RedeemCode=false`, and `SaveTokens=false`.
* Katana invokes `AuthorizationCodeReceived` before its optional built-in code
  redemption. Supplying `TokenEndpointResponse` or calling
  `HandleCodeRedemption(...)` prevents the handler from redeeming the code a
  second time.
* The resolved OpenID Connect 4.2.3 graph uses IdentityModel 5.3.0, Katana
  4.2.3, `Newtonsoft.Json` 13.0.1, and `Owin` 1.0.0.
* MSAL.NET 4.87.0 has a direct .NET Framework floor of `net462`.
  Microsoft.Identity.Web 4.14.2 also has a direct .NET Framework floor of
  `net462`. Neither package can be referenced by a `net452` project.
* The workstation has .NET SDK 8.0.423 and 10.0.302, .NET Framework 4.8.1,
  and .NET Framework 4.5.2 reference assemblies. Full-framework MSBuild is not
  on `PATH`.
* A disposable SDK-style `net452` test project restored, compiled, and ran two
  tests through `dotnet test` with SDK 10.0.302. The probe compiled Katana
  4.2.3 code-only PKCE, built-in redemption, secure cookie options, and
  `SystemWebCookieManager`.
* The successful test stack was `Microsoft.NET.Test.Sdk` 17.11.1, xUnit 2.9.3,
  and `xunit.runner.visualstudio` 2.4.1. Adapter versions 2.4.5 and 2.8.2 both
  failed restore because their direct .NET Framework floor is `net462`.

## Findings

### Framework and package matrix

* `net452`: Katana 4.2.3 is compatible; MSAL.NET 4.87.0 and
  Microsoft.Identity.Web 4.14.2 are incompatible. This is an unsupported
  protocol PoC target.
* `net48`: All three package lines are compatible by target framework. Support
  follows the underlying Windows lifecycle.
* `net10.0`: Use MSAL.NET and Microsoft.Identity.Web, not Katana. This is the
  strategic, supported implementation target.

Package compatibility does not make .NET Framework 4.5.2 supported. Its support
ended on April 26, 2022. Retargeting to .NET Framework 4.8 requires a rebuild and
regression test; installing 4.8 on a machine does not change the application's
compile target.

Microsoft.Identity.Web's `net462` asset does not make it the preferred
System.Web authentication stack. Katana remains the appropriate middleware for
the classic System.Web comparison. Microsoft.Identity.Web belongs in the
ASP.NET Core .NET 10 application.

### Katana code flow and PKCE

The legacy project does not need custom PKCE generation, transaction storage, or
token-request construction. Katana 4.2.3 performs these operations when all of
the following settings are explicit:

```csharp
new OpenIdConnectAuthenticationOptions
{
    ResponseType = OpenIdConnectResponseType.Code,
    ResponseMode = OpenIdConnectResponseMode.Query,
    UsePkce = true,
    RedeemCode = true,
    SaveTokens = false
};
```

`ResponseType=code` is mandatory for native PKCE. `RedeemCode=true` is mandatory
for Katana-owned token endpoint redemption. Relying on defaults silently produces
hybrid flow without PKCE and leaves redemption to application code.

`ResponseMode=query` is the least surprising option for this code-only PoC. It
also reduces dependence on cross-site POST cookie behavior on the old System.Web
runtime. The callback remains a server endpoint and must be registered as a
`web.redirectUri`.

Do not mutate `ResponseType` in `RedirectToIdentityProvider`. Katana decides
whether to create the verifier before that notification. Changing the response
type there can separate the outbound protocol message from the PKCE transaction
that Katana prepared.

### Legacy project and test shape

Use a small SDK-style `Microsoft.NET.Sdk` project targeting `net452`. Build its
assembly with `dotnet build`, but host it in IIS or IIS Express through
`Microsoft.Owin.Host.SystemWeb`; `dotnet run` is not a System.Web host.

Keep host wiring thin and move tenant policy, log redaction, and session response
projection into ordinary classes. This makes deterministic tests possible
without an Entra tenant or IIS process. A physical web root can contain
`web.config`, static content, and the compiled assembly under `bin` for local
IIS Express execution.

Use these direct package references for the legacy host:

```xml
<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net452"
                  Version="1.0.3" PrivateAssets="all" />
<PackageReference Include="Microsoft.Owin.Host.SystemWeb" Version="4.2.3" />
<PackageReference Include="Microsoft.Owin.Security.Cookies" Version="4.2.3" />
<PackageReference Include="Microsoft.Owin.Security.OpenIdConnect" Version="4.2.3" />
```

Do not add MSAL.NET or Microsoft.Identity.Web to the `net452` project. The
OpenID Connect package resolves these relevant transitive dependencies:

* `Microsoft.IdentityModel.JsonWebTokens` 5.3.0
* `Microsoft.IdentityModel.Logging` 5.3.0
* `Microsoft.IdentityModel.Protocols` 5.3.0
* `Microsoft.IdentityModel.Protocols.OpenIdConnect` 5.3.0
* `Microsoft.IdentityModel.Tokens` 5.3.0
* `System.IdentityModel.Tokens.Jwt` 5.3.0
* `Microsoft.Owin` and `Microsoft.Owin.Security` 4.2.3
* `Newtonsoft.Json` 13.0.1 and `Owin` 1.0.0

Use this tested package set for a `net452` xUnit test project:

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
<PackageReference Include="xunit" Version="2.9.3" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.4.1"
                  PrivateAssets="all" />
```

The runner pin is intentional. Newer adapter releases tested during this
research require `net462`. The narrow local commands are:

```powershell
dotnet restore path\to\Legacy.Tests.csproj
dotnet build path\to\Legacy.csproj --configuration Release
dotnet test path\to\Legacy.Tests.csproj --configuration Release
```

### System.Web authentication boundary

Register cookie middleware before OpenID Connect middleware and make the cookie
scheme the default sign-in type. Use `SystemWebCookieManager` so Katana and
System.Web do not independently rewrite the same cookies.

The browser receives only an opaque, HTTP-only session cookie and projected
session data. Keep `SaveTokens=false`; otherwise Katana stores access and refresh
tokens in authentication properties, which can enlarge the serialized session
ticket and put bearer credentials into the cookie custody path.

Katana protects the PKCE verifier together with authentication properties in
state and removes it after callback processing. Under System.Web, the protection
boundary depends on ASP.NET machine-key data protection. Every farm instance
must share stable protection keys, and callback traffic must be able to recover
the challenge state produced by another instance.

The legacy core comparison should omit downstream Graph access. Katana can
redeem the sign-in code, but `SaveTokens=false` deliberately discards token
custody after identity establishment. A Graph demonstration would require a
separate server-side token cache and lifecycle design on a runtime that cannot
use current MSAL.NET. That extra mechanism obscures the sign-in comparison.

### Modern project shape

Use an ASP.NET Core `net10.0` web application with
Microsoft.Identity.Web 4.14.2. Let the ASP.NET Core OpenID Connect handler use
authorization code flow and PKCE. Add server-side token acquisition only when
the optional Graph `User.Read` demonstration is included.

Keep `SaveTokens` at the Microsoft.Identity.Web-managed default and use its
server-side token cache rather than serializing OAuth tokens into the browser
cookie. The browser-facing session endpoint should return an allowlisted
projection such as display name, tenant ID, and authentication state.

### Multitenant registration and validation

One confidential Entra application registration can hold both HTTPS localhost
callbacks under `web.redirectUris`. Default the manifest `signInAudience` to
`AzureADMyOrg`. Set it to `AzureADMultipleOrgs` only through an explicit
multitenant provisioning option.

Use the tenant-specific authority in single-tenant mode. Use the
`organizations` authority in multitenant mode so personal Microsoft accounts are
not admitted. Multi-tenant mode also requires all of the following controls:

* A non-empty allowlist of tenant IDs at startup
* Signature, audience, lifetime, nonce, state, and correlation validation before
  an application session is created
* Issuer validation that binds the issuer tenant segment to the validated `tid`
  claim
* Allowlist rejection after cryptographic validation and before cookie sign-in
* Tenant-administrator consent in each customer tenant when requested permissions
  require it

Do not implement multitenancy by setting `ValidateIssuer=false` alone. Katana's
older IdentityModel graph needs a replacement issuer validator for a templated
`organizations` issuer. That validator must accept only the expected Entra v2
issuer shape, require a GUID `tid`, bind issuer and `tid`, and then apply the
same allowlist used by the session gate.

## Recommended implementation

Build the comparison as two independent BFF hosts sharing only tests or neutral
policy helpers where target-framework compatibility permits it:

1. Create the legacy System.Web host as an SDK-style `net452` assembly with
   Katana 4.2.3, the `net452` reference-assemblies package, IIS Express operator
   configuration, and a separate `net452` test project.
2. Configure Katana with `ResponseType=code`, `ResponseMode=query`,
   `UsePkce=true`, `RedeemCode=true`, and `SaveTokens=false`.
3. Delete the proposed custom PKCE generator, protected verifier store, and
   custom token request from the implementation plan. Retain deterministic tests
   for effective option values, tenant policy, redaction, cookie settings, and
   browser response shape.
4. Create the modern ASP.NET Core host at `net10.0` with
   Microsoft.Identity.Web 4.14.2 and its server-side token acquisition and cache
   only if Graph is demonstrated.
5. Use one Entra confidential web registration with both exact localhost
   callbacks, one short-lived development credential, and single-tenant behavior
   by default.
6. Gate `AzureADMultipleOrgs` behind an explicit option and require a non-empty
   tenant allowlist plus issuer-to-tenant binding in both applications.
7. Capture only protocol metadata in evidence: event names, timestamps,
   correlation identifiers generated for logs, result categories, and redacted
   tenant IDs. Never capture token, code, verifier, secret, cookie, or full claim
   values.

This keeps the comparison focused on the behavior under investigation: both
server applications own the authorization code exchange and browser session,
while only the modern implementation uses a currently supported application
runtime and identity integration stack.

## Cautions

* .NET Framework 4.5.2 is out of support. Internet exposure, production use, and
  presentation as a migration destination are inappropriate.
* Katana 4.2.3 is compatible with `net452`, but its transitive IdentityModel
  5.3.0 stack is old. Keep token validation inside middleware and minimize custom
  protocol logic.
* The implementation-details file currently proposes custom PKCE and manual code
  redemption. That design should be revised before implementation because native
  Katana code-only PKCE is available and was compile-tested.
* `UsePkce=true` alone is insufficient. Katana's default hybrid response type
  suppresses its PKCE branch.
* `RedeemCode=false` is the default. Failing to set it leaves the code unredeemed
  unless application code handles `AuthorizationCodeReceived`.
* Calling `HandleCodeRedemption()` with no token response during code-only flow
  prevents Katana redemption but cannot produce the ID token needed to establish
  an identity. Do not use it as a shortcut.
* Never disable issuer validation without installing a strict multitenant issuer
  validator. An allowlist check does not replace signature, audience, or issuer
  validation.
* Force TLS 1.2 for the legacy process before metadata and token back-channel
  calls. Treat changes to `ServicePointManager.SecurityProtocol` as process-wide.
* Require HTTPS callbacks, `CookieSecure=Always`, HTTP-only cookies, bounded
  session lifetime, and an explicit SameSite strategy. Test the exact supported
  browser set because .NET 4.5.2 predates current SameSite behavior.
* Synchronize machine keys in a web farm and protect them as credentials. Key
  drift breaks state, nonce, verifier, and session recovery.
* Read client credentials from environment variables or protected local
  configuration. Do not place them in source, command output, provisioning state,
  browser storage, cookies, or telemetry.
* Use exact redirect URI matching and constrain post-login return URLs to local
  paths to avoid open redirects.
* Apply antiforgery protection to every state-changing BFF endpoint. An
  HTTP-only cookie prevents token theft by browser code but does not prevent CSRF.
* Do not log raw authentication exceptions in user responses. Katana's
  `AuthenticationFailed` notification must map failures to a generic response
  and redacted diagnostics.
* A successful SDK-style build does not prove IIS hosting. Validate the deployed
  physical path, `web.config`, bitness, HTTPS certificate, callback, and OWIN
  startup discovery separately.

## Unresolved questions

* Does the comparison require a live Graph `User.Read` call, or is authenticated
  session projection sufficient? Session projection is the cleaner default.
* Is exact reproduction of a historical hybrid or manual-redemption flow a test
  requirement? If not, native code-only PKCE should remain mandatory.
* Which tenant IDs should populate the explicit multitenant allowlist, and who
  owns consent in each tenant?
* Is IIS Express already installed with a trusted certificate for port 44352?
  The command-line build probe did not test hosting.
* Will the PoC ever run on multiple IIS instances? If so, the machine-key and
  server-side cache design must be decided before live sign-in testing.
* Which evidence fields are required for the final comparison packet? The list
  should be fixed before instrumentation so sensitive protocol values cannot
  enter ad hoc traces.

## References

* [Microsoft.Owin.Security.OpenIdConnect 4.2.3](https://www.nuget.org/packages/Microsoft.Owin.Security.OpenIdConnect/4.2.3)
* [Microsoft.Owin.Security.Cookies 4.2.3](https://www.nuget.org/packages/Microsoft.Owin.Security.Cookies/4.2.3)
* [Microsoft.Owin.Host.SystemWeb 4.2.3](https://www.nuget.org/packages/Microsoft.Owin.Host.SystemWeb/4.2.3)
* [Katana OpenID Connect options source](https://github.com/aspnet/AspNetKatana/blob/main/src/Microsoft.Owin.Security.OpenIdConnect/OpenIdConnectAuthenticationOptions.cs)
* [Katana OpenID Connect handler source](https://github.com/aspnet/AspNetKatana/blob/main/src/Microsoft.Owin.Security.OpenIdConnect/OpenidConnectAuthenticationHandler.cs)
* [Katana code-flow sample](https://github.com/aspnet/AspNetKatana/blob/main/samples/Katana.Sandbox.WebServer/Startup.cs)
* [MSAL.NET](https://www.nuget.org/packages/Microsoft.Identity.Client/4.87.0)
* [Microsoft.Identity.Web](https://www.nuget.org/packages/Microsoft.Identity.Web/4.14.2)
* [Microsoft .NET Framework support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-framework)
* [OWIN SameSite cookie guidance](https://learn.microsoft.com/aspnet/samesite/owin-samesite)
* [Single-tenant and multitenant application guidance](https://learn.microsoft.com/entra/identity-platform/single-and-multi-tenant-apps)
* [Microsoft Entra application manifest reference](https://learn.microsoft.com/entra/identity-platform/reference-app-manifest)
* [Redirect URI restrictions](https://learn.microsoft.com/entra/identity-platform/reply-url)
* [OAuth 2.0 authorization code flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-auth-code-flow)
* [RFC 7636: Proof Key for Code Exchange](https://www.rfc-editor.org/rfc/rfc7636)
* [Microsoft.NETFramework.ReferenceAssemblies.net452 1.0.3](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net452/1.0.3)
* [Microsoft.NET.Test.Sdk 17.11.1](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.11.1)
* [xunit.runner.visualstudio 2.4.1](https://www.nuget.org/packages/xunit.runner.visualstudio/2.4.1)
