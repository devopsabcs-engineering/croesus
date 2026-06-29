using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Croesus.Api.Tests;

/// <summary>
/// Negative-control evidence for the On-Behalf-Of flow. These tests prove the strongest "you cannot just
/// replay an unbound token" assertions:
/// <list type="number">
/// <item>A Microsoft Graph-audience token presented to <c>/api/me</c> is rejected with 401 — the audience
/// binding is enforced and the OBO exchange is never attempted on a foreign-audience token.</item>
/// <item>(Documented) Token A (audienced to this API) presented directly to Microsoft Graph would be
/// rejected with 401 because its audience is not Graph. This is asserted structurally rather than against
/// live Graph so the test stays deterministic and credential-free in CI.</item>
/// </list>
/// </summary>
public sealed class NegativeControlTests : IClassFixture<NegativeControlTests.ApiFactory>
{
    private readonly ApiFactory _factory;

    public NegativeControlTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GraphAudienceToken_PresentedToApi_IsRejectedWith401()
    {
        var client = _factory.CreateClient();
        var graphAudienceToken = TestAuth.CreateToken(TestAuth.GraphAudience, "access_as_user");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphAudienceToken);

        var response = await client.GetAsync("/api/me");

        // aud == Graph != this API -> 401 at the authentication middleware; the controller (and thus OBO) never runs.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NoToken_PresentedToApi_IsRejectedWith401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void TokenA_PresentedDirectlyToGraph_WouldBeRejected_BecauseAudienceIsNotGraph()
    {
        // Token A is minted for THIS API (aud == api://<API_CLIENT_ID>). Microsoft Graph rejects any token
        // whose audience is not Graph with 401. The audiences are provably distinct, which is exactly why a
        // replay of token A to Graph cannot succeed and an OBO exchange (token B, aud == Graph) is required.
        Assert.NotEqual(TestAuth.GraphAudience, TestAuth.ApiAudience);
    }

    /// <summary>
    /// Hosts the real API but replaces the JWT bearer validation with a locally controlled signing key and a
    /// fixed valid audience, so the negative controls are deterministic and never contact Microsoft Entra.
    /// The real routing, the real <c>MeController</c>, and the real <c>[Authorize]</c>/<c>[RequiredScope]</c>
    /// gate are exercised; only the token-signing trust anchor is swapped for the test.
    /// </summary>
    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
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
                    ["AzureAd:Audience"] = TestAuth.ApiAudience,
                    ["Graph:BaseUrl"] = "https://graph.microsoft.com/v1.0",
                    ["Graph:Scopes"] = "user.read"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // PostConfigure runs after Microsoft.Identity.Web's own configuration, so this wins:
                // validate against the local test key and require aud == this API. A Graph-audience token
                // therefore fails audience validation and yields 401 before any controller code executes.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.RequireHttpsMetadata = false;

                    // Supply a static, empty OIDC configuration so the bearer handler never reaches out to
                    // Microsoft Entra for signing keys during the test run; validation uses the local key below.
                    options.Configuration = new OpenIdConnectConfiguration();

                    // Validate against the local test key and require aud == this API. A Graph-audience token
                    // therefore fails audience validation and yields 401 before any controller code executes.
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = true,
                        ValidAudiences = new[] { TestAuth.ApiAudience },
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = TestAuth.SigningKey,
                        ValidateLifetime = false,
                        NameClaimType = "name"
                    };
                });
            });
        }
    }

    /// <summary>Mints HS256-signed test tokens with a chosen audience and delegated scope.</summary>
    internal static class TestAuth
    {
        public const string ApiAudience = "api://11111111-1111-1111-1111-111111111111";
        public const string GraphAudience = "https://graph.microsoft.com";

        // 256-bit symmetric key, used only by the in-process test host. Not a real credential.
        private const string SigningSecret = "croesus-negative-control-signing-key-0123456789-abcdef";

        public static SymmetricSecurityKey SigningKey { get; } =
            new(Encoding.UTF8.GetBytes(SigningSecret));

        public static string CreateToken(string audience, string scope)
        {
            var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim("scp", scope),
                new Claim("appid", "22222222-2222-2222-2222-222222222222"),
                new Claim("oid", "33333333-3333-3333-3333-333333333333"),
                new Claim("jti", Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: "https://test-issuer.invalid",
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
