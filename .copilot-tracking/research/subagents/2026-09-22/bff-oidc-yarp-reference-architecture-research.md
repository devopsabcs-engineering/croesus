<!-- markdownlint-disable-file -->
# BFF + OIDC + YARP Reference Architecture Research

**Status**: Complete (one gap — see [Research Gaps](#research-gaps))
**Date**: 2026-09-22
**Workspace**: `c:\src\GitHub\devopsabcs-engineering\croesus`
**Audience**: Microsoft team guiding ISV customer Croesus toward Microsoft-recommended BFF practices

---

## Research Questions

1. Deep summary of the Blazor Web App + OIDC + YARP + Aspire article (all pivots).
2. General Blazor Web App OIDC / Entra / `Microsoft.Identity.Web` BFF guidance.
3. BFF security rationale (Microsoft + IETF `draft-ietf-oauth-browser-based-apps`).
4. Azure API Management BFF pattern vs in-process ASP.NET Core BFF.
5. YARP specifics — transforms, config schema, package.
6. .NET Aspire angle — required or optional?
7. Concrete reference implementation sketch for a "third app" in this repo.
8. Pitfalls — cookie size, chunking, forwarded headers, nonce/correlation failures.

---

## Executive Summary

The anchor document the customer was given — [Secure an ASP.NET Core Blazor Web App with OpenID Connect (OIDC), `pivots=with-yarp-and-aspire`](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-oidc?view=aspnetcore-10.0&pivots=with-yarp-and-aspire) — is Microsoft's canonical, code-complete demonstration of the Backend-for-Frontend pattern on ASP.NET Core. Three findings dominate:

1. **The BFF is an OIDC confidential client; the browser only ever holds an HttpOnly auth cookie.** Access and refresh tokens are persisted server-side in `AuthenticationProperties` via `SaveTokens = true`, never surfaced to JavaScript/WASM. The Microsoft article's own words: *"The project uses YARP to proxy requests to a weather forecast endpoint in the backend web API project (`MinimalApiJwt`) with the `access_token` stored in the authentication cookie."*
2. **YARP's role is a ~6-line request transform, not a product decision.** The entire token-attachment mechanism is `app.MapForwarder(...)` + `AddRequestTransform` + `HttpContext.GetTokenAsync("access_token")`. The same article's *non-YARP* pivots achieve the identical security outcome with a `DelegatingHandler` (`TokenHandler`) on a named `HttpClient`. **YARP is optional.**
3. **Aspire contributes only local orchestration and service discovery.** Its footprint is `builder.AddServiceDefaults()`, `app.MapDefaultEndpoints()`, and an `AppHost.cs` with `WithReference`. Microsoft's own Entra pivot explicitly states the YARP destination prefix (`https://weatherapi`) is an Aspire service-discovery name that *"There's no need to change ... when deploying the Blazor Web App to production."* **Aspire is optional.**

The independent standards backing is strong: [`draft-ietf-oauth-browser-based-apps-25` §6.1.4.3](https://www.ietf.org/archive/id/draft-ietf-oauth-browser-based-apps-25.html) states the BFF architecture *"is strongly recommended for business applications, sensitive applications, and applications that handle personal data."*

---

## 1. The Anchor Article — Blazor Web App + OIDC (all pivots)

**Primary source**: [Secure an ASP.NET Core Blazor Web App with OpenID Connect (OIDC)](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-oidc?view=aspnetcore-10.0&pivots=with-yarp-and-aspire) (uid `blazor/security/blazor-web-app-oidc`, `ms.date` 2025-12-18, monikers `aspnetcore-8.0` → `aspnetcore-11.0`).

### 1.1 Pivot map — the article has three BFF variants

The pivot group is `blazor-web-app-oidc-specification`. The zone identifiers actually present in the page source are:

| Pivot / zone | Sample folder in `dotnet/blazor-samples` | Render mode | Token attachment |
| --- | --- | --- | --- |
| `with-yarp-and-aspire` | `BlazorWebAppOidcBffAutoYarpAspire` | Interactive **Auto**, global | YARP `MapForwarder` + request transform |
| `without-yarp-and-aspire` | `BlazorWebAppOidcBffAuto` | Interactive **Auto**, global | `DelegatingHandler` (`TokenHandler`) |
| `without-yarp-and-aspire-server` | `BlazorWebAppOidcBffServer` | Interactive **Server**, global | `DelegatingHandler` (`TokenHandler`) |

> **Note on the requested URLs.** The task asked for `pivots=without-bff-pattern` and a `with-bff-pattern` variant. Fetching `?pivots=without-bff-pattern` returns the **same page content** — that pivot value does not exist in this article's zone pivot group, so Learn falls back to rendering all zones. The actual zone names are the three above. All three are BFF-pattern samples; this article has no "without BFF" pivot.
>
> Corroborating quote from the troubleshooting section: *"For one of the Backend-for-Frontend (BFF) pattern samples, start the solution from the `Aspire/Aspire.AppHost` project. For one of the non-BFF pattern samples, start the solution from the server project."*

### 1.2 Exact architecture (YARP + Aspire pivot)

Direct quote of the project inventory:

> The sample app consists of the following projects:
>
> - Aspire:
>   - `Aspire.AppHost`: Used to manage the high-level orchestration concerns of the app.
>   - `Aspire.ServiceDefaults`: Contains default Aspire app configurations that can be extended and customized as needed.
> - `MinimalApiJwt`: Backend web API, containing an example Minimal API endpoint for weather data.
> - `BlazorWebAppOidc`: Server-side project of the Blazor Web App. The project uses [YARP](https://dotnet.github.io/yarp/) to proxy requests to a weather forecast endpoint in the backend web API project (`MinimalApiJwt`) with the `access_token` stored in the authentication cookie.
> - `BlazorWebAppOidc.Client`: Client-side project of the Blazor Web App.

And the pattern statement:

> The [Backend for Frontend (BFF) pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/backends-for-frontends) is adopted using Aspire for service discovery and YARP for proxying requests to a weather forecast endpoint on the backend app.
>
> The backend web API (`MinimalApiJwt`) uses JWT-bearer authentication to validate JWT tokens saved by the Blazor Web App in the sign-in cookie.

**Request flow:**

```text
Browser ──(1) HttpOnly auth cookie──▶ BlazorWebAppOidc (BFF, confidential client)
                                        │
                                        ├─(2) GetTokenAsync("access_token") from cookie
                                        │
                                        └─(3) Authorization: Bearer <AT>──▶ MinimalApiJwt (JWT-bearer)
```

- **(1)** Browser → BFF carries only `Cookie:` — no `Authorization` header, no token in JS-reachable storage.
- **(2)** The BFF reads the access token out of the authentication ticket (server-side, decrypted with the Data Protection key ring).
- **(3)** BFF → downstream API is a normal OAuth 2.0 bearer call. `MinimalApiJwt` validates `Authority`/`Audience` and has no knowledge of cookies.

### 1.3 Where tokens live, and why the browser never sees one

The mechanism is [`RemoteAuthenticationOptions.SaveTokens`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.remoteauthenticationoptions.savetokens):

> **SaveTokens**: Defines whether access and refresh tokens should be stored in the `AuthenticationProperties` after a successful authorization. This property is set to `true` so the refresh token gets stored for non-interactive token refresh.

```csharp
oidcOptions.SaveTokens = true;
```

From the sample's inline comment (`10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc/Program.cs`):

```text
// (2) SaveTokens is set to true, which saves the access and refresh tokens
// in the cookie, so the app can authenticate requests for weather data and
// use the refresh token to obtain a new access token on access token
// expiration.
```

The `AuthenticationProperties` are serialized into the cookie payload, which is **encrypted and signed by ASP.NET Core Data Protection** and marked `HttpOnly` by the cookie handler. The browser holds an opaque blob it cannot read, and JavaScript cannot read it at all. This is exactly the IETF BFF definition (see §3).

The sample's own client project confirms the browser side is token-free:

```csharp
// 10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc.Client/Program.cs
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddHttpClient<IWeatherForecaster, ClientWeatherForecaster>(httpClient =>
{
    httpClient.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

await builder.Build().RunAsync();
```

No MSAL, no token acquisition, no `Authorization` header. The client's `HttpClient` base address is the **BFF's own origin** (`builder.HostEnvironment.BaseAddress`), so the cookie rides along automatically.

Article statement on the auth-state flow:

> The server project calls `AddAuthenticationStateSerialization` to add a server-side authentication state provider that uses `PersistentComponentState` to flow the authentication state to the client. The client calls `AddAuthenticationStateDeserialization` to deserialize and use the authentication state passed by the server. The authentication state is fixed for the lifetime of the WebAssembly application.

### 1.4 The full `AddAuthentication` / `AddOpenIdConnect` / `AddCookie` configuration

Verbatim from `10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc/Program.cs` (comments preserved, since they are Microsoft's own explanation of each knob):

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;
using BlazorWebAppOidc;
using BlazorWebAppOidc.Client.Weather;
using BlazorWebAppOidc.Components;

const string MS_OIDC_SCHEME = "MicrosoftOidc";

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddAuthentication(MS_OIDC_SCHEME)
    .AddOpenIdConnect(MS_OIDC_SCHEME, oidcOptions =>
    {
        // For the following OIDC settings, any line that's commented out
        // represents a DEFAULT setting. If you adopt the default, you can
        // remove the line if you wish.

        // ........................................................................
        // Pushed Authorization Requests (PAR) support. By default, the setting is
        // to use PAR if the identity provider's discovery document (usually found
        // at '.well-known/openid-configuration') advertises support for PAR. If
        // you wish to require PAR support for the app, you can assign
        // 'PushedAuthorizationBehavior.Require' to 'PushedAuthorizationBehavior'.
        //
        // Note that PAR isn't supported by Microsoft Entra, and there are no plans
        // for Entra to ever support it in the future.

        //oidcOptions.PushedAuthorizationBehavior = PushedAuthorizationBehavior.UseIfAvailable;
        // ........................................................................

        // ........................................................................
        // The OIDC handler must use a sign-in scheme capable of persisting
        // user credentials across requests.

        oidcOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        // ........................................................................

        // ........................................................................
        // The "openid" and "profile" scopes are required for the OIDC handler
        // and included by default. You should enable these scopes here if scopes
        // are provided by "Authentication:Schemes:MicrosoftOidc:Scope"
        // configuration because configuration may overwrite the scopes collection.

        //oidcOptions.Scope.Add(OpenIdConnectScope.OpenIdProfile);
        // ........................................................................

        // ........................................................................
        // The following paths must match the redirect and post logout redirect
        // paths configured when registering the application with the OIDC provider.
        // The default values are "/signin-oidc" and "/signout-callback-oidc".

        //oidcOptions.CallbackPath = new PathString("/signin-oidc");
        //oidcOptions.SignedOutCallbackPath = new PathString("/signout-callback-oidc");
        // ........................................................................

        // ........................................................................
        // The RemoteSignOutPath is the "Front-channel logout URL" for remote single
        // sign-out. The default value is "/signout-oidc".

        //oidcOptions.RemoteSignOutPath = new PathString("/signout-oidc");
        // ........................................................................

        // ........................................................................
        // The "Weather.Get" scope for accessing the external web API for weather
        // data. The following example is based on using Microsoft Entra ID in
        // an ME-ID tenant domain (the {APP ID URI} placeholder is found in
        // the Entra or Azure portal where the web API is exposed). For any other
        // identity provider, use the appropriate scope.

        oidcOptions.Scope.Add("{APP ID URI}/Weather.Get");
        // ........................................................................

        // ........................................................................
        // The following example Authority is configured for Microsoft Entra ID
        // and a single-tenant application registration. Set the {TENANT ID}
        // placeholder to the Tenant ID. The "common" Authority
        // https://login.microsoftonline.com/common/v2.0/ should be used
        // for multi-tenant apps. You can also use the "common" Authority for
        // single-tenant apps, but it requires a custom IssuerValidator as shown
        // in the comments below.

        oidcOptions.Authority = "https://login.microsoftonline.com/{TENANT ID}/v2.0/";
        // ........................................................................

        // ........................................................................
        // Set the Client ID for the app. Set the {CLIENT ID} placeholder to
        // the Client ID.

        oidcOptions.ClientId = "{CLIENT ID}";
        // ........................................................................

        // ........................................................................
        // Setting ResponseType to "code" configures the OIDC handler to use
        // authorization code flow. Implicit grants and hybrid flows are unnecessary
        // in this mode. In a Microsoft Entra ID app registration, you don't need to
        // select either box for the authorization endpoint to return access tokens
        // or ID tokens. The OIDC handler automatically requests the appropriate
        // tokens using the code returned from the authorization endpoint.

        oidcOptions.ResponseType = OpenIdConnectResponseType.Code;
        // ........................................................................

        // ........................................................................
        // Set MapInboundClaims to "false" to obtain the original claim types from
        // the token. Many OIDC servers use "name" and "role"/"roles" rather than
        // the SOAP/WS-Fed defaults in ClaimTypes. Adjust these values if your
        // identity provider uses different claim types.

        oidcOptions.MapInboundClaims = false;
        oidcOptions.TokenValidationParameters.NameClaimType = "name";
        oidcOptions.TokenValidationParameters.RoleClaimType = "roles";
        // ........................................................................

        // ........................................................................
        // Many OIDC providers work with the default issuer validator, but the
        // configuration must account for the issuer parameterized with "{TENANT ID}"
        // returned by the "common" endpoint's /.well-known/openid-configuration
        // For more information, see
        // https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/issues/1731

        //var microsoftIssuerValidator = AadIssuerValidator.GetAadIssuerValidator(oidcOptions.Authority);
        //oidcOptions.TokenValidationParameters.IssuerValidator = microsoftIssuerValidator.Validate;
        // ........................................................................

        // ........................................................................
        // OIDC connect options set later via ConfigureCookieOidc
        //
        // (1) The "offline_access" scope is required for the refresh token.
        //
        // (2) SaveTokens is set to true, which saves the access and refresh tokens
        // in the cookie, so the app can authenticate requests for weather data and
        // use the refresh token to obtain a new access token on access token
        // expiration.
        // ........................................................................
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

// ConfigureCookieOidc attaches a cookie OnValidatePrincipal callback to get
// a new access token when the current one expires, and reissue a cookie with the
// new access token saved inside. If the refresh fails, the user will be signed
// out. OIDC connect options are set for saving tokens and the offline access
// scope.
builder.Services.ConfigureCookieOidc(CookieAuthenticationDefaults.AuthenticationScheme, MS_OIDC_SCHEME);

builder.Services.AddAuthorization();

builder.Services.AddCascadingAuthenticationState();

// Remove or set 'SerializeAllClaims' to 'false' if you only want to
// serialize name and role claims for CSR.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization(options => options.SerializeAllClaims = true);

builder.Services.AddHttpForwarderWithServiceDiscovery();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IWeatherForecaster, ServerWeatherForecaster>(httpClient =>
{
    httpClient.BaseAddress = new("https://weatherapi");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapDefaultEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorWebAppOidc.Client._Imports).Assembly);

app.MapForwarder("/weather-forecast", "https://weatherapi", transformBuilder =>
{
    transformBuilder.AddRequestTransform(async transformContext =>
    {
        var accessToken = await transformContext.HttpContext.GetTokenAsync("access_token");
        transformContext.ProxyRequest.Headers.Authorization = new("Bearer", accessToken);
    });
}).RequireAuthorization();

app.MapGroup("/authentication").MapLoginAndLogout();

app.Run();
```

#### Option-by-option reference (article's own definitions)

| Option | Value in sample | Article's explanation |
| --- | --- | --- |
| `PushedAuthorizationBehavior` | `UseIfAvailable` (default, commented out) | *"Controls Pushed Authorization Requests (PAR) support. By default, the setting is to use PAR if the identity provider's discovery document ... advertises support for PAR. ... **PAR isn't supported by Microsoft Entra, and there are no plans for Entra to ever support it in the future.**"* |
| `SignInScheme` | `CookieAuthenticationDefaults.AuthenticationScheme` | *"Sets the authentication scheme corresponding to the middleware responsible of persisting user's identity after a successful authentication. The OIDC handler needs to use a sign-in scheme that's capable of persisting user credentials across requests."* |
| `Scope` (`openid`, `profile`) | default | *"required for the OIDC handler to work, but these may need to be re-added if scopes are included in the `Authentication:Schemes:MicrosoftOidc:Scope` configuration."* |
| `Scope` (`offline_access`) | set in `ConfigureCookieOidc` | *"The `offline_access` scope is required for the refresh token."* |
| `Scope` (`{APP ID URI}/Weather.Get`) | explicit | Downstream API scope. ME-ID form: `api://{CLIENT ID}/Weather.Get` |
| `SaveTokens` | `true` (set in `ConfigureCookieOidc`) | *"Defines whether access and refresh tokens should be stored in the `AuthenticationProperties` after a successful authorization."* |
| `ResponseType` | `OpenIdConnectResponseType.Code` | *"Configures the OIDC handler to only perform authorization code flow. Implicit grants and hybrid flows are unnecessary in this mode."* |
| `MapInboundClaims` | `false` | *"**MapInboundClaims must be set to `false` for most OIDC providers**, which prevents renaming claims."* |
| `TokenValidationParameters.NameClaimType` | `"name"` | *"Many OIDC servers use `name` and `role` rather than the SOAP/WS-Fed defaults in `ClaimTypes`."* |
| `TokenValidationParameters.RoleClaimType` | `"roles"` | Entra app roles arrive in `roles`. For non-Entra IdPs the article says *"For many OIDC identity providers, the role claim type is `role`."* |
| `TokenValidationParameters.IssuerValidator` | commented out | Required **only** when using the `common` authority. `AadIssuerValidator.GetAadIssuerValidator(oidcOptions.Authority)` from [`Microsoft.IdentityModel.Validators`](https://www.nuget.org/packages/Microsoft.IdentityModel.Validators). |
| `CallbackPath` | `/signin-oidc` (default) | *"The request path within the app's base path where the user-agent is returned."* |
| `SignedOutCallbackPath` | `/signout-callback-oidc` (default) | Post-logout return path; must be registered as a Redirect URI in Entra. |
| `RemoteSignOutPath` | `/signout-oidc` (default) | *"Requests received on this path cause the handler to invoke sign-out using the sign-out scheme."* Maps to Entra's **Front-channel logout URL**. |

> **`GetClaimsFromUserInfoEndpoint`** — the task asked about this. It is **not set** in any of the BFF samples. The article mentions it only as a known-issue caveat under *Token refresh*: *"The sample implementation doesn't include code for requesting claims from the UserInfo endpoint on token refresh. For more information, see [`BlazorWebAppOidc AddOpenIdConnect with GetClaimsFromUserInfoEndpoint = true doesn't propogate [sic] role claims to client` (dotnet/aspnetcore #58826)](https://github.com/dotnet/aspnetcore/issues/58826#issuecomment-2492738142)."* Treat enabling it as a known sharp edge.

#### `CookieOidcServiceCollectionExtensions.cs` — where `SaveTokens` and `offline_access` are actually wired

```csharp
// 10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc/CookieOidcServiceCollectionExtensions.cs
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using BlazorWebAppOidc;

namespace Microsoft.Extensions.DependencyInjection;

internal static partial class CookieOidcServiceCollectionExtensions
{
    public static IServiceCollection ConfigureCookieOidc(this IServiceCollection services, string cookieScheme, string oidcScheme)
    {
        services.AddSingleton<CookieOidcRefresher>();
        services.AddOptions<CookieAuthenticationOptions>(cookieScheme).Configure<CookieOidcRefresher>((cookieOptions, refresher) =>
        {
            cookieOptions.Events.OnValidatePrincipal = context => refresher.ValidateOrRefreshCookieAsync(context, oidcScheme);
        });
        services.AddOptions<OpenIdConnectOptions>(oidcScheme).Configure(oidcOptions =>
        {
            // Request a refresh_token.
            oidcOptions.Scope.Add(OpenIdConnectScope.OfflineAccess);
            // Store the refresh_token.
            oidcOptions.SaveTokens = true;
        });
        return services;
    }
}
```

### 1.5 The YARP reverse-proxy configuration and token-attaching transform

The article's description:

> YARP (Yet Another Reverse Proxy) is a library used to create a reverse proxy server. `MapForwarder` in the `Program` file of the server project adds direct forwarding of HTTP requests that match the specified pattern to a specific destination using default configuration for the outgoing request, customized transforms, and default HTTP client.

The complete mechanism — **this is the whole thing**:

```csharp
app.MapForwarder("/weather-forecast", "https://weatherapi", transformBuilder =>
{
    transformBuilder.AddRequestTransform(async transformContext =>
    {
        var accessToken = await transformContext.HttpContext.GetTokenAsync("access_token");
        transformContext.ProxyRequest.Headers.Authorization = new("Bearer", accessToken);
    });
}).RequireAuthorization();
```

Supporting registration:

```csharp
builder.Services.AddHttpForwarderWithServiceDiscovery();
```

`AddHttpForwarderWithServiceDiscovery()` is the **Aspire-flavoured** registration (it layers service discovery onto YARP's `IHttpForwarder`). Without Aspire, register plain `AddHttpForwarder()` and pass a real URL instead of `https://weatherapi`.

`.RequireAuthorization()` is essential: it guarantees the cookie has already been validated (and refreshed, via `OnValidatePrincipal`) before the transform runs, so `GetTokenAsync("access_token")` returns a live token.

#### Entra / `Microsoft.Identity.Web` variant of the same transform

The Entra article uses `ITokenAcquisition` instead of reading the raw cookie token:

```csharp
// 10.0/BlazorWebAppEntraBffYarpAspire/BlazorWebAppEntra/Program.cs
app.MapForwarder("/weather-forecast", "https://weatherapi", transformBuilder =>
{
    transformBuilder.AddRequestTransform(async transformContext =>
    {
        var tokenAcquisition = transformContext.HttpContext.RequestServices.GetRequiredService<ITokenAcquisition>();
        List<string> scopes = [ "https://guardrexorg.onmicrosoft.com/edb4f62e-f83a-496e-9629-ed87ad546c62/Weather.Get" ];
        var accessToken = await tokenAcquisition.GetAccessTokenForUserAsync(scopes);
        transformContext.ProxyRequest.Headers.Authorization = new("Bearer", accessToken);
    });
}).RequireAuthorization();

app.MapGroup("/authentication").MapLoginAndLogout();

app.Run();
```

### 1.6 Sign-out handling

There is no `SignOutSessionStateManager` in the BFF samples (that type belongs to the older standalone-WASM MSAL model). Instead, a minimal-API endpoint group. Full source of `LoginLogoutEndpointRouteBuilderExtensions.cs`:

```csharp
// 10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc/LoginLogoutEndpointRouteBuilderExtensions.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.AspNetCore.Routing;

internal static class LoginLogoutEndpointRouteBuilderExtensions
{
    internal static IEndpointConventionBuilder MapLoginAndLogout(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("");

        group.MapGet("/login", (string? returnUrl) => TypedResults.Challenge(GetAuthProperties(returnUrl)))
            .AllowAnonymous();

        // Sign out of the Cookie and OIDC handlers. If you do not sign out with the OIDC handler,
        // the user will automatically be signed back in the next time they visit a page that requires authentication
        // without being able to choose another account.
        group.MapPost("/logout", ([FromForm] string? returnUrl) => TypedResults.SignOut(GetAuthProperties(returnUrl),
            [CookieAuthenticationDefaults.AuthenticationScheme, "MicrosoftOidc"]));

        return group;
    }

    private static AuthenticationProperties GetAuthProperties(string? returnUrl)
    {
        // TODO: Use HttpContext.Request.PathBase instead.
        const string pathBase = "/";

        // Prevent open redirects.
        if (string.IsNullOrEmpty(returnUrl))
        {
            returnUrl = pathBase;
        }
        else if (!Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
        {
            returnUrl = new Uri(returnUrl, UriKind.Absolute).PathAndQuery;
        }
        else if (returnUrl[0] != '/')
        {
            returnUrl = $"{pathBase}{returnUrl}";
        }

        return new AuthenticationProperties { RedirectUri = returnUrl };
    }
}
```

Four things worth flagging to Croesus:

1. **Logout is `POST`, login is `GET`.** Logout is state-changing, so it must not be a `GET` (prevents drive-by logout CSRF).
2. **Both schemes are signed out.** *"If you do not sign out with the OIDC handler, the user will automatically be signed back in the next time they visit a page that requires authentication without being able to choose another account."*
3. **Open-redirect defence is explicit** in `GetAuthProperties` — relative-only, absolute URLs reduced to `PathAndQuery`.
4. **`post_logout_redirect_uri`** is driven by `SignedOutCallbackPath` (default `/signout-callback-oidc`), which the article says must be registered in Entra: *"If you don't add the signed-out callback path URI to the app's registration in Entra, Entra refuses to redirect the user back to the app and merely asks them to close their browser window."*

**Front-channel logout** is `RemoteSignOutPath` (default `/signout-oidc`): *"Requests received on this path cause the handler to invoke sign-out using the sign-out scheme."* In Entra this is the **Front-channel logout URL** field.

### 1.7 Antiforgery / CSRF protection

This is non-negotiable for any cookie-based BFF and the sample wires it two ways.

**Middleware** (Program.cs, before endpoint mapping):

```csharp
app.UseAntiforgery();
```

**Token in the logout form** — `Layout/LogInOrOut.razor`:

```razor
<div class="nav-item px-3">
    <AuthorizeView>
        <Authorized>
            <form action="authentication/logout" method="post">
                <AntiforgeryToken />
                <button type="submit" class="nav-link">
                    <span class="bi bi-arrow-bar-left-nav-menu" aria-hidden="true">
                    </span> Logout
                </button>
            </form>
        </Authorized>
        <NotAuthorized>
            <a class="nav-link" href="authentication/login">
                <span class="bi bi-person-badge-nav-menu" aria-hidden="true"></span>
                Login
            </a>
        </NotAuthorized>
    </AuthorizeView>
</div>
```

Cross-reference to the IETF requirement — [`draft-ietf-oauth-browser-based-apps-25` §6.1.3.3](https://www.ietf.org/archive/id/draft-ietf-oauth-browser-based-apps-25.html): *"The BFF **MUST** implement a proper CSRF defense."* The draft lists three acceptable mechanisms: `SameSite=Strict` cookies (§6.1.3.3.1), CORS + a mandatory custom request header (§6.1.3.3.2), and anti-forgery / double-submit cookies (§6.1.3.3.3). ASP.NET Core's `UseAntiforgery()` + `<AntiforgeryToken />` is the third.

### 1.8 Refresh-token handling — `CookieOidcRefresher`

The article:

> The custom cookie refresher (`CookieOidcRefresher.cs`) implementation updates the user's claims automatically when they expire. The current implementation expects to receive an ID token from the token endpoint in exchange for the refresh token. The claims in this ID token are then used to overwrite the user's claims.

And on nonce:

> A nonce isn't required or used when a refresh token is exchanged for a new access token. In the sample app, the `CookieOidcRefresher` (`CookieOidcRefresher.cs`) deliberately sets `OpenIdConnectProtocolValidator.RequireNonce` to `false`.

Full source:

```csharp
// 10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc/CookieOidcRefresher.cs
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BlazorWebAppOidc;

// https://github.com/dotnet/aspnetcore/issues/8175
internal sealed class CookieOidcRefresher(IOptionsMonitor<OpenIdConnectOptions> oidcOptionsMonitor)
{
    private readonly OpenIdConnectProtocolValidator oidcTokenValidator = new()
    {
        // We no longer have the original nonce cookie which is deleted at the end of the authorization code flow having served its purpose.
        // Even if we had the nonce, it's likely expired. It's not intended for refresh requests. Otherwise, we'd use oidcOptions.ProtocolValidator.
        RequireNonce = false,
    };

    public async Task ValidateOrRefreshCookieAsync(CookieValidatePrincipalContext validateContext, string oidcScheme)
    {
        var accessTokenExpirationText = validateContext.Properties.GetTokenValue("expires_at");
        if (!DateTimeOffset.TryParse(accessTokenExpirationText, out var accessTokenExpiration))
        {
            return;
        }

        var oidcOptions = oidcOptionsMonitor.Get(oidcScheme);
        var now = oidcOptions.TimeProvider!.GetUtcNow();
        if (now + TimeSpan.FromMinutes(5) < accessTokenExpiration)
        {
            return;
        }

        var oidcConfiguration = await oidcOptions.ConfigurationManager!.GetConfigurationAsync(validateContext.HttpContext.RequestAborted);
        var tokenEndpoint = oidcConfiguration.TokenEndpoint ?? throw new InvalidOperationException("Cannot refresh cookie. TokenEndpoint missing!");

        using var refreshResponse = await oidcOptions.Backchannel.PostAsync(tokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string?>()
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = oidcOptions.ClientId,
                ["client_secret"] = oidcOptions.ClientSecret,
                ["scope"] = string.Join(" ", oidcOptions.Scope),
                ["refresh_token"] = validateContext.Properties.GetTokenValue("refresh_token"),
            }));

        if (!refreshResponse.IsSuccessStatusCode)
        {
            validateContext.RejectPrincipal();
            return;
        }

        var refreshJson = await refreshResponse.Content.ReadAsStringAsync();
        var message = new OpenIdConnectMessage(refreshJson);

        var validationParameters = oidcOptions.TokenValidationParameters.Clone();
        if (oidcOptions.ConfigurationManager is BaseConfigurationManager baseConfigurationManager)
        {
            validationParameters.ConfigurationManager = baseConfigurationManager;
        }
        else
        {
            validationParameters.ValidIssuer = oidcConfiguration.Issuer;
            validationParameters.IssuerSigningKeys = oidcConfiguration.SigningKeys;
        }

        var validationResult = await oidcOptions.TokenHandler.ValidateTokenAsync(message.IdToken, validationParameters);

        if (!validationResult.IsValid)
        {
            validateContext.RejectPrincipal();
            return;
        }

        var validatedIdToken = JwtSecurityTokenConverter.Convert(validationResult.SecurityToken as JsonWebToken);
        validatedIdToken.Payload["nonce"] = null;
        oidcTokenValidator.ValidateTokenResponse(new()
        {
            ProtocolMessage = message,
            ClientId = oidcOptions.ClientId,
            ValidatedIdToken = validatedIdToken,
        });

        validateContext.ShouldRenew = true;
        validateContext.ReplacePrincipal(new ClaimsPrincipal(validationResult.ClaimsIdentity));

        var expiresIn = int.Parse(message.ExpiresIn, NumberStyles.Integer, CultureInfo.InvariantCulture);
        var expiresAt = now + TimeSpan.FromSeconds(expiresIn);
        validateContext.Properties.StoreTokens([
            new() { Name = "access_token", Value = message.AccessToken },
            new() { Name = "id_token", Value = message.IdToken },
            new() { Name = "refresh_token", Value = message.RefreshToken },
            new() { Name = "token_type", Value = message.TokenType },
            new() { Name = "expires_at", Value = expiresAt.ToString("o", CultureInfo.InvariantCulture) },
        ]);
    }
}
```

Key behaviours to demo:

- Refresh fires on a **5-minute-before-expiry** window inside `OnValidatePrincipal` — i.e. transparently on an ordinary request, no browser round trip.
- `RejectPrincipal()` on refresh failure ⇒ user is signed out. This aligns with IETF §6.1.2.2: *"when the BFF learns that a refresh token for an active session is no longer valid, it also makes sense to invalidate the session."*
- `ShouldRenew = true` re-issues the cookie with the fresh token set.

The article also points to a supported alternative:

> An alternative solution can be found in the open source [`Duende.AccessTokenManagement.OpenIdConnect` package](https://docs.duendesoftware.com/accesstokenmanagement/web-apps/).

```csharp
// Add services for token management
builder.Services.AddOpenIdConnectAccessTokenManagement();

// Register a typed HTTP client with token management support
builder.Services.AddHttpClient<InvoiceClient>(client =>
    {
        client.BaseAddress = new Uri("https://api.example.com/invoices/");
    })
    .AddUserAccessTokenHandler();
```

With `Microsoft.Identity.Web` (the Entra article), refresh is framework-managed: *"Automatic non-interactive token refresh is managed by the framework."* — no `CookieOidcRefresher` needed.

### 1.9 The non-YARP pivots — the `TokenHandler` alternative

Verbatim from the `without-yarp-and-aspire` zones:

```csharp
public class TokenHandler(IHttpContextAccessor httpContextAccessor) :
    DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is null)
        {
            throw new Exception("HttpContext not available");
        }

        var accessToken = await httpContextAccessor.HttpContext
            .GetTokenAsync("access_token");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
```

```csharp
builder.Services.AddScoped<TokenHandler>();

builder.Services.AddHttpClient("ExternalApi",
      client => client.BaseAddress = new Uri(builder.Configuration["ExternalApiUri"] ??
          throw new Exception("Missing base address!")))
      .AddHttpMessageHandler<TokenHandler>();
```

```json
"ExternalApiUri": "https://localhost:7277"
```

Article caveat: *"The token handler only executes during static server-side rendering (static SSR), so using `HttpContext` is safe in this scenario."*

**This is the key "YARP is optional" evidence.** Same `GetTokenAsync("access_token")`, same `Authorization: Bearer`, no YARP dependency.

### 1.10 Backend API (`MinimalApiJwt`) configuration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

builder.Services.AddAuthentication()
    .AddJwtBearer("Bearer", jwtOptions =>
    {
        jwtOptions.Authority = "{AUTHORITY}";
        jwtOptions.Audience = "{AUDIENCE}";
    });

builder.Services.AddAuthorization();
```

```csharp
app.MapGet("/weather-forecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
}).RequireAuthorization();
```

Authority/Audience formats:

| Tenant type | Authority | Audience |
| --- | --- | --- |
| ME-ID (V1 STS) | `https://sts.windows.net/{TENANT ID}/` | `api://{CLIENT ID}` |
| ME-ID (V2 STS) | `https://login.microsoftonline.com/{TENANT ID}/v2.0` | `api://{CLIENT ID}` |
| Entra External ID | `https://{DIRECTORY NAME}.ciamlogin.com/{TENANT ID}/v2.0` | `{CLIENT ID}` |
| AAD B2C | `https://login.microsoftonline.com/{TENANT ID}/v2.0` | `https://{DIRECTORY NAME}.onmicrosoft.com/{CLIENT ID}` |

### 1.11 Entra app-registration guidance (both articles agree)

> We recommend using separate registrations for apps and web APIs, even when the apps and web APIs are in the same solution.

> Register the web API (`MinimalApiJwt`) **first** so that you can then grant access to the web API when registering the app. ... expose the web API in **App registrations** > **Expose an API** with a scope name of `Weather.Get`.

> Next, register the app ... with a **Web** platform configuration and a **Redirect URI** of `https://localhost/signin-oidc` (a port isn't required).

> In the Entra or Azure portal's **Implicit grant and hybrid flows** app registration configuration, **don't select either checkbox** for the authorization endpoint to return **Access tokens** or **ID tokens**. The OpenID Connect handler automatically requests the appropriate tokens using the code returned from the authorization endpoint.

> Create a client secret in the app's registration ... (**Manage** > **Certificates & secrets** > **New client secret**).

The Entra article adds a third Redirect URI + front-channel logout requirement:

> Next, register the app (`BlazorWebAppEntra`) with a **Web** platform configuration with two entries under **Redirect URI**: `https://localhost/signin-oidc` and `https://localhost/signout-callback-oidc` ... Set the **Front-channel logout URL** to `https://localhost/signout-callback-oidc`.

Secret handling — the article's explicit warning:

> **Don't store app secrets, connection strings, credentials, passwords, personal identification numbers (PINs), private C#/.NET code, or private keys/tokens in client-side code, which is *always insecure*.** In test/staging and production environments, server-side Blazor code and web APIs should use secure authentication flows that avoid maintaining credentials within project code or configuration files. Outside of local development testing, we recommend avoiding the use of environment variables to store sensitive data ...

```dotnetcli
dotnet user-secrets init
dotnet user-secrets set "Authentication:Schemes:MicrosoftOidc:ClientSecret" "{SECRET}"
```

### 1.12 App-settings-driven configuration (production shape)

```json
"Authentication": {
  "Schemes": {
    "MicrosoftOidc": {
      "Authority": "https://login.microsoftonline.com/{TENANT ID (BLAZOR APP)}/v2.0",
      "ClientId": "{CLIENT ID (BLAZOR APP)}",
      "CallbackPath": "/signin-oidc",
      "SignedOutCallbackPath": "/signout-callback-oidc",
      "RemoteSignOutPath": "/signout-oidc",
      "SignedOutRedirectUri": "/",
      "Scope": [
        "openid",
        "profile",
        "offline_access",
        "{APP ID URI (WEB API)}/Weather.Get"
      ]
    }
  }
},
```

```json
"Authentication": {
  "Schemes": {
    "Bearer": {
      "Authority": "https://sts.windows.net/{TENANT ID (WEB API)}/",
      "ValidAudiences": [ "{APP ID URI (WEB API)}" ]
    }
  }
},
```

> The configuration is automatically picked up by the authentication builder.

---

## 2. General Blazor / Entra / `Microsoft.Identity.Web` BFF Guidance

**Source**: [Secure an ASP.NET Core Blazor Web App with Microsoft Entra ID](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/blazor-web-app-with-entra) (uid `blazor/security/blazor-web-app-entra`, `ms.date` 2026-07-06, monikers `aspnetcore-9.0`+). Sample folder: `BlazorWebAppEntraBffYarpAspire`.

This is the article to point Croesus at if they want Microsoft-supported libraries rather than hand-rolled OIDC. The OIDC article says so directly:

> For Microsoft Entra ID, you can use `AddMicrosoftIdentityWebApp` from [Microsoft Identity Web](https://learn.microsoft.com/en-us/entra/msal/dotnet/microsoft-identity-web/) ([`Microsoft.Identity.Web` NuGet package](https://www.nuget.org/packages/Microsoft.Identity.Web)), which adds both the OIDC and Cookie authentication handlers with the appropriate defaults. The sample app and the guidance in this article don't use Microsoft Identity Web. The guidance demonstrates how to configure the OIDC handler *manually* for any OIDC provider.

### 2.1 The `AddDownstreamApi` / `ITokenAcquisition` BFF shape

```csharp
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(msIdentityOptions =>
    {
        msIdentityOptions.CallbackPath = "/signin-oidc";
        msIdentityOptions.ClientId = "{CLIENT ID (BLAZOR APP)}";
        msIdentityOptions.Domain = "{DIRECTORY NAME}.onmicrosoft.com";
        msIdentityOptions.Instance = "https://login.microsoftonline.com/";
        msIdentityOptions.ResponseType = "code";
        msIdentityOptions.TenantId = "{TENANT ID}";
    })
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddDownstreamApi("DownstreamApi", configOptions =>
    {
        configOptions.BaseUrl = "{BASE ADDRESS}";
        configOptions.Scopes = ["{APP ID URI}/Weather.Get"];
    })
    .AddDistributedTokenCaches();
```

App-settings form:

```json
{
  "AzureAd": {
    "CallbackPath": "/signin-oidc",
    "ClientId": "{CLIENT ID (BLAZOR APP)}",
    "Domain": "{DIRECTORY NAME}.onmicrosoft.com",
    "Instance": "https://login.microsoftonline.com/",
    "ResponseType": "code",
    "TenantId": "{TENANT ID}"
  },
  "DownstreamApi": {
    "BaseUrl": "{BASE ADDRESS}",
    "Scopes": ["{APP ID URI}/Weather.Get"]
  }
}
```

```csharp
.AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
.EnableTokenAcquisitionToCallDownstreamApi()
.AddDownstreamApi("DownstreamApi", builder.Configuration.GetSection("DownstreamApi"))
.AddDistributedTokenCaches();
```

Server-rendered components use `IDownstreamApi`:

> Microsoft Identity Web packages provide API to create a named downstream web service for making web API calls. `IDownstreamApi` is injected into the `ServerWeatherForecaster`, which is used to call `CallApiForUserAsync` to obtain weather data from an external web API (`MinimalApiJwt` project).

### 2.2 Production token cache — a hard requirement

> **Warning**: Always replace the in-memory distributed token caches with a real token cache provider when deploying the app to a production environment. If you fail to adopt a production distributed token cache provider, the app may suffer significantly degraded performance.

> Production web apps and web APIs should use a production distributed token cache (for example: [Redis](https://redis.io/), [Microsoft SQL Server](https://www.microsoft.com/sql-server), [Microsoft Azure Cosmos DB](https://azure.microsoft.com/products/cosmos-db)).

```csharp
builder.Services.AddDistributedMemoryCache();

builder.Services.Configure<MsalDistributedTokenCacheAdapterOptions>(
    options =>
    {
      // The following lines that are commented out reflect
      // default values. We recommend overriding the default
      // value of Encrypt to encrypt tokens at rest.

      //options.DisableL1Cache = false;
      //options.L1CacheOptions.SizeLimit = 500 * 1024 * 1024;
      options.Encrypt = true;
      //options.SlidingExpiration = TimeSpan.FromHours(1);
    });
```

### 2.3 Shared Data Protection key ring — required for any multi-instance BFF

From the OIDC article:

> Server-side Blazor Web Apps hosted in a web farm or cluster of machines must adopt [*session affinity*](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr#use-session-affinity-sticky-sessions-for-server-side-web-farm-hosting) to maintain Blazor circuits for users of the app.
>
> We also recommend using a shared [Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction) key ring in production, even when the app uses the Interactive WebAssembly render mode exclusively for client-side rendering (no Blazor circuits).

Entra article's concrete implementation:

```csharp
TokenCredential? credential;

if (builder.Environment.IsProduction())
{
    credential = new ManagedIdentityCredential("{MANAGED IDENTITY CLIENT ID}");
}
else
{
    // Local development and testing only
    DefaultAzureCredentialOptions options = new()
    {
        VisualStudioTenantId = "{TENANT ID}",
        SharedTokenCacheTenantId = "{TENANT ID}"
    };

    credential = new DefaultAzureCredential(options);
}

builder.Services.AddDataProtection()
    .SetApplicationName("BlazorWebAppEntra")
    .PersistKeysToAzureBlobStorage(new Uri("{BLOB URI}"), credential)
    .ProtectKeysWithAzureKeyVault(new Uri("{KEY IDENTIFIER}"), credential);
```

> You can pass any app name to `SetApplicationName`. Just confirm that **all app deployments use the same value**.

**This is the single most common production BFF failure**: without a shared key ring, instance A cannot decrypt the auth cookie instance B issued, and users bounce back to login at random.

### 2.4 STS token version (V1 vs V2) — Entra-specific gotcha

> There are two types of token URIs, named Version 1 (V1) and Version 2 (V2). In Azure's security token services (STS), the V1 endpoint uses the `sts.windows.net` domain as the issuer, while the V2 endpoint uses the `login.microsoftonline.com` domain as the issuer.

> The STS version must be changed in the web API's (`MinimalApiJwt`) app registration in the Azure portal. Set the value of `requestedAccessTokenVersion` to `2` in the web API app registration's manifest. **Entra issues access tokens in the version requested by the resource (audience) app registration**, so this setting on the Blazor Web App's client registration has no effect on the tokens that `MinimalApiJwt` receives and validates.

```csharp
jwtOptions.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,
    // Ensure the issuer ends with /v2.0 if using the V2 endpoint and that
    // {TENANT ID} is the tenant GUID (matching the token's tid claim), not a domain
    ValidIssuer = "https://login.microsoftonline.com/{TENANT ID}/v2.0",
    ValidateAudience = true,
    ValidAudiences = new[] { "{WEB API CLIENT ID 1}", "{WEB API CLIENT ID 2}" },
    ValidateLifetime = true
};
```

### 2.5 Built-in evidence components (directly reusable for a demo)

The article ships a ready-made claims-inspection component — perfect for the "show what happened" requirement in Q7:

```razor
@page "/user-claims"
@using System.Security.Claims
@using Microsoft.AspNetCore.Authorization
@attribute [Authorize]

<PageTitle>User Claims</PageTitle>

<h1>User Claims</h1>

@if (claims.Any())
{
    <ul>
        @foreach (var claim in claims)
        {
            <li><b>@claim.Type:</b> @claim.Value</li>
        }
    </ul>
}

@code {
    private IEnumerable<Claim> claims = Enumerable.Empty<Claim>();

    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (AuthState == null)
        {
            return;
        }

        var authState = await AuthState;
        claims = authState.User.Claims;
    }
}
```

And a downstream-API token-inspection pattern (note the DEBUG guard and explicit production caution — *"In production, avoid logging the token or its contents."*):

```csharp
app.MapGet("/weather-forecast", (HttpContext context, ILogger<Program> logger) =>
{
#if DEBUG
    var authHeader = context.Request.Headers.Authorization.FirstOrDefault(v =>
        v != null &&
        v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase));

    if (authHeader is not null)
    {
        var token = authHeader["Bearer ".Length..].Trim();
        logger.LogDebug("Token: {Token}", token);

        try
        {
            var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
            var jwtToken = handler.ReadJsonWebToken(token);
            logger.LogDebug("Audience: {Audience}", string.Join(", ", jwtToken.Audiences));
            logger.LogDebug("Issuer: {Issuer}", jwtToken.Issuer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decode token.");
        }
    }
#endif
    // ...
}).RequireAuthorization();
```

---

## 3. The BFF Security Rationale

### 3.1 The authoritative standards statement

**Source**: [OAuth 2.0 for Browser-Based Applications, `draft-ietf-oauth-browser-based-apps-25`](https://www.ietf.org/archive/id/draft-ietf-oauth-browser-based-apps-25.html) (Web Authorization Protocol WG; intended status **Best Current Practice**; published 4 July 2025; authors Parecki (Okta), De Ryck (Pragmatic Web Security), Waite (Ping Identity)).

The draft ranks three architectures **"in decreasing order of security"**:

> - A browser-based application that relies on a backend component for handling OAuth responsibilities and forwards all requests through the backend component (**Backend-For-Frontend or BFF**)
> - A browser-based application that relies on a backend component for handling OAuth responsibilities, but calls resource servers directly using the access token (**Token-Mediating Backend**)
> - A browser-based application acting as the client, handling all OAuth responsibilities in the browser (**Browser-based OAuth Client**)

#### The exact BFF recommendation (§6.1.4.3 Summary)

> However, because of the nature of the BFF architecture pattern, it offers strong security guarantees. Using a BFF also ensures that the application's attack surface does not increase by using OAuth. The only viable attack pattern is hijacking the client application in the user's browser, a problem inherent to web applications.
>
> **This architecture is strongly recommended for business applications, sensitive applications, and applications that handle personal data.**

And the converse, for browser-held tokens (§6.3.4.3):

> To summarize, the architecture of a browser-based OAuth client application is straightforward, but results in a significant increase in the attack surface of the application. The attacker is not only able to hijack the client, but also to extract a full-featured set of tokens from the browser-based application.
>
> **This architecture is not recommended for business applications, sensitive applications, and applications that handle personal data.**

#### The BFF's three responsibilities (§6.1, verbatim)

> 1. The BFF interacts with the authorization server as a confidential OAuth client (as defined in Section 2.1 of [RFC6749])
> 2. The BFF manages OAuth access and refresh tokens in the context of a cookie-based session, avoiding the direct exposure of any tokens to the browser-based application
> 3. The BFF forwards all requests to a resource server, augmenting them with the correct access token before forwarding them to the resource server

### 3.2 Why SPAs should not hold access/refresh tokens

The draft enumerates four attack scenarios available to an attacker who achieves JS execution (§5.1):

| # | Scenario | Effect |
| --- | --- | --- |
| 5.1.1 | **Single-Execution Token Theft** | Read tokens from `localStorage`/`IndexedDB`, exfiltrate. |
| 5.1.2 | **Persistent Token Theft** | Install a loop that steals the *latest* tokens continuously. *"Refresh token rotation is not sufficient to prevent abuse of a refresh token."* |
| 5.1.3 | **Acquisition and Extraction of New Tokens** | Hidden iframe runs a *new* silent Authorization Code flow in the user's session. *"There are no practical security mechanisms for frontend applications that counter this attack scenario."* DPoP is also ineffective: *"the attacker can use their own key pair."* |
| 5.1.4 | **Proxying Requests via the User's Browser** | Attacker just calls the API from inside the origin. *"This attack pattern is well-known and also occurs with traditional applications using `HttpOnly` session cookies. It is commonly accepted that this scenario cannot be stopped or prevented by application-level security measures."* |

Storage does not save you (§8.5):

> Note that the main difference between these patterns is the exposure of the data, but that **none of these options can fully mitigate token exfiltration** when the attacker can execute malicious code in the application's execution environment.

Nor, at rest (§8.6):

> In all cases, as of this writing, there is no guarantee that browser storage is encrypted at rest. ... More and more malware is specifically created to crawl user's machines looking for browser profiles to obtain high-value tokens and session cookies, resulting in account takeover attacks.

### 3.3 What the BFF actually protects against (§6.1.4.2, verbatim)

> The other attack scenarios, listed below, are effectively mitigated by the BFF application architecture:
>
> - Single-Execution Token Theft (Section 5.1.1)
> - Persistent Token Theft (Section 5.1.2)
> - Acquisition and Extraction of New Tokens (Section 5.1.3)
>
> The BFF counters the first two attack scenarios by **not exposing any tokens to the browser-based application. Even when the attacker gains full control over the application, there are simply no tokens to be stolen.**
>
> The third scenario, where the attacker obtains a fresh access token (and optionally refresh token) by running a silent flow, is mitigated by **making the BFF a confidential client. Even when the attacker manages to obtain an authorization code, they are prevented from exchanging this code due to the lack of client credentials.** Additionally, the use of PKCE prevents other attacks against the authorization code.
>
> Since refresh and access tokens are managed by the BFF and not exposed to the browser, the following two consequences of potential attacks become irrelevant:
>
> - Exploiting Stolen Refresh Tokens
> - Exploiting Stolen Access Tokens

On bearer-token "unbound"-ness (§5.2.2):

> Note that the possession of the access token allows its unrestricted use by the attacker. The attacker can send arbitrary requests to resource servers, using any HTTP method, destination URL, header values, or body.

What the BFF does **not** fix (be honest with the customer — §6.1.4.1):

> - Proxying Requests via the User's Browser (Section 5.1.4)
>
> Note that this attack scenario results in the following consequences:
>
> - Client Hijacking (Section 5.2.3)
>
> Note that client hijacking is an attack scenario that is inherent to the nature of browser-based applications. As a result, nothing will be able to prevent such attacks apart from stopping the execution of malicious code in the first place.

But even there the BFF constrains the blast radius:

> Note that the use of `HttpOnly` cookies prevents the attacker from directly accessing the session state, which prevents the escalation from client hijacking to session hijacking.

### 3.4 BFF normative requirements (all MUST/SHOULD from §6.1.3)

| § | Requirement |
| --- | --- |
| 6.1.3.1 | The BFF **MUST** act as a confidential client by establishing credentials with the authorization server. The BFF **MUST** use the OAuth 2.0 Authorization Code grant. |
| 6.1.3.2 | The BFF **MUST** enable the `Secure` flag for its cookies. |
| 6.1.3.2 | The BFF **MUST** enable the `HttpOnly` flag for its cookies. |
| 6.1.3.2 | The BFF **SHOULD** enable the `SameSite=Strict` flag for its cookies. |
| 6.1.3.2 | The BFF **SHOULD** set its cookie path to `/`. |
| 6.1.3.2 | The BFF **SHOULD NOT** set the `Domain` attribute for cookies. |
| 6.1.3.2 | The BFF **SHOULD** start the name of its cookies with the `__Host` prefix. |
| 6.1.3.2 | When using client-side sessions that contain access tokens, the BFF **SHOULD** encrypt its cookie contents. |
| 6.1.3.3 | The BFF **MUST** implement a proper CSRF defense. |
| 6.1.3.6 | The BFF **MUST** enforce strict outbound request controls by validating destination hosts before forwarding requests ... maintaining an explicit allowlist of approved resource servers. |
| 6.1.3.6 | When implementing a dynamically configurable proxy, the BFF **MUST** ensure that it only allows requests to explicitly permitted hosts and paths. |

> The "SHOULD" items are only not "MUSTs" so that existing architectures can be compliant.

The `__Host` rationale:

> The `__Host` prefix prevents the cookie from being shared with subdomains, thereby countering subdomain-based session hijacking or session fixation attacks.

> **Note (gap vs. the Microsoft sample)**: the Blazor OIDC sample relies on `AddCookie(...)` defaults and does **not** explicitly set `__Host-` naming, `SameSite=Strict`, or `SecurePolicy.Always`. The existing Croesus `poc/modern-net10` app *does* (see §7.1) — it is already closer to the IETF baseline than the Learn sample on this axis. Good talking point.

Also worth flagging (§6.1.3.5 — Operational Considerations):

> As the BFF is forwarding all requests to the resource server on behalf of the frontend, care should be taken to ensure the resource server is aware of this component and uses appropriate policies for rate limiting and other anti-abuse measures. For example, if the BFF is deployed as a single-instance service, and the resource server is rate limiting requests based on IP address, it might start blocking requests as many users' browsers will appear to be coming from the single IP address of the BFF.

And (§6.1.3.4 — Privacy):

> The BFF component is able to observe all requests and responses between the application and a resource server, which can have a considerable privacy impact.

### 3.5 Microsoft's own product-side statement

From the Azure Architecture Center [Backends for Frontends pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/backends-for-frontends), Well-Architected Security row:

> The service separation introduced in this pattern allows security and authorization in the service layer to be customized for each client's specific needs. This approach can reduce the API's surface area and limit lateral movement between backends that might expose different capabilities.

---

## 4. Azure API Management BFF Pattern vs In-Process ASP.NET Core BFF

> **Fetch gap**: The dedicated page [Implementing the BFF/curated API pattern using Azure API Management](https://learn.microsoft.com/en-us/azure/architecture/example-scenario/api-management/backend-for-frontend) failed to return extractable content on three attempts (both with and without `?tabs=bff`). The analysis below is grounded in the **APIM worked example inside the canonical BFF pattern page**, which is fully quotable, plus the IETF BFF definition. See [Research Gaps](#research-gaps).

### 4.1 What the APIM example actually is

From [Backends for Frontends pattern → Example](https://learn.microsoft.com/en-us/azure/architecture/patterns/backends-for-frontends):

> This example demonstrates a use case for the pattern in which two distinct client applications, a mobile app and a desktop application, interact with Azure API Management (data plane gateway). This gateway serves as an abstraction layer and manages common cross-cutting concerns such as:
>
> - **Authorization.** Ensures that only verified identities with the proper access tokens can call protected resources by using API Management with Microsoft Entra ID.
> - **Monitoring.** Captures and sends request and response details to Azure Monitor for observability purposes.
> - **Request caching.** Optimizes repeated requests by serving responses from cache by built-in features of API Management.
> - **Routing and aggregation.** Directs incoming requests to the appropriate BFF services.

Design components:

> - **Microsoft Entra ID** serves as the cloud-based identity provider. It provides tailored audience claims for both mobile and desktop clients. These claims are then used for authorization.
> - **API Management** serves as a proxy between the clients and their BFF services, which establishes a perimeter. API Management is configured with policies to [validate the JSON Web Tokens](https://learn.microsoft.com/en-us/azure/api-management/validate-jwt-policy) and rejects requests that lack a token or contain invalid claims for the targeted BFF service.
> - **Azure Monitor** functions as the centralized monitoring solution.
> - **Azure Functions** is a serverless solution that efficiently exposes BFF service logic across multiple endpoints.

### 4.2 The critical distinction for Croesus

**The APIM example is *not* a cookie-session BFF.** Read the flow carefully:

> 1. The mobile client sends a `GET` request for page `1`, **including the OAuth 2.0 token in the authorization header**.
> 2. The request reaches the API Management gateway, which intercepts it and:
>    1. **Checks the authorization status.** API Management implements defense in depth, so it checks the validity of the access token.

The client is **already holding a bearer token** and sending it. APIM validates that token (`validate-jwt` policy) and routes. That is an **API-gateway / curated-API** pattern. It is *"BFF"* in Sam Newman's original sense (a per-client backend that shapes payloads), **not** in the IETF `oauth-browser-based-apps` §6.1 sense (a confidential client that holds tokens so the browser doesn't).

The architecture-center page itself hints at the ambiguity:

> Another scenario is an application that combines an [API gateway](https://learn.microsoft.com/en-us/azure/architecture/microservices/design/gateway) with microservices. This approach might be sufficient for some scenarios where BFF services are typically recommended.

And:

> The BFF service should only handle client-specific logic related to a specific user experience. Cross-cutting features, such as monitoring and authorization, should be abstracted to maintain efficiency. Typical features that might surface in the BFF service are handled separately with the [Gatekeeping](https://learn.microsoft.com/en-us/azure/architecture/patterns/gatekeeper), [Rate Limiting](https://learn.microsoft.com/en-us/azure/architecture/patterns/rate-limiting-pattern), and [Routing](https://learn.microsoft.com/en-us/azure/architecture/patterns/gateway-routing) patterns.

### 4.3 Can APIM do the OIDC code flow + cookie session itself?

**No, not as a first-class capability.** Based on the sourced material:

- APIM's documented OAuth surface in this scenario is `validate-jwt` — **inbound token validation**, not the authorization-code grant, not PKCE code-verifier custody, not client-secret custody, not cookie issuance, not refresh-token rotation, not `HttpOnly`/`__Host` cookie management, not `OnValidatePrincipal`-style transparent refresh.
- APIM has no documented mechanism (in the sourced pages) for the IETF BFF requirements §6.1.2.1 (`/login`, `/callback`, `/logout`, `check session` endpoints), §6.1.2.2 (refresh-token custody), or §6.1.2.3 (cookie-based session state).

**Therefore: APIM still needs a token-handling component.** In the Microsoft example that component is the Azure Functions BFF service — but in that example, tokens are already in the browser, so it isn't solving the XSS problem at all.

### 4.4 Recommendation matrix

| Situation | Recommend | Why |
| --- | --- | --- |
| Browser/SPA must not hold tokens; XSS is the primary threat | **In-process ASP.NET Core BFF** (the Learn article) | Only pattern satisfying IETF §6.1 MUSTs: confidential client + cookie session + token custody. |
| Multiple client form factors need differently shaped payloads | **BFF per client** (Newman/Azure sense) | *"If the application supports multiple interfaces, such as a web interface and a mobile app, create a BFF service for each interface."* |
| Need rate limiting, caching, centralized observability, perimeter | **APIM in front of the BFF(s)** | *"Cross-cutting features ... should be abstracted to maintain efficiency."* |
| Native mobile client (not a browser) | APIM + `validate-jwt`, client holds token | Native apps follow RFC 8252, not the browser-based-apps BCP. |
| Both | **APIM → ASP.NET Core BFF → APIs** | Defence in depth; the Azure example literally calls APIM's JWT check *"defense in depth"*. |

**The two are complementary, not alternatives.** For Croesus's browser estate, APIM alone does *not* discharge the BFF obligation.

---

## 5. YARP Specifics

**Package**: [`Yarp.ReverseProxy`](https://www.nuget.org/packages/Yarp.ReverseProxy) — namespace `Yarp.ReverseProxy.Transforms` per the sample's `using`. Docs now live in the ASP.NET Core docset: [YARP Request and Response Transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms) and [YARP Configuration Files](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-files) (monikers back to `aspnetcore-1.0`, `defaultMoniker: aspnetcore-10.0`).

### 5.1 Two ways to proxy — `MapForwarder` vs `MapReverseProxy`

**Direct forwarding (what the Blazor sample uses)** — one route, no config file:

```csharp
builder.Services.AddHttpForwarderWithServiceDiscovery();   // Aspire flavour
// or: builder.Services.AddHttpForwarder();                // plain YARP

app.MapForwarder("/weather-forecast", "https://weatherapi", transformBuilder => { /* ... */ })
   .RequireAuthorization();
```

**Full reverse proxy (config-driven)**:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add the reverse proxy capability to the server
builder.Services.AddReverseProxy()
    // Initialize the reverse proxy from the "ReverseProxy" section of configuration
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Register the reverse proxy routes
app.MapReverseProxy();

app.Run();
```

### 5.2 `AddTransforms` from code

> `AddTransforms` can be called after `AddReverseProxy` to provide a callback for configuring transforms. This callback is invoked each time a route is built or rebuilt and allows the developer to inspect the `RouteConfig` information and conditionally add transforms for it.
>
> The `AddTransforms` callback provides a `TransformBuilderContext` where transforms can be added or configured. ... The `TransformBuilderContext` also includes an `IServiceProvider` for access to any needed services.

```csharp
services.AddReverseProxy()
    .LoadFromConfig(_configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        // Added to all routes.
        builderContext.AddPathPrefix("/prefix");

        // Conditionally add a transform for routes that require auth.
        if (!string.IsNullOrEmpty(builderContext.Route.AuthorizationPolicy))
        {
            builderContext.AddRequestTransform(async transformContext =>
            {
                transformContext.ProxyRequest.Headers.Add("CustomHeader", "CustomValue");
            });
        }
    });
```

This `builderContext.Route.AuthorizationPolicy` guard is exactly the right shape for a "only attach a bearer token on authorized routes" BFF.

> Request and response **body** transforms are not provided by YARP but you can write middleware to do this.

### 5.3 Configuration schema (`ReverseProxy:Routes` / `Clusters`)

Minimum viable:

```json
{
  "ReverseProxy": {
    "Routes": {
      "route1" : {
        "ClusterId": "cluster1",
        "Match": {
          "Path": "{**catch-all}",
          "Hosts" : [ "www.aaaaa.com", "www.bbbbb.com"]
        }
      }
    },
    "Clusters": {
      "cluster1": {
        "Destinations": {
          "cluster1/destination1": {
            "Address": "https://example.com/"
          }
        }
      }
    }
  }
}
```

Route requirements:

> Each route requires at least the following fields:
>
> - `RouteId`: A unique name for the route.
> - `ClusterId`: The name of an entry in the `Clusters` section.
> - `Match`: Either a `Hosts` array or a `Path` pattern string.

> Route matching is based on the most specific routes having highest precedence. ... Explicit ordering can be achieved by using the `order` field, where lower values take higher priority.

Relevant properties for a BFF:

```json
"allrouteprops" : {
  "ClusterId": "allclusterprops",
  "Order" : 100,
  "MaxRequestBodySize" : 1000000,
  "AuthorizationPolicy" : "Anonymous",
  "CorsPolicy" : "Default",
  "Match": {
    "Path": "/something/{**remainder}",
    "Methods" : [ "GET", "PUT" ]
  },
  "Transforms" : [
    { "RequestHeader": "MyHeader", "Set": "MyValue" }
  ]
}
```

> `"AuthorizationPolicy" : "Anonymous"` — Name of the policy or `"Default"`, `"Anonymous"`

**IETF §6.1.3.6 compliance note**: YARP's `Clusters`/`Destinations` model *is* the explicit allowlist the draft demands (*"maintaining an explicit allowlist of approved resource servers"*). Using named clusters rather than dynamic destination selection satisfies the MUST. Call this out to Croesus explicitly — it's a genuine architectural advantage of YARP over a hand-rolled proxy.

Also note: *"the configuration updates without restarting the proxy when the source file changes."*

### 5.4 Default transforms already applied

> The following transforms are enabled by default for all routes:
>
> - Host - Suppress the incoming request's Host header. The proxy request will default to the host name specified in the destination server address.
> - X-Forwarded-For - Sets the client's IP address to the X-Forwarded-For header.
> - X-Forwarded-Proto - Sets the request's original scheme (http/https) to the X-Forwarded-Proto header.
> - X-Forwarded-Host - Sets the request's original Host to the X-Forwarded-Host header.
> - X-Forwarded-Prefix - Sets the request's original PathBase, if any, to the X-Forwarded-Prefix header.

Important for the downstream API: it will receive `X-Forwarded-*` and must be configured with `UseForwardedHeaders` + `KnownProxies` if it needs the real client IP. (See §8.3.)

**Security note**: YARP does **not** automatically strip the inbound `Cookie` header. IETF §6.1.1 states the BFF *"removes the cookie from the request, attaches the user's access token to the request, and forwards it."* For strict compliance, add a request-header removal transform:

```csharp
transformBuilder.AddRequestHeaderRemove("Cookie");
```

This is a **gap in the Microsoft sample** worth raising with Croesus.

---

## 6. The .NET Aspire Angle — Required or Optional?

**Verdict: entirely optional. It is a developer-inner-loop convenience, not a security or architectural requirement.**

### 6.1 What Aspire contributes to the sample

Article:

> - Aspire:
>   - `Aspire.AppHost`: Used to manage the high-level orchestration concerns of the app.
>   - `Aspire.ServiceDefaults`: Contains default Aspire app configurations that can be extended and customized as needed.

> Aspire improves the experience of building .NET cloud-native apps. It provides a consistent, opinionated set of tools and patterns for building and running distributed apps.

Total Aspire footprint in the BFF project:

```csharp
builder.AddServiceDefaults();     // OpenTelemetry, health checks, service discovery, resilience
// ...
app.MapDefaultEndpoints();        // /health, /alive
```

Plus `AppHost.cs`:

```csharp
// 10.0/BlazorWebAppOidcBffAutoYarpAspire/Aspire/Aspire.AppHost/AppHost.cs
var builder = DistributedApplication.CreateBuilder(args);

var weatherApi = builder.AddProject<Projects.MinimalApiJwt>("weatherapi");

builder.AddProject<Projects.BlazorWebAppOidc>("blazorfrontend")
    .WithReference(weatherApi);

builder.Build().Run();
```

The name `"weatherapi"` is what makes `https://weatherapi` resolve in `MapForwarder` and `HttpClient.BaseAddress`. That's the **entire** coupling.

### 6.2 Microsoft's own statement that it doesn't matter in production

From the Entra article's *YARP forwarder destination prefix* section:

> The Blazor Web App server project's YARP forwarder ... specifies a destination prefix of `https://weatherapi`. This value matches the project name passed to `AddProject` in the `AppHost.cs` file of the `Aspire.AppHost` project.
>
> **There's no need to change the destination prefix of the YARP forwarder when deploying the Blazor Web App to production.** The Microsoft Identity Web Downstream API package uses the base URI passed via configuration to make the web API call from the `ServerWeatherForecaster`, not the destination prefix of the YARP forwarder. In production, the YARP forwarder merely transforms the request, adding the user's access token.

### 6.3 Aspire's own positioning

From [What is Aspire?](https://aspire.dev/get-started/what-is-aspire/):

> - **Aspire is** the product layer where you model, run, and observe a distributed application in one place.
> - **Aspire isn't** a replacement for your application framework.
> - **Aspire isn't** a cloud provider or production runtime.

### 6.4 Prerequisite friction

> Aspire requires [Visual Studio](https://visualstudio.microsoft.com/) version 17.10 or later.

> The sample app only configures an **insecure HTTP launch profile (`http`)** for use during development testing.

That second note matters: the shipped Aspire sample runs over plain HTTP locally, which is fine for dev but would violate the IETF `Secure` cookie MUST in any environment resembling production.

### 6.5 De-Aspire-ing the sample (exact deltas)

| Remove | Replace with |
| --- | --- |
| `builder.AddServiceDefaults();` | Nothing, or your own OTel/health-check registration |
| `app.MapDefaultEndpoints();` | `app.MapHealthChecks("/health");` |
| `builder.Services.AddHttpForwarderWithServiceDiscovery();` | `builder.Services.AddHttpForwarder();` |
| `"https://weatherapi"` (in `MapForwarder` and `HttpClient.BaseAddress`) | `builder.Configuration["DownstreamApi:BaseUrl"]` |
| `Aspire.AppHost` + `Aspire.ServiceDefaults` projects | Delete; start the **server project** directly |

The non-Aspire pivots are already structured this way — they read `builder.Configuration["ExternalApiUri"]` and have no `AddServiceDefaults`. **Tell Croesus to use the `without-yarp-and-aspire` pivot as the baseline and add YARP only if they need path-based proxying.**

---

## 7. Reference Implementation Sketch — a "Third App" for This Repo

### 7.1 Repo reality check

Verified target frameworks (from `.csproj` scan):

| Project | TFM |
| --- | --- |
| `api/Croesus.Api.csproj` | `net8.0` |
| `api/Tests/Croesus.Api.Tests.csproj` | `net8.0` |
| `poc/legacy-net452/LegacyNet452.csproj` | `net452` |
| `poc/legacy-net452/Tests/LegacyNet452.Tests.csproj` | `net452` |
| `poc/modern-net10/Croesus.ModernBff.csproj` | **`net10.0`** |
| `poc/modern-net10/Tests/Croesus.ModernBff.Tests.csproj` | `net10.0` |

**The repo already targets `net10.0`** for the modern BFF PoC. `net10.0` is the current LTS release (the Learn articles carry the `aspnetcore-10.0` default moniker), so the third app should target `net10.0` for consistency with `poc/modern-net10` and with every code sample quoted above.

The existing `poc/modern-net10/Program.cs` is already a **cookie-session BFF** using `Microsoft.Identity.Web`, with:

- `AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))`
- `__Host-Croesus.ModernBff.Session` cookie — `HttpOnly`, `SecurePolicy.Always`, `SameSite.Lax`, `Path = "/"`, `Domain = null`, 30-minute absolute expiry, no sliding
- `__Host-Croesus.ModernBff.Antiforgery` — `HttpOnly`, `Secure`, `SameSite.Strict`
- `ResponseType = Code`, `UsePkce = true`, `MapInboundClaims = false`
- **`SaveTokens = false`** ← the key difference; today it is an authentication-only BFF, not a token-forwarding BFF
- `ForwardedHeaders` with explicit `KnownProxies` from config
- Custom `TenantPolicy` issuer validator + `OnTokenValidated` tenant assertion
- An evidence endpoint: `GET /api/session` → `SessionResponse.FromPrincipal(context.User)`

**So the "third app" is not a from-scratch build.** It is `poc/modern-net10` + `SaveTokens` + `offline_access` + YARP + an enriched evidence page. That framing is much easier to sell and much faster to demo.

> Note: `poc/modern-net10`'s cookie config already satisfies more of [`draft-ietf-oauth-browser-based-apps-25` §6.1.3.2](https://www.ietf.org/archive/id/draft-ietf-oauth-browser-based-apps-25.html) than the Microsoft Learn sample does (`__Host` prefix, `Secure`, `HttpOnly`, `Path=/`, no `Domain`). The one deviation is `SameSite.Lax` rather than the draft's `SHOULD` of `Strict` — which is a deliberate and correct trade-off, because `Strict` breaks the OIDC redirect back from `login.microsoftonline.com` on the `/signin-oidc` callback. Document that reasoning; it's the kind of detail that earns credibility.

### 7.2 Proposed layout

```text
poc/bff-yarp-net10/
├─ Croesus.BffYarp.csproj
├─ Program.cs
├─ appsettings.json
├─ appsettings.Development.json
├─ Security/
│  ├─ AccessTokenTransform.cs        # the YARP transform (Q7 deliverable)
│  ├─ CookieOidcRefresher.cs         # copied verbatim from the Learn sample
│  └─ EvidenceCollector.cs
├─ Models/
│  └─ EvidenceResponse.cs
└─ wwwroot/
   └─ index.html                     # or a Razor Component evidence page
```

### 7.3 `Croesus.BffYarp.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Croesus.BffYarp</RootNamespace>
    <UserSecretsId>croesus-bff-yarp-net10</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <!-- Confirm current versions at https://www.nuget.org before pinning. -->
    <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.*" />
    <PackageReference Include="Microsoft.IdentityModel.Validators" Version="8.*" />
    <PackageReference Include="Yarp.ReverseProxy" Version="2.*" />
  </ItemGroup>

</Project>
```

Package citations:

- [`Yarp.ReverseProxy`](https://www.nuget.org/packages/Yarp.ReverseProxy) — namespace `Yarp.ReverseProxy.Transforms`, used by every BFF sample above.
- [`Microsoft.IdentityModel.Validators`](https://www.nuget.org/packages/Microsoft.IdentityModel.Validators) — *"Add the `Microsoft.IdentityModel.Validators` NuGet package to the server project"* (required only for the `common` authority path).
- [`Microsoft.Identity.Web`](https://www.nuget.org/packages/Microsoft.Identity.Web) — swap in if you prefer `AddMicrosoftIdentityWebApp` + `AddDownstreamApi` (the `poc/modern-net10` approach).

> The Learn articles' standing caveat: *"For guidance on adding packages to .NET apps, see the articles under Install and manage packages at Package consumption workflow (NuGet documentation). Confirm correct package versions at NuGet.org."*

### 7.4 `Program.cs`

Provenance for each block is called out in comments.

```csharp
using System.Net;
using Croesus.BffYarp.Models;
using Croesus.BffYarp.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

const string OidcScheme = "MicrosoftOidc";

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Forwarded headers. MUST be configured before anything that builds absolute
// URLs (the OIDC redirect_uri in particular).
// Source: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer
// Pattern mirrors the existing poc/modern-net10/Program.cs in this repo.
// ---------------------------------------------------------------------------
var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in knownProxies)
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

// ---------------------------------------------------------------------------
// Authentication. Option-for-option from:
// 10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc/Program.cs
// ---------------------------------------------------------------------------
builder.Services.AddAuthentication(OidcScheme)
    .AddOpenIdConnect(OidcScheme, oidcOptions =>
    {
        // Sign-in scheme must be able to persist credentials across requests.
        oidcOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        // Authorization code flow only. No implicit, no hybrid.
        oidcOptions.ResponseType = OpenIdConnectResponseType.Code;

        // PKCE. Default is true on the OIDC handler; set explicitly so the
        // evidence page can assert it.
        oidcOptions.UsePkce = true;

        // PAR: Entra does not support it and has no plans to. Leave at default.
        oidcOptions.PushedAuthorizationBehavior = PushedAuthorizationBehavior.UseIfAvailable;

        // Keep original claim types from the token.
        oidcOptions.MapInboundClaims = false;
        oidcOptions.TokenValidationParameters.NameClaimType = "name";
        oidcOptions.TokenValidationParameters.RoleClaimType = "roles";

        // Tokens live server-side, inside the encrypted auth ticket.
        oidcOptions.SaveTokens = true;
        oidcOptions.Scope.Add(OpenIdConnectScope.OfflineAccess);

        // Authority / ClientId / ClientSecret / remaining Scope come from
        // Authentication:Schemes:MicrosoftOidc:* in configuration.
        // Paths default to /signin-oidc, /signout-callback-oidc, /signout-oidc.
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

// ---------------------------------------------------------------------------
// Cookie hardening. Satisfies draft-ietf-oauth-browser-based-apps-25 section 6.1.3.2:
//   MUST Secure, MUST HttpOnly, SHOULD SameSite=Strict, SHOULD Path=/,
//   SHOULD NOT set Domain, SHOULD use the __Host prefix.
// SameSite is Lax (not Strict) because Strict breaks the OIDC redirect back
// to /signin-oidc. Documented deviation.
// Mirrors poc/modern-net10/Program.cs.
// ---------------------------------------------------------------------------
builder.Services.PostConfigure<CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme,
    options =>
    {
        options.Cookie.Name = "__Host-Croesus.BffYarp.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.Cookie.Domain = null;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = false;

        // Transparent, non-interactive access-token refresh.
        // Source: 10.0/.../CookieOidcServiceCollectionExtensions.cs
        options.Events.OnValidatePrincipal = context =>
            context.HttpContext.RequestServices
                .GetRequiredService<CookieOidcRefresher>()
                .ValidateOrRefreshCookieAsync(context, OidcScheme);
    });

builder.Services.AddSingleton<CookieOidcRefresher>();

// ---------------------------------------------------------------------------
// CSRF. draft-ietf-oauth-browser-based-apps-25 section 6.1.3.3:
//   "The BFF MUST implement a proper CSRF defense."
// ---------------------------------------------------------------------------
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-Croesus.BffYarp.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.HeaderName = "X-CSRF-TOKEN";
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// ---------------------------------------------------------------------------
// YARP. Config-driven routes/clusters give the explicit destination allowlist
// required by draft-ietf-oauth-browser-based-apps-25 section 6.1.3.6.
// Source: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-files
//         https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms
// ---------------------------------------------------------------------------
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms<AccessTokenTransformProvider>();

builder.Services.AddSingleton<EvidenceCollector>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseHsts();
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.UseStaticFiles();

// ---------------------------------------------------------------------------
// Login / logout. Source: 10.0/.../LoginLogoutEndpointRouteBuilderExtensions.cs
// ---------------------------------------------------------------------------
var auth = app.MapGroup("/authentication");

auth.MapGet("/login", (string? returnUrl) =>
        TypedResults.Challenge(BuildAuthProperties(returnUrl)))
    .AllowAnonymous();

// POST, not GET. Signs out BOTH schemes: if the OIDC handler is skipped the user
// is silently signed back in on the next protected page.
auth.MapPost("/logout", ([FromForm] string? returnUrl) =>
    TypedResults.SignOut(
        BuildAuthProperties(returnUrl),
        [CookieAuthenticationDefaults.AuthenticationScheme, OidcScheme]));

// ---------------------------------------------------------------------------
// Evidence endpoint: everything the demo needs to SHOW what happened.
// ---------------------------------------------------------------------------
app.MapGet("/api/evidence", async (HttpContext context, EvidenceCollector collector) =>
        Results.Ok(await collector.CollectAsync(context)))
    .RequireAuthorization();

// ---------------------------------------------------------------------------
// The proxy. Every /api/** call is authorized, then token-stamped, then forwarded.
// ---------------------------------------------------------------------------
app.MapReverseProxy();

app.Run();

static AuthenticationProperties BuildAuthProperties(string? returnUrl)
{
    const string pathBase = "/";

    // Prevent open redirects. Source: LoginLogoutEndpointRouteBuilderExtensions.cs
    if (string.IsNullOrEmpty(returnUrl))
    {
        returnUrl = pathBase;
    }
    else if (!Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
    {
        returnUrl = new Uri(returnUrl, UriKind.Absolute).PathAndQuery;
    }
    else if (returnUrl[0] != '/')
    {
        returnUrl = $"{pathBase}{returnUrl}";
    }

    return new AuthenticationProperties { RedirectUri = returnUrl };
}

public partial class Program;
```

> `CookieOidcRefresher` is copied verbatim from [`10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc/CookieOidcRefresher.cs`](https://raw.githubusercontent.com/dotnet/blazor-samples/main/10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc/CookieOidcRefresher.cs) — full text in §1.8 above. If you use `Microsoft.Identity.Web` instead, delete it: *"Automatic non-interactive token refresh is managed by the framework."*

### 7.5 The YARP transform class

This is the Q7 deliverable — the `MapForwarder` inline lambda from the article, promoted to an `ITransformProvider` so it can apply per-route and carry correlation headers.

```csharp
// Security/AccessTokenTransform.cs
using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Croesus.BffYarp.Security;

/// <summary>
/// Attaches the user's server-held access token to proxied requests.
/// Core logic is the request transform from
/// 10.0/BlazorWebAppOidcBffAutoYarpAspire/BlazorWebAppOidc/Program.cs:
///
///   var accessToken = await transformContext.HttpContext.GetTokenAsync("access_token");
///   transformContext.ProxyRequest.Headers.Authorization = new("Bearer", accessToken);
/// </summary>
internal sealed class AccessTokenTransformProvider : ITransformProvider
{
    public const string CorrelationHeader = "X-Croesus-Correlation-Id";

    public void ValidateRoute(TransformRouteValidationContext context) { }

    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        // Only stamp tokens on routes that actually require an authenticated user.
        // Pattern from https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms
        if (string.IsNullOrEmpty(context.Route.AuthorizationPolicy) ||
            string.Equals(context.Route.AuthorizationPolicy, "Anonymous", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // draft-ietf-oauth-browser-based-apps-25 section 6.1.1: the BFF "removes the
        // cookie from the request, attaches the user's access token to the request,
        // and forwards it". The downstream API must never see the session cookie.
        context.AddRequestHeaderRemove("Cookie");

        context.AddRequestTransform(async transformContext =>
        {
            var httpContext = transformContext.HttpContext;

            var accessToken = await httpContext.GetTokenAsync("access_token");

            if (string.IsNullOrEmpty(accessToken))
            {
                // No token in the ticket: fail closed rather than forwarding anonymously.
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            transformContext.ProxyRequest.Headers.Authorization = new("Bearer", accessToken);

            // Correlation for the evidence page and downstream logs.
            var correlationId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
        });
    }
}
```

Simpler inline variant, if you prefer to stay literally identical to the article:

```csharp
app.MapForwarder("/api/{**catch-all}", "https://localhost:7277", transformBuilder =>
{
    transformBuilder.AddRequestTransform(async transformContext =>
    {
        var accessToken = await transformContext.HttpContext.GetTokenAsync("access_token");
        transformContext.ProxyRequest.Headers.Authorization = new("Bearer", accessToken);
    });
}).RequireAuthorization();
```

### 7.6 Evidence collector

```csharp
// Security/EvidenceCollector.cs
using System.Diagnostics;
using Croesus.BffYarp.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Croesus.BffYarp.Security;

internal sealed class EvidenceCollector
{
    public async Task<EvidenceResponse> CollectAsync(HttpContext context)
    {
        // Token metadata only. Never the token itself.
        // Learn caution: "In production, avoid logging the token or its contents."
        var accessToken = await context.GetTokenAsync("access_token");
        var expiresAt = await context.GetTokenAsync("expires_at");
        var hasRefreshToken = !string.IsNullOrEmpty(await context.GetTokenAsync("refresh_token"));

        TokenMetadata? tokenMetadata = null;
        if (!string.IsNullOrEmpty(accessToken))
        {
            var handler = new JsonWebTokenHandler();
            var jwt = handler.ReadJsonWebToken(accessToken);
            tokenMetadata = new TokenMetadata(
                Issuer: jwt.Issuer,
                Audiences: [.. jwt.Audiences],
                ValidFrom: jwt.ValidFrom,
                ValidTo: jwt.ValidTo,
                Scopes: jwt.Claims.FirstOrDefault(c => c.Type is "scp" or "scope")?.Value,
                LengthInCharacters: accessToken.Length);
        }

        return new EvidenceResponse(
            // What the browser actually holds.
            BrowserVisibleCookies: [.. context.Request.Cookies.Keys],
            BrowserSentAuthorizationHeader: context.Request.Headers.ContainsKey("Authorization"),

            // Who the user is.
            IsAuthenticated: context.User.Identity?.IsAuthenticated ?? false,
            AuthenticationType: context.User.Identity?.AuthenticationType,
            Claims: [.. context.User.Claims.Select(c => new ClaimView(c.Type, c.Value))],

            // What the server holds on their behalf.
            ServerHeldAccessToken: tokenMetadata,
            ServerHeldRefreshToken: hasRefreshToken,
            AccessTokenExpiresAt: expiresAt,

            // Correlation.
            TraceIdentifier: context.TraceIdentifier,
            ActivityId: Activity.Current?.Id,
            CorrelationHeaderSentDownstream: AccessTokenTransformProvider.CorrelationHeader,

            // Proxy-awareness — makes the App Service / SiteMinder story visible.
            RequestScheme: context.Request.Scheme,
            RequestHost: context.Request.Host.Value,
            RemoteIpAddress: context.Connection.RemoteIpAddress?.ToString(),
            OriginalProto: context.Request.Headers["X-Original-Proto"].ToString(),
            OriginalHost: context.Request.Headers["X-Original-Host"].ToString());
    }
}
```

```csharp
// Models/EvidenceResponse.cs
namespace Croesus.BffYarp.Models;

public sealed record ClaimView(string Type, string Value);

public sealed record TokenMetadata(
    string Issuer,
    string[] Audiences,
    DateTime ValidFrom,
    DateTime ValidTo,
    string? Scopes,
    int LengthInCharacters);

public sealed record EvidenceResponse(
    string[] BrowserVisibleCookies,
    bool BrowserSentAuthorizationHeader,
    bool IsAuthenticated,
    string? AuthenticationType,
    ClaimView[] Claims,
    TokenMetadata? ServerHeldAccessToken,
    bool ServerHeldRefreshToken,
    string? AccessTokenExpiresAt,
    string TraceIdentifier,
    string? ActivityId,
    string CorrelationHeaderSentDownstream,
    string RequestScheme,
    string? RequestHost,
    string? RemoteIpAddress,
    string OriginalProto,
    string OriginalHost);
```

**The demo money shot**: `BrowserVisibleCookies` lists only `__Host-Croesus.BffYarp.Session` and `__Host-Croesus.BffYarp.Antiforgery`; `BrowserSentAuthorizationHeader` is `false`; and yet `ServerHeldAccessToken` shows a real issuer, audience, scope set, and expiry. Open DevTools → Application → Local Storage → **empty**. That single side-by-side makes the entire argument.

### 7.7 `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Yarp": "Information"
    }
  },
  "AllowedHosts": "*",

  "Authentication": {
    "Schemes": {
      "MicrosoftOidc": {
        "Authority": "https://login.microsoftonline.com/{TENANT ID (BFF APP)}/v2.0",
        "ClientId": "{CLIENT ID (BFF APP)}",
        "CallbackPath": "/signin-oidc",
        "SignedOutCallbackPath": "/signout-callback-oidc",
        "RemoteSignOutPath": "/signout-oidc",
        "SignedOutRedirectUri": "/",
        "Scope": [
          "openid",
          "profile",
          "offline_access",
          "{APP ID URI (WEB API)}/Croesus.Read"
        ]
      }
    }
  },

  "ForwardedHeaders": {
    "KnownProxies": []
  },

  "ReverseProxy": {
    "Routes": {
      "croesus-api": {
        "ClusterId": "croesus-api",
        "AuthorizationPolicy": "Default",
        "Match": {
          "Path": "/api/{**remainder}",
          "Methods": [ "GET", "POST", "PUT", "PATCH", "DELETE" ]
        },
        "Transforms": [
          { "PathRemovePrefix": "/api" }
        ]
      }
    },
    "Clusters": {
      "croesus-api": {
        "Destinations": {
          "croesus-api/primary": {
            "Address": "https://localhost:7277/"
          }
        }
      }
    }
  }
}
```

Notes:

- The `Authentication:Schemes:MicrosoftOidc:*` shape is lifted verbatim from the article's *Supply configuration with the JSON configuration provider* section — *"The configuration is automatically picked up by the authentication builder."*
- `ClientSecret` is **deliberately absent**. Set it with `dotnet user-secrets set "Authentication:Schemes:MicrosoftOidc:ClientSecret" "{SECRET}"` locally and Key Vault in Azure.
- The single-entry `Clusters` block is the IETF §6.1.3.6 allowlist.
- `"AuthorizationPolicy": "Default"` is what makes `AccessTokenTransformProvider.Apply` engage.
- `/api/evidence` is mapped **before** `MapReverseProxy()`, so it is served locally rather than proxied. If you prefer, rename the proxy prefix to `/downstream/**` to remove any ambiguity.

### 7.8 `appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.AspNetCore.Authentication": "Debug",
      "Microsoft.AspNetCore.HttpLogging": "Information",
      "Yarp": "Debug"
    }
  }
}
```

### 7.9 Entra app registrations for the third app

Per both Learn articles:

**Web API first** (`Croesus.Api`):

1. App registrations → New registration.
2. Expose an API → Application ID URI (`api://{CLIENT ID}`) → Add scope `Croesus.Read`.
3. Record the App ID URI.

**Then the BFF** (`Croesus.BffYarp`):

1. New registration → **Web** platform.
2. Redirect URIs: `https://localhost/signin-oidc` **and** `https://localhost/signout-callback-oidc`.
3. Front-channel logout URL: `https://localhost/signout-oidc`.
4. **Implicit grant and hybrid flows: leave BOTH checkboxes unchecked.**
5. API permissions → add `api://{API CLIENT ID}/Croesus.Read` → grant admin consent.
6. Certificates & secrets → New client secret.

---

## 8. Pitfalls the Docs Warn About

### 8.1 Cookie size and `ChunkingCookieManager`

The Learn articles do **not** mention `ChunkingCookieManager` by name, but they do document the underlying problem in detail. From *Application roles for apps registered with Microsoft Entra (ME-ID)*:

> The approach described in this section configures ME-ID to send groups and roles in the authentication cookie header. When users are only a member of a few security groups and roles, the following approach should work for most hosting platforms without running into a problem where headers are too long, **for example with IIS hosting that has a default header length limit of 16 KB (`MaxRequestBytes`)**. If header length is a problem due to high group or role membership, we recommend not following the guidance in this section in favor of implementing [Microsoft Graph](https://learn.microsoft.com/en-us/graph/sdks/sdks-overview) to obtain a user's groups and roles from ME-ID separately, **an approach that doesn't inflate the size of the authentication cookie**. For more information, see [Bad Request - Request Too Long - IIS Server (`dotnet/aspnetcore` #57545)](https://github.com/dotnet/aspnetcore/issues/57545).

**Why this bites a BFF specifically**: `SaveTokens = true` puts access + ID + refresh tokens *inside* the cookie. Add `"groupMembershipClaims": "All"` and a user in 50 groups, and you blow past 16 KB even with ASP.NET Core's built-in cookie chunking (`.AspNetCore.Cookies`, `.AspNetCore.CookiesC1`, `C2`, …) — because chunking splits the cookie but IIS still sums the total `Cookie:` header.

**Mitigations for Croesus:**

- Do not set `groupMembershipClaims: All`. Use Microsoft Graph for group lookup.
- Use `AuthenticationStateSerializationOptions.SerializeAllClaims = false` if using Blazor Auto (the sample sets `true`, and the article says: *"If you only want the name and role claims serialized for CSR, remove the option or set it to `false`."*).
- Consider a server-side ticket store (`CookieAuthenticationOptions.SessionStore`) so the cookie carries only a session ID — this is the IETF §6.1.2.3 "server-side session" option: *"Server-side sessions expose only a session identifier and keep all data on the server. Doing so ensures a great level of control over active sessions ... The downside of this approach is the impact on scalability."*
- If you keep the client-side session with tokens in the cookie, IETF §6.1.3.2 says the BFF **SHOULD** encrypt cookie contents — ASP.NET Core Data Protection already does this.
- Raise IIS's `MaxRequestBytes`/`MaxFieldLength` if hosting behind IIS (relevant given Croesus's IIS/ASPX estate).

### 8.2 `MaxAge` / sliding expiration vs token lifetime mismatch

IETF §6.1.2.2:

> When the refresh token expires, there is no way to obtain a valid access token without running an entirely new Authorization Code flow. Therefore, **it makes sense to configure the lifetime of the cookie-based session managed by the BFF to be equal to the maximum lifetime of the refresh token.** Additionally, when the BFF learns that a refresh token for an active session is no longer valid, it also makes sense to invalidate the session.

The `CookieOidcRefresher` enforces the second half — `validateContext.RejectPrincipal()` on a failed refresh.

Concrete failure modes to demo:

- **Cookie outlives refresh token** → user appears signed in, every API call 401s. Bad UX, looks like an outage.
- **Cookie expires before refresh token** → user is bounced to login while a perfectly good refresh token sits unused.
- **`SlidingExpiration = true` + short absolute refresh-token lifetime** → sliding renewal keeps the cookie alive indefinitely past the refresh token's death. This is exactly why `poc/modern-net10` sets `SlidingExpiration = false`.

### 8.3 `ForwardedHeaders` / `X-Forwarded-Proto` — the classic App Service failure

**This is the single highest-value pitfall for Croesus** given their SiteMinder-fronted, reverse-proxied estate.

[Configure ASP.NET Core to work with proxy servers and load balancers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer), *Forward the scheme for Linux and non-IIS reverse proxies*:

> Apps that call `UseHttpsRedirection` and `UseHsts` **put a site into an infinite loop** if deployed to an Azure Linux App Service, Azure Linux virtual machine (VM), or behind any other reverse proxy besides IIS. The reverse proxy terminates TLS, and Kestrel isn't made aware of the correct request scheme. **OAuth and OIDC also fail in this configuration because they generate incorrect redirects.** `UseIISIntegration` adds and configures forwarded headers middleware when running behind IIS, but there's no matching automatic configuration for Linux (Apache or Nginx integration).

Mechanism:

> `HttpContext.Request.Scheme`: Set using the `X-Forwarded-Proto` header value.

If `X-Forwarded-Proto` isn't processed, `Request.Scheme` is `http`, so the OIDC handler builds `redirect_uri=http://croesus.example.com/signin-oidc`, which does not match the registered `https://...` URI → **AADSTS50011: The redirect URI specified in the request does not match the redirect URIs configured for the application.**

The middleware is **off by default**:

> The `ForwardedHeaders` value is `ForwardedHeaders.None`. The desired forwarders must be set here to enable the middleware.

> If no `ForwardedHeadersOptions` are specified ... the default headers to forward are `ForwardedHeaders.None`. The `ForwardedHeaders` property **must** be configured with the headers to forward.

Ordering:

> Forwarded headers middleware should run **before** other middleware. ... Forwarded headers middleware can run after diagnostics and error handling, but it **must be run before calling `UseHsts`**.

```csharp
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();

// ... then everything else
```

Trust boundary:

> The request's original remote IP must match an entry in the `KnownProxies` or `KnownNetworks` lists before forwarded headers are processed. This limits header spoofing by not accepting forwarders from untrusted proxies. When an unknown proxy is detected, logging indicates the address of the proxy:
>
> ```console
> September 20th 2018, 15:49:44.168 Unknown proxy: 10.0.0.100:54321
> ```

> **Important**: Only allow trusted proxies and networks to forward headers. Otherwise, [IP spoofing](https://www.iplocation.net/ip-spoofing) attacks are possible.

Defaults that catch people out:

> - There's only **one proxy** between the app and the source of the requests.
> - Only loopback addresses are configured for known proxies and known networks.
> - `ForwardLimit` default is `1` — *"only the rightmost value from the headers is processed unless the value of `ForwardLimit` is increased."*

App Service escape hatch:

> To forward the scheme from the proxy in non-IIS scenarios, enable the forwarded headers middleware by setting `ASPNETCORE_FORWARDEDHEADERS_ENABLED` to `true`. **Warning: This flag uses settings designed for cloud environments and doesn't enable features such as the `KnownProxies` option to restrict which IPs forwarders are accepted from.**

Last-resort override when the proxy simply won't send headers:

```csharp
app.Use((context, next) =>
{
    context.Request.Scheme = "https";
    return next(context);
});

app.UseForwardedHeaders();
```

Diagnostics:

```csharp
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders;
});
// ...
app.UseForwardedHeaders();
app.UseHttpLogging();   // MUST be after UseForwardedHeaders
```

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.AspNetCore.HttpLogging": "Information"
    }
  }
}
```

> **Compounding factor for a YARP BFF**: YARP *itself* adds `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host`, `X-Forwarded-Prefix` by default on the outbound leg. With SiteMinder → App Service → BFF → YARP → API, there are **two** hops appending values. The downstream API's `ForwardLimit` of 1 will read the wrong entry. Set `ForwardLimit` explicitly on the API and configure `KnownProxies` with the BFF's egress IPs.

### 8.4 `CallbackPath` collisions and path-base issues

> Paths must match the redirect URI (login callback path) and post logout redirect (signed-out callback path) paths configured when registering the application with the OIDC provider. ... Both the sign-in and sign-out paths must be registered as redirect URIs. The default values are `/signin-oidc` and `/signout-callback-oidc`.

Collision risk for a BFF: `/signin-oidc` is a reserved path. **Do not create a YARP route matching `{**catch-all}`** or it will swallow the OIDC callback. Scope YARP to `/api/{**remainder}` (as in §7.7) or explicitly order the auth endpoints first.

The sample's own `LoginLogoutEndpointRouteBuilderExtensions` carries a known limitation:

```csharp
// TODO: Use HttpContext.Request.PathBase instead.
const string pathBase = "/";
```

If the BFF is hosted under a virtual path (`/CroesusBff/`) behind SiteMinder or IIS, this hard-coded `"/"` produces wrong return URLs. Fix by reading `HttpContext.Request.PathBase`. Related Learn guidance:

```csharp
app.UsePathBase("/foo");
// ...
app.UseRouting();
```

> When using `WebApplication` ... `app.UseRouting` must be called **after** `UsePathBase` so that the routing middleware can observe the modified path before matching routes.

Multiple redirect URIs per app registration is fine; multiple *apps sharing* a registration is not.

### 8.5 Nonce and correlation-cookie failures

> A *nonce* is a string value that associates a client's session with an ID token to mitigate [replay attacks](https://developer.mozilla.org/docs/Glossary/Replay_attack).
>
> If you receive a nonce error during authentication development and testing, **use a new InPrivate/incognito browser session for each test run**, no matter how small the change made to the app or test user because **stale cookie data can lead to a nonce error**.

> A nonce isn't required or used when a refresh token is exchanged for a new access token. In the sample app, the `CookieOidcRefresher` deliberately sets `OpenIdConnectProtocolValidator.RequireNonce` to `false`.

Article's cookie hygiene guidance:

> Cookies and site data can persist across app updates and interfere with testing and troubleshooting. Clear the following when making app code changes, user account changes with the provider, or provider app configuration changes:
>
> - User sign-in cookies
> - App cookies
> - Cached and stored site data

Visual Studio recipes: Edge `-inprivate`, Chrome `--incognito --new-window {URL}`, Firefox `-private -url {URL}`.

**Correlation cookie failures** (`.AspNetCore.Correlation.*`) — the article doesn't name them directly, but the four causes are all documented elsewhere in the material above:

| Cause | Symptom | Fix |
| --- | --- | --- |
| `SameSite` too strict on the correlation cookie | `Correlation failed` on `/signin-oidc` | The OIDC callback is a cross-site POST/redirect from `login.microsoftonline.com`. Keep correlation/nonce cookies at `SameSite=None; Secure` (the handler's default) — do **not** blanket-force `Strict`. |
| Cookie issued over `http`, returned over `https` (or vice versa) | `Correlation failed` | `UseForwardedHeaders` (§8.3). |
| Multi-instance without shared Data Protection key ring | Intermittent `Correlation failed` / random logouts | `SetApplicationName` + `PersistKeysToAzureBlobStorage` (§2.3). |
| `CorrelationCookie.Path` scoped away from the callback | `Correlation failed` | Leave at default. |

Other documented common errors:

> - Depending on the requirements of the scenario, a missing or incorrect Authority, Instance, Tenant ID, Tenant domain, Client ID, or Redirect URI prevents an app from authenticating clients.
> - Incorrect request scopes prevent clients from accessing server web API endpoints.
> - Incorrect or missing server API permissions prevent clients from accessing server web API endpoints.
> - Running the app at a different port than is configured in the Redirect URI of the IP's app registration. Note that **a port isn't required for Microsoft Entra ID and an app running at a `localhost` development testing address**, but the app's port configuration and the port where the app is running must match for non-`localhost` addresses.

And:

> **Unauthorized client for ME-ID** — Error: `unauthorized_client`, Description: `AADB2C90058: The provided application is not configured to allow public clients.` To resolve: set the [`allowPublicClient` attribute](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest#allowpublicclient-attribute) to `null` or `true`.

App-upgrade hygiene:

> 1. Clear the local system's NuGet package caches by executing `dotnet nuget locals all --clear`.
> 2. Delete the project's `bin` and `obj` folders.
> 3. Restore and rebuild the project.
> 4. Delete all of the files in the deployment folder on the server prior to redeploying the app.

---

## 9. Applicability to Croesus

### 9.1 Constraint inventory

| Constraint | Status | Source |
| --- | --- | --- |
| Production runtime is **.NET Framework 4.5.2** | **Out of support since 26 April 2022** | [.NET Framework Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework) |
| **.NET Framework 4.8.1** path under consideration | Supported, *"the latest version of .NET Framework"* | ibid. |
| Existing ASPX / SiteMinder estate (`gpd-central.desjardins.com/CentralWebApp/LogonSso.aspx`) | Federated front door, cookie/header-based SSO | Customer session |
| Repo has `net10.0` PoC (`poc/modern-net10`) | Already a cookie-session BFF | Verified in workspace |
| Repo has `net452` PoC (`poc/legacy-net452`) | Legacy comparison surface | Verified in workspace |
| Repo API is `net8.0` (`api/Croesus.Api.csproj`) | Downstream resource server candidate | Verified in workspace |

### 9.2 The lifecycle fact — lead with this

Direct quote:

> | .NET Framework 4.6.1 | November 30, 2015 | Inactive | April 26, 2022 |
> | .NET Framework 4.6 | July 20, 2015 | Inactive | April 26, 2022 |
> | **.NET Framework 4.5.2** | May 5, 2014 | **Inactive** | **April 26, 2022** |

> Support for .NET Framework versions 4.5.2, 4.6, and 4.6.1 ended on April 26, 2022, so **security fixes, updates, and technical support for these versions will no longer be provided.** Update your deployed runtime to a more recent version, such as [.NET Framework 4.6.2](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net462) in order to continue to receive updates and technical support.

And the 4.8.1 target:

> .NET Framework 4.8.1 is the latest version of .NET Framework and will continue to be distributed with future releases of Windows. **As long as it is installed on a supported version of Windows, .NET Framework 4.8.1 will continue to also be supported.**

> Beginning with version 4.5.2 and later, .NET Framework is defined as a **component of the Windows operating system (OS)**. Components receive the same support as their parent products, therefore, .NET Framework 4.5.2 and later follows the lifecycle policy of the underlying Windows OS on which it is installed.

**Framing for the conversation**: this is not a "we'd like you to modernize" ask. 4.5.2 receives **no security fixes**. The 4.8.1 move is a correct and low-risk interim step that restores supportability. But 4.8.1 still cannot run the reference architecture.

### 9.3 What 4.8.1 buys and what it does not

| Capability | .NET Framework 4.8.1 | .NET 10 (ASP.NET Core) |
| --- | --- | --- |
| Supported runtime | ✅ (tied to Windows OS lifecycle) | ✅ (LTS) |
| OIDC authorization code + PKCE, confidential client | ⚠️ via OWIN/Katana `Microsoft.Owin.Security.OpenIdConnect` (maintenance mode) | ✅ First-class `AddOpenIdConnect` |
| `SaveTokens` server-side token custody | ⚠️ OWIN equivalent exists but no supported refresh helper | ✅ `SaveTokens = true` + `CookieOidcRefresher` |
| Transparent refresh (`OnValidatePrincipal`) | ❌ No shipped pattern | ✅ Sample source above, or `Microsoft.Identity.Web` framework-managed |
| YARP | ❌ `Yarp.ReverseProxy` targets .NET Core / .NET 6+ | ✅ |
| `__Host-` / `SameSite` / `Secure` cookie control | ⚠️ Partial, manual | ✅ `CookieAuthenticationOptions` |
| Built-in antiforgery for the BFF surface | ⚠️ MVC/WebForms-era `AntiForgery` | ✅ `UseAntiforgery()` |
| `Microsoft.Identity.Web` | ❌ .NET Core / .NET only | ✅ |
| Every code sample in this document | ❌ | ✅ |

**Conclusion: 4.8.1 restores supportability but does not unlock the recommended BFF architecture.** The Microsoft-recommended BFF is an ASP.NET Core artefact end to end.

### 9.4 Recommended sequencing

```text
Phase 0  (now)     Acknowledge 4.5.2 is unsupported. Agree 4.8.1 as the
                   interim supportability floor for the existing estate.
                   Nothing architectural changes.

Phase 1  (weeks)   Stand up poc/bff-yarp-net10 (section 7) as a NEW, side-by-side
                   ASP.NET Core 10 BFF. Registers its own Entra app, proxies
                   /api/** to the existing net8.0 Croesus.Api.
                   Ship the evidence page. Demo: DevTools localStorage EMPTY,
                   no Authorization header from the browser, live token
                   metadata server-side.
                   Nothing in the legacy estate is touched.

Phase 2  (months)  Route selected ASPX surfaces through the BFF. SiteMinder
                   stays the front door; the BFF becomes the OIDC confidential
                   client behind it. Requires careful ForwardedHeaders and
                   PathBase work (see 8.3, 8.4) — this is where the risk is.

Phase 3  (longer)  Migrate ASPX page-by-page behind the stable BFF boundary.
                   The BFF's /api/** contract does not change as the frontend
                   moves, because the cookie-session boundary is stable.
```

### 9.5 SiteMinder-specific concerns to raise

1. **Two proxy hops.** SiteMinder → App Service/IIS → BFF. `ForwardLimit` defaults to `1`. Set it explicitly and populate `KnownProxies` with SiteMinder's egress addresses. Verify with `HttpLogging` (§8.3).
2. **Scheme mismatch is near-certain.** SiteMinder terminates TLS. Without `UseForwardedHeaders`, `redirect_uri` will be `http://` and Entra will reject it with AADSTS50011. This is the number-one predicted failure.
3. **Virtual path.** `CentralWebApp` suggests apps are hosted under a path. The sample's hard-coded `const string pathBase = "/"` will produce wrong return URLs. Use `HttpContext.Request.PathBase` and `UsePathBase` (§8.4). Register redirect URIs including the virtual path.
4. **Header-based SSO vs OIDC cookie session.** SiteMinder typically injects identity headers (`SM_USER` etc.). Decide explicitly: is SiteMinder the IdP (BFF trusts injected headers), or is Entra the IdP (SiteMinder is pure transport)? These are architecturally different. If SiteMinder is a transport-only front door, the BFF's OIDC flow must be able to complete redirects out to `login.microsoftonline.com` and back — confirm SiteMinder will not intercept `/signin-oidc`.
5. **Cookie path and `__Host` prefix.** `__Host-` requires `Path=/` and no `Domain`. If SiteMinder rewrites paths or the app is under a virtual directory, `__Host-` may be unusable — fall back to `__Secure-` with explicit `Path`. Document the deviation against IETF §6.1.3.2.
6. **Existing domain-scoped SiteMinder cookies.** IETF §6.1.3.2 warns: *"a subdomain takeover attack against `b.example.com` can enable CSRF attacks against the BFF of `a.example.com`."* If SiteMinder cookies are set on `.desjardins.com`, the BFF must not rely on `SameSite` alone — the antiforgery token becomes load-bearing.
7. **IIS header limits.** Given the ASPX/IIS estate, the 16 KB `MaxRequestBytes` ceiling (§8.1) is a live risk once `SaveTokens = true` adds three JWTs to the cookie *on top of* SiteMinder's own cookies.

### 9.6 What to actually say in the next session

- **Anchor on the standards, not the product.** `draft-ietf-oauth-browser-based-apps-25` is a vendor-neutral IETF BCP-track document that ranks BFF first and says tokens-in-browser is *"not recommended for business applications, sensitive applications, and applications that handle personal data."* Croesus is a wealth-management ISV. That sentence is written for them.
- **De-risk the ask.** Aspire: not required. YARP: not required. Blazor: not required. The BFF pattern is `AddOpenIdConnect` + `AddCookie` + `SaveTokens` + `Authorization: Bearer` on the proxied leg. Roughly 40 lines. Everything else in the article is scaffolding.
- **Lead with the lifecycle fact.** 4.5.2 EOL 26 April 2022, no security fixes.
- **Show, don't tell.** The evidence page (§7.6). Empty `localStorage`, no browser `Authorization` header, live server-held token metadata, side by side.
- **Name the real risks honestly.** The BFF does not stop client hijacking (IETF §6.1.4.1). It stops token theft and silent re-issuance. The forwarded-headers/SiteMinder integration is the genuine engineering risk, not the auth code.

---

## Research Gaps

- [ ] **APIM BFF example-scenario page not retrieved.** [`example-scenario/api-management/backend-for-frontend`](https://learn.microsoft.com/en-us/azure/architecture/example-scenario/api-management/backend-for-frontend) returned *"Failed to extract meaningful content"* on three attempts (bare URL, `?tabs=bff`). §4 is grounded in the APIM worked example inside the [patterns/backends-for-frontends](https://learn.microsoft.com/en-us/azure/architecture/patterns/backends-for-frontends) page, which is fully quotable and sufficient for the architectural comparison — but the dedicated page may contain APIM policy XML worth quoting.
- [ ] **`blazor-web-app-oidc` (the genuinely non-BFF article) not fetched separately.** The task listed it under Q2. Its canonical URL appears to resolve to the `blazor-web-app-with-oidc` article covered here (same `uid: blazor/security/blazor-web-app-oidc`). Worth a direct confirmation pass.
- [ ] **`aspire.dev/get-started/csharp-service-defaults/` not fetched.** `learn.microsoft.com/dotnet/aspire/fundamentals/service-defaults` now 301s to `aspire.dev/fundamentals/service-defaults/`, which itself 302s to `/get-started/csharp-service-defaults/`. The exact `AddServiceDefaults()` body was not captured. Low impact — §6 establishes optionality from the Learn articles' own statements.
- [ ] **`entra/architecture/auth-oauth2` and `entra/identity-platform/v2-protocols-oidc` not fetched.** Listed in the session links. Useful for a protocol-primer appendix if the customer needs OIDC fundamentals rather than BFF specifics.
- [ ] **Tangential links not pursued** (correctly out of scope): `entra/identity-platform/saml-vs-oidc`, `aspnet/core/grpc/`.

---

## Recommended Next Research (not completed)

- [ ] Retrieve the APIM BFF example-scenario page via an alternate route (GitHub source: `MicrosoftDocs/architecture-center` → `docs/example-scenario/api-management/backend-for-frontend.md`) and extract any `validate-jwt` policy XML.
- [ ] Determine whether APIM has *any* first-party OIDC-code-flow-plus-cookie capability (e.g. `get-authorization-context` policy, Credential Manager, `authentication-managed-identity`) that would change the §4.3 conclusion. The current answer is "no", based on absence of evidence rather than an explicit denial.
- [ ] Confirm OWIN/Katana (`Microsoft.Owin.Security.OpenIdConnect`) support status and whether a *partial* BFF on 4.8.1 is defensible as a Phase-1.5 stopgap — this would materially change the §9.4 sequencing if viable.
- [ ] Research SiteMinder ↔ Entra ID federation patterns specifically (SiteMinder as SAML IdP federated to Entra, vs SiteMinder as pure reverse proxy). Determines whether the BFF's IdP is Entra or SiteMinder.
- [ ] Capture the `AddServiceDefaults()` implementation body to quantify exactly what is lost by dropping Aspire (OTel wiring, `AddStandardResilienceHandler`, health-check endpoints).
- [ ] Verify current NuGet versions for `Yarp.ReverseProxy` (2.x) and `Microsoft.AspNetCore.Authentication.OpenIdConnect` on `net10.0`; the §7.3 `csproj` uses wildcards.
- [ ] Investigate `CookieAuthenticationOptions.SessionStore` (server-side ticket store) as the mitigation for §8.1 cookie bloat, given IIS's 16 KB ceiling and Croesus's IIS estate. Includes distributed-cache backing and its interaction with session affinity.
- [ ] Read `poc/modern-net10/Security/*` (`BffAuthenticationSettings`, `TenantPolicy`, `AuthenticationConfigurationStartupValidator`, `OidcRemoteFailureDiagnostic`) in full to determine exactly what needs to change to turn it into the token-forwarding BFF of §7, rather than creating a new project.
- [ ] Confirm whether `api/Croesus.Api.csproj` (`net8.0`) already validates JWT bearer tokens and with which Authority/Audience — determines whether it can serve as the §7.7 downstream cluster destination unmodified.

---

## Clarifying Questions

1. **Is Entra ID the identity provider, or is SiteMinder?** This is the single biggest architectural fork. If SiteMinder federates *to* Entra, the BFF is a standard Entra OIDC client. If SiteMinder is the IdP and injects headers, the BFF is a header-trusting boundary and most of this document's OIDC configuration does not apply as written.
2. **Is the third app additive or a migration?** §7 assumes side-by-side (new project, new Entra registration, zero legacy impact). If Croesus expects an in-place ASPX conversion, sequencing and risk change materially.
3. **Does Croesus already run APIM?** Changes §4 from "explain the difference" to "here is how they compose in your environment."
4. **Does Croesus's `Croesus.Api` (`net8.0`) already do JWT-bearer validation?** Determines whether §7.7's cluster destination works unmodified or needs an `AddJwtBearer` pass first.
5. **Is the customer actually considering Blazor?** Every Microsoft sample is a Blazor Web App, which risks the message landing as "rewrite your frontend in Blazor." The BFF pattern is frontend-agnostic. If Croesus is on React/Angular/ASPX, we should present the BFF as a plain ASP.NET Core minimal-API host with static-file serving — the security properties are identical. Worth confirming before the next session so the demo doesn't get derailed.
6. **Single-instance or multi-instance BFF?** If multi-instance, the shared Data Protection key ring (§2.3) is a hard prerequisite, not a nice-to-have, and should be in the Phase-1 scope.
7. **Expected group/role claim volume per user?** Drives the §8.1 cookie-size decision (client-side session vs server-side ticket store) before any code is written.
8. **Is `poc/modern-net10` a demo artefact or a production candidate?** It is already roughly 80% of the target architecture. If it is production-bound, extending it beats creating `poc/bff-yarp-net10`.
