using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Yarp.ReverseProxy.Forwarder;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// Hosts the real application. Only three seams are replaced: the owned API is a recording handler, token
/// acquisition is a deterministic stand-in, and a test-only sign-in path drives the genuine cookie handler
/// and the genuine ticket store. Everything being asserted, including the cookie configuration, the proxy
/// boundary, and the evidence projection, is the shipped code.
/// </summary>
public class BffFactory : WebApplicationFactory<Program>
{
    internal RecordingDownstream Downstream { get; } = new();

    internal FakeTokenAcquisition TokenAcquisition { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(ConfigurationValues()));

        // The Windows event-log provider throws once a previous test host has disposed it, and a logger
        // fault surfaces as the authentication failure, masking the real one. Tests need the real one.
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IForwarderHttpClientFactory>(new RecordingForwarderHttpClientFactory(Downstream));
            services.AddSingleton<ITokenAcquisition>(TokenAcquisition);
            services.AddSingleton<IStartupFilter, TestSignInStartupFilter>();
            ConfigureAdditionalTestServices(services);
        });
    }

    protected virtual Dictionary<string, string?> ConfigurationValues() => TestConfiguration.Values();

    protected virtual void ConfigureAdditionalTestServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// An HTTPS base address. The session and antiforgery cookies are <c>Secure</c>, so a plain-HTTP client
    /// would silently drop them and every session assertion would be meaningless.
    /// </summary>
    public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}

/// <summary>
/// Adds a test-only sign-in path ahead of the application pipeline. It calls the real cookie handler, so the
/// resulting cookie, ticket store entry, and authentication properties are produced by shipped code.
/// </summary>
internal sealed class TestSignInStartupFilter : IStartupFilter
{
    public const string SignInPath = "/__test/signin";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (!context.Request.Path.Equals(SignInPath, StringComparison.Ordinal))
            {
                await nextMiddleware();
                return;
            }
            var identity = new ClaimsIdentity(
                [
                    new Claim("sub", TestConfiguration.ObjectId),
                    new Claim("oid", TestConfiguration.ObjectId),
                    new Claim("tid", TestConfiguration.TenantId),
                    new Claim("sid", context.Request.Query["sid"].FirstOrDefault() ?? "synthetic-session-identifier"),
                    new Claim("name", "Test User"),
                    new Claim("amr", "pwd"),
                    new Claim("amr", "mfa"),
                    new Claim("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
                ],
                authenticationType: "TestOidc",
                nameType: "name",
                roleType: "roles");

            var properties = new AuthenticationProperties();

            // Deliberately adversarial: tokens are placed in the properties exactly as SaveTokens = true would.
            // The ticket store is what keeps them off the browser, so this is the condition worth proving.
            properties.StoreTokens(
            [
                new AuthenticationToken { Name = "access_token", Value = TestConfiguration.SentinelSecret },
                new AuthenticationToken { Name = "refresh_token", Value = TestConfiguration.SentinelSecret }
            ]);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                properties);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        next(app);
    };
}
