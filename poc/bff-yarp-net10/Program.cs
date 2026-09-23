using Croesus.BffYarp.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.TokenCacheProviders;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

IdentityModelEventSource.ShowPII = false;

// The policies are resolved from the container immediately after the host is built, which is the first
// point at which configuration is fully composed. A misconfigured destination or an unmeasured forwarding
// trust set therefore fails the host before it serves a request, not at the first request.
var downstreamScopes = builder.Configuration.GetSection("DownstreamApi:Scopes").Get<string[]>() ?? [];

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(serviceProvider => ForwardedHeadersPolicy.Load(
    serviceProvider.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(serviceProvider => ProxyDestinationPolicy.Load(
    serviceProvider.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(serviceProvider => BffAuthenticationSettings.Load(
    serviceProvider.GetRequiredService<IConfiguration>(),
    serviceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName));
builder.Services.AddSingleton<TenantPolicy>();
builder.Services.AddSingleton<EvidenceCollector>();
builder.Services.AddSingleton<ClaimsChallengeHandler>();
builder.Services.AddSingleton<ClaimsChallengeResponseTransform>();
builder.Services.AddSingleton<ServerTicketStore>();
builder.Services.AddHostedService<AuthenticationConfigurationStartupValidator>();

builder.Services.AddSingleton<IConfigureOptions<ForwardedHeadersOptions>>(serviceProvider =>
    new ConfigureOptions<ForwardedHeadersOptions>(options =>
    {
        var policy = serviceProvider.GetRequiredService<ForwardedHeadersPolicy>();
        if (policy.IsEnabled)
        {
            policy.Apply(options);
        }
    }));

// The ticket store and the MSAL token cache share this backing store. A shared store is what makes the
// session survive a restart and span instances; startup validation refuses the process-local fallback
// outside Development and Poc.
var redisConnectionString = builder.Configuration["DistributedCache:Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "croesus-bff:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("croesus-bff-yarp");
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
    // DPAPI is opt-in because it is wrong for the usual Windows host here: an App Service worker runs without a
    // loaded user profile, so user-scoped DPAPI fails at startup, and machine-scoped keys cannot be read by a
    // second instance sharing the same key ring.
    if (OperatingSystem.IsWindows()
        && builder.Configuration.GetValue("DataProtection:ProtectKeysWithDpapi", defaultValue: false))
    {
        dataProtection.ProtectKeysWithDpapi();
    }
}

builder.Services
    .AddAuthentication(options =>
    {
        // The cookie scheme is the default challenge. An unauthenticated request is answered by this
        // application, which decides between a bounded JSON result and a navigation, rather than being
        // turned into an identity-provider redirect by whichever middleware happened to run first.
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi(downstreamScopes)
    .AddDistributedTokenCaches();

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-Croesus.BffYarp.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.HeaderName = "X-CSRF-TOKEN";
});

builder.Services
    .AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .PostConfigure<ServerTicketStore, BffAuthenticationSettings>((options, ticketStore, settings) =>
    {
        options.Cookie.Name = "__Host-Croesus.BffYarp.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        // Lax, not Strict: the OIDC callback is a cross-site navigation and Strict would drop the cookie on it.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.Cookie.Domain = null;
        options.ExpireTimeSpan = settings.SessionAbsoluteExpiry;
        options.SlidingExpiration = false;
        options.LoginPath = "/bff/login";
        options.AccessDeniedPath = "/bff/login";

        // Server-side custody: the browser receives the opaque key this store returns and nothing else.
        options.SessionStore = ticketStore;

        options.Events.OnRedirectToLogin = context =>
        {
            // A fetch call must never be answered with an identity-provider page.
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/bff"))
            {
                return ClaimsChallengeHandler.WriteInteractionRequiredAsync(context.HttpContext, challengeId: null);
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services
    .AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
    .PostConfigure<TenantPolicy>((options, tenantPolicy) =>
    {
        options.ResponseType = OpenIdConnectResponseType.Code;
        // An authorization code carrying downstream API scopes exceeds the 2048-byte IIS query string limit,
        // so the code arrives in the request body instead of the URL, browser history and proxy logs.
        options.ResponseMode = OpenIdConnectResponseMode.FormPost;
        options.UsePkce = true;
        // Tokens live in the server-side token cache. Saving them here would put them in the browser-held ticket.
        options.SaveTokens = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.IssuerValidator = tenantPolicy.ValidateIssuer;

        var previousRedirect = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            await previousRedirect(context);
            if (context.Properties.Items.TryGetValue("claims", out var claims) && !string.IsNullOrEmpty(claims))
            {
                context.ProtocolMessage.SetParameter("claims", claims);
            }
        };

        var previousTokenValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            await previousTokenValidated(context);
            if (context.Result?.Failure is not null)
            {
                return;
            }

            try
            {
                tenantPolicy.ValidatePrincipal(context.Principal, context.SecurityToken);
            }
            catch (SecurityTokenValidationException)
            {
                context.Fail("Tenant validation failed.");
            }
        };

        options.Events.OnRemoteFailure = async context =>
        {
            // A protocol failure is a client-side protocol error, not a server fault, and its detail is not
            // safe to echo: it can carry state, nonce, and error descriptions from the request.
            context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Croesus.BffYarp.Authentication")
                .LogWarning(
                    "OIDC remote authentication failed ({ExceptionType}).",
                    context.Failure?.GetType().Name ?? "Unknown");

            context.HandleResponse();
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "authentication_failed" });
        };
    });

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
    {
        // The browser Host header never reaches the owned API, and the forwarding metadata is regenerated
        // from the measured connection rather than appended to whatever the caller supplied.
        context.AddOriginalHost(false);
        context.AddXForwarded(ForwardedTransformActions.Set);
        context.RequestTransforms.Add(new AccessTokenTransform());
        context.ResponseTransforms.Add(context.Services.GetRequiredService<ClaimsChallengeResponseTransform>());
    });

var app = builder.Build();

app.Services.GetRequiredService<BffAuthenticationSettings>();
app.Services.GetRequiredService<ProxyDestinationPolicy>();
var forwardedHeadersPolicy = app.Services.GetRequiredService<ForwardedHeadersPolicy>();

if (forwardedHeadersPolicy.IsEnabled)
{
    app.UseForwardedHeaders();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseMiddleware<ProxyBoundaryMiddleware>();

app.MapReverseProxy().RequireAuthorization();

app.MapGet("/bff/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { requestToken = tokens.RequestToken, headerName = tokens.HeaderName });
    })
    .RequireAuthorization();

app.MapGet("/bff/evidence", (HttpContext context, EvidenceCollector evidence) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(evidence.Project(context.User));
    })
    .RequireAuthorization();

app.MapGet("/bff/login", async (
        HttpContext context,
        ClaimsChallengeHandler challengeHandler,
        string? challengeId,
        string? returnUrl) =>
    {
        var properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            // Only a local path is ever a redirect target, so a challenge cannot be used as an open redirect.
            RedirectUri = returnUrl is not null && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                ? returnUrl
                : "/"
        };

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claims = await challengeHandler.RedeemChallengeAsync(context.User, challengeId);
            if (claims is not null)
            {
                properties.Items["claims"] = claims;
            }
        }

        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    })
    .AllowAnonymous();

app.MapPost("/bff/logout", async (
        HttpContext context,
        IAntiforgery antiforgery,
        IMsalTokenCacheProvider tokenCacheProvider,
        ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("Croesus.BffYarp.Logout");
        if (!await ProxyBoundaryMiddleware.ValidateAntiforgeryAsync(context, antiforgery, logger))
        {
            return Results.BadRequest(new { error = "antiforgery_validation_failed" });
        }

        var accountId = context.User.GetMsalAccountId();
        if (!string.IsNullOrEmpty(accountId))
        {
            await tokenCacheProvider.ClearAsync(accountId);
        }

        // Signing out the cookie scheme removes the server-side ticket through the session store, so a
        // captured cookie no longer resolves to a session. Identity-provider sign-out is a separate decision
        // and is not performed here.
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    })
    .RequireAuthorization();

app.Run();

public partial class Program;
