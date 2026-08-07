using System.Net;
using System.Text.Encodings.Web;
using Croesus.ModernBff.Models;
using Croesus.ModernBff.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

IdentityModelEventSource.ShowPII = false;

builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-Croesus.ModernBff.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddSingleton(serviceProvider => BffAuthenticationSettings.Load(
    serviceProvider.GetRequiredService<IConfiguration>(),
    serviceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName));
builder.Services.AddSingleton<TenantPolicy>();
builder.Services.AddHostedService<AuthenticationConfigurationStartupValidator>();

builder.Services.PostConfigure<CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme,
    options =>
    {
        options.Cookie.Name = "__Host-Croesus.ModernBff.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.Cookie.Domain = null;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = false;
    });

builder.Services
    .AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
    .PostConfigure<TenantPolicy>((options, tenantPolicy) =>
    {
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.SaveTokens = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.IssuerValidator = tenantPolicy.ValidateIssuer;

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
                tenantPolicy.ValidatePrincipal(context.Principal);
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Croesus.ModernBff.Authentication")
                    .LogInformation("OIDC token passed issuer and tenant policy validation");
            }
            catch (SecurityTokenValidationException)
            {
                context.Fail("Tenant validation failed.");
            }
        };

        options.Events.OnRemoteFailure = async context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Authentication failed.");
        };
    });

var knownProxies = builder.Configuration
    .GetSection("ForwardedHeaders:KnownProxies")
    .Get<string[]>()
    ?? [];
if (knownProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        foreach (var proxy in knownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    });
}

var app = builder.Build();

if (knownProxies.Length > 0)
{
    app.UseForwardedHeaders();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/", (HttpContext context, IAntiforgery antiforgery) =>
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var displayName = HtmlEncoder.Default.Encode(
            context.User.FindFirst("name")?.Value
            ?? context.User.Identity?.Name
            ?? "Signed-in user");
        var requestToken = HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty);

        return Results.Content(
            $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Croesus modern BFF</title>
            </head>
            <body>
              <main>
                <h1>Croesus modern BFF</h1>
                <p>Signed in as {{displayName}}.</p>
                <p><a href="/api/session">View session metadata</a></p>
                <form method="post" action="/signout">
                  <input type="hidden" name="__RequestVerificationToken" value="{{requestToken}}">
                  <button type="submit">Sign out</button>
                </form>
              </main>
            </body>
            </html>
            """,
            "text/html");
    })
    .RequireAuthorization();

app.MapGet("/api/session", (HttpContext context) =>
        Results.Ok(SessionResponse.FromPrincipal(context.User)))
    .RequireAuthorization();

app.MapPost("/signout", async (HttpContext context, IAntiforgery antiforgery) =>
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        return Results.SignOut(
            authenticationSchemes:
            [
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme
            ]);
    })
    .RequireAuthorization();

app.Run();

public partial class Program;
