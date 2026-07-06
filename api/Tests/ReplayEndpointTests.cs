using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Croesus.Api.Tests;

/// <summary>
/// Tests for the reversible Tier 2 token-replay endpoint (<c>POST /api/replay</c>). They prove:
/// <list type="number">
/// <item>With <c>Demo:EnableReplay=false</c> the route is ABSENT (404) — the gate is truly reversible.</item>
/// <item>With the gate on, an unauthenticated request is rejected with 401 by the auth middleware.</item>
/// <item>With a valid API-audience token, neither the response body nor any captured log contains a
/// JWT-shaped substring or the forwarded token — redaction holds end to end.</item>
/// <item>A Graph-audience token presented as the bearer is rejected with 401 by audience validation.</item>
/// </list>
/// The Graph call is stubbed so tests are deterministic and never touch the network. Token minting and the
/// symmetric trust anchor are reused from <see cref="NegativeControlTests.TestAuth"/>. The two host factories
/// are shared class fixtures (constructed once, disposed once), mirroring <see cref="NegativeControlTests"/>:
/// this avoids per-test host disposal poisoning the shared IdentityModel crypto state.
/// </summary>
public sealed class ReplayEndpointTests :
    IClassFixture<ReplayEndpointTests.ReplayEnabledFactory>,
    IClassFixture<ReplayEndpointTests.ReplayDisabledFactory>
{
    private readonly ReplayEnabledFactory _enabled;
    private readonly ReplayDisabledFactory _disabled;

    public ReplayEndpointTests(ReplayEnabledFactory enabled, ReplayDisabledFactory disabled)
    {
        _enabled = enabled;
        _disabled = disabled;
    }

    [Fact]
    public async Task ReplayDisabled_PostReplay_ReturnsNotFound()
    {
        var client = _disabled.CreateClient();
        var apiToken = NegativeControlTests.TestAuth.CreateToken(
            NegativeControlTests.TestAuth.ApiAudience, "access_as_user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        var response = await client.PostAsJsonAsync("/api/replay", new { graphToken = "any" });

        // Gate off -> ReplayController is not in the MVC model -> route unmapped -> 404 (not 401/405).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReplayEnabled_NoToken_ReturnsUnauthorized()
    {
        var client = _enabled.CreateClient();

        var response = await client.PostAsJsonAsync("/api/replay", new { graphToken = "any" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReplayEnabled_ValidApiToken_ResponseAndLogsContainNoRawToken()
    {
        var client = _enabled.CreateClient();

        var apiToken = NegativeControlTests.TestAuth.CreateToken(
            NegativeControlTests.TestAuth.ApiAudience, "access_as_user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        // The forwarded token being "replayed" is itself a JWT-shaped string; it must never appear in output.
        var forwardedGraphToken = NegativeControlTests.TestAuth.CreateToken(
            NegativeControlTests.TestAuth.GraphAudience, "User.Read");

        var response = await client.PostAsJsonAsync("/api/replay", new { graphToken = forwardedGraphToken });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The forwarded token must not be echoed, and no JWT-shaped substring may appear in the body.
        Assert.DoesNotContain(forwardedGraphToken, body);
        Assert.False(JwtShaped.IsMatch(body), "Response body contains a JWT-shaped substring.");

        // Any captured log line must likewise be free of the forwarded token and of any JWT-shaped substring.
        foreach (var message in _enabled.LogMessages)
        {
            Assert.DoesNotContain(forwardedGraphToken, message);
            Assert.False(JwtShaped.IsMatch(message), $"A log line contains a JWT-shaped substring: {message}");
        }
    }

    [Fact]
    public async Task ReplayEnabled_GraphAudienceToken_IsRejectedWith401()
    {
        var client = _enabled.CreateClient();
        var graphAudienceToken = NegativeControlTests.TestAuth.CreateToken(
            NegativeControlTests.TestAuth.GraphAudience, "access_as_user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAudienceToken);

        var response = await client.PostAsJsonAsync("/api/replay", new { graphToken = "any" });

        // aud == Graph != this API -> 401 at the authentication middleware; the controller never runs.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Detects a real base64url JWT (three segments, each long as in a genuine token: header ~36, payload
    // 100+, signature ~43 chars). A minimum of 24 chars per segment avoids false positives on dotted type
    // names in framework log noise (e.g. "Microsoft.IdentityModel.Protocols") while still matching any real
    // access token, so the redaction assertions test leakage rather than incidental dots.
    private static readonly Regex JwtShaped = new(
        @"[A-Za-z0-9_-]{24,}\.[A-Za-z0-9_-]{24,}\.[A-Za-z0-9_-]{24,}",
        RegexOptions.Compiled);

    /// <summary>Shared class fixture: the API with the replay gate ON and the Graph call stubbed.</summary>
    public sealed class ReplayEnabledFactory : ReplayApiFactoryBase
    {
        public ReplayEnabledFactory() : base(enableReplay: true)
        {
        }
    }

    /// <summary>Shared class fixture: the API with the replay gate OFF (route absent).</summary>
    public sealed class ReplayDisabledFactory : ReplayApiFactoryBase
    {
        public ReplayDisabledFactory() : base(enableReplay: false)
        {
        }
    }

    /// <summary>
    /// Hosts the real API with the JWT bearer trust anchor swapped for a local symmetric key (mirroring
    /// <see cref="NegativeControlTests"/>), toggles <c>Demo:EnableReplay</c>, and stubs the outbound Graph
    /// HttpClient so the replay call returns a canned 401 without any network I/O. Captured log messages are
    /// exposed via <see cref="LogMessages"/> for redaction assertions.
    /// </summary>
    public abstract class ReplayApiFactoryBase : WebApplicationFactory<Program>
    {
        private readonly bool _enableReplay;
        private readonly CapturingLoggerProvider _logProvider = new();

        protected ReplayApiFactoryBase(bool enableReplay) => _enableReplay = enableReplay;

        /// <summary>Formatted log messages captured across this host's lifetime.</summary>
        public IReadOnlyCollection<string> LogMessages => _logProvider.Messages;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                    ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000000",
                    ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
                    ["AzureAd:Audience"] = NegativeControlTests.TestAuth.ApiAudience,
                    ["Graph:BaseUrl"] = "https://graph.microsoft.com/v1.0",
                    ["Graph:Scopes"] = "user.read",
                    ["Demo:EnableReplay"] = _enableReplay ? "true" : "false"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.RequireHttpsMetadata = false;

                    // Pin a static (empty) OIDC configuration so the bearer handler never performs a network
                    // metadata fetch to Entra. Validation then relies solely on the local key below, making the
                    // successful-auth path deterministic (the negative controls never exercised a success).
                    var staticConfig = new OpenIdConnectConfiguration();
                    options.Configuration = staticConfig;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(staticConfig);

                    // Validate against a key with its OWN crypto provider factory so this host's signature
                    // providers are cached independently of the process-wide default cache — cross-class host
                    // disposal cannot dispose a provider this host still relies on.
                    var validationKey = new SymmetricSecurityKey(NegativeControlTests.TestAuth.SigningKey.Key)
                    {
                        CryptoProviderFactory = new CryptoProviderFactory()
                    };

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = true,
                        ValidAudiences = new[] { NegativeControlTests.TestAuth.ApiAudience },
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = validationKey,
                        ValidateLifetime = false,
                        NameClaimType = "name"
                    };
                });

                // Stub the default HttpClient so the replay's Graph call is deterministic (canned 401, no network).
                services.AddHttpClient(string.Empty)
                    .ConfigurePrimaryHttpMessageHandler(() => new StubGraphHandler());

                services.AddLogging(logging => logging.AddProvider(_logProvider));
            });
        }
    }

    /// <summary>Returns a canned 401 for any request, standing in for Microsoft Graph rejecting the replay.</summary>
    private sealed class StubGraphHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }

    /// <summary>Captures formatted log messages so tests can assert redaction across the logging path.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _messages;

            public CapturingLogger(ConcurrentQueue<string> messages) => _messages = messages;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _messages.Enqueue(formatter(state, exception));
        }
    }
}
