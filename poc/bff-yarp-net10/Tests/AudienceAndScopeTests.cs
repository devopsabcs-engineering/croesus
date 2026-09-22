using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// Stands in for the owned API as a real resource server: a genuine <c>JwtBearer</c> handler validating a
/// genuine signed token against a genuine authorization policy. Only the trust anchor is local, so no test
/// in this file reaches Microsoft Entra, needs a tenant, or needs a registration.
/// <para>
/// The two controls under test can each be removed independently. <paramref name="validateAudience"/>
/// switches off audience validation and <paramref name="requireScope"/> switches off the delegated-scope
/// requirement. That is what lets the negative tests below prove a rejection was <em>caused</em> by the
/// control rather than by the host refusing everything.
/// </para>
/// </summary>
internal sealed class OwnedApiResourceServer : IAsyncDisposable
{
    public const string OwnedApiAudience = "api://bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    /// <summary>A different resource entirely. A token minted for this must never open the owned API.</summary>
    public const string ForeignAudience = "https://graph.microsoft.com";

    public const string RequiredScope = "access_as_user";
    public const string ResourcePath = "/api/resource";

    private const string ScopePolicy = "owned-api-delegated-scope";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("owned-api-synthetic-signing-key-material-0123456789"));

    private readonly IHost _host;
    private int _resourceInvocations;

    private OwnedApiResourceServer(IHost host) => _host = host;

    /// <summary>How many times the protected endpoint actually ran. A rejected call must leave this at zero.</summary>
    public int ResourceInvocations => Volatile.Read(ref _resourceInvocations);

    public static async Task<OwnedApiResourceServer> StartAsync(
        bool validateAudience = true,
        bool requireScope = true)
    {
        OwnedApiResourceServer? server = null;

        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services
                        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                        .AddJwtBearer(options =>
                        {
                            options.RequireHttpsMetadata = false;
                            // A static configuration keeps the handler from reaching for signing keys over
                            // the network; validation uses the local key below and nothing else.
                            options.Configuration = new OpenIdConnectConfiguration();
                            // Leave "scp" as "scp" so the scope policy reads the claim the token carries.
                            options.MapInboundClaims = false;
                            options.TokenValidationParameters = new TokenValidationParameters
                            {
                                ValidateIssuer = false,
                                ValidateAudience = validateAudience,
                                ValidAudiences = [OwnedApiAudience],
                                ValidateIssuerSigningKey = true,
                                IssuerSigningKey = SigningKey,
                                ValidateLifetime = true,
                                ClockSkew = TimeSpan.Zero
                            };
                        });

                    services.AddAuthorizationBuilder()
                        .AddPolicy(ScopePolicy, policy =>
                        {
                            policy.RequireAuthenticatedUser();
                            if (requireScope)
                            {
                                // "scp" is space-delimited, so a substring match would admit "access_as_userx".
                                policy.RequireAssertion(context =>
                                    context.User.FindFirst("scp")?.Value
                                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                        .Contains(RequiredScope, StringComparer.Ordinal) == true);
                            }
                        });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet(ResourcePath, (HttpContext context) =>
                        {
                            Interlocked.Increment(ref server!._resourceInvocations);
                            return Results.Ok(new { subject = context.User.FindFirst("sub")?.Value });
                        })
                        .RequireAuthorization(ScopePolicy));
                }))
            .StartAsync();

        server = new OwnedApiResourceServer(host);
        return server;
    }

    public HttpClient CreateClient() => _host.GetTestClient();

    /// <summary>Mints a synthetic, locally signed token. Nothing here is, or resembles, a real credential.</summary>
    public static string MintToken(string audience, string? scope, TimeSpan? lifetime = null)
    {
        var now = DateTime.UtcNow;
        List<Claim> claims =
        [
            new Claim("sub", TestConfiguration.ObjectId),
            new Claim("oid", TestConfiguration.ObjectId),
            new Claim("tid", TestConfiguration.TenantId)
        ];

        if (scope is not null)
        {
            claims.Add(new Claim("scp", scope));
        }

        // A negative lifetime mints an already-expired token, so "not before" is anchored to the expiry
        // rather than to now; otherwise nbf would fall after exp and the token could not be constructed.
        var expires = now.Add(lifetime ?? TimeSpan.FromMinutes(10));
        var notBefore = (expires < now ? expires : now).AddMinutes(-1);

        var token = new JwtSecurityToken(
            issuer: TestConfiguration.Issuer,
            audience: audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>
/// Step 3.6 evidence: the owned API binds a token to its own audience and to a delegated scope, and the two
/// failures are told apart.
/// <para>
/// Each negative test is paired with a control-removal test that switches the relevant check off and shows
/// the very same token is then admitted. That pairing is the point. Without it, a 401 could equally mean
/// "audience validation rejected this token" or "this host rejects everything", and the test would be
/// evidence of nothing. Both control-removal tests below are executed on every run; neither is a claim made
/// on the strength of a configuration flag.
/// </para>
/// </summary>
public sealed class AudienceAndScopeTests
{
    [Fact]
    public async Task TokenForADifferentAudience_IsRejectedByTheOwnedApi()
    {
        await using var ownedApi = await OwnedApiResourceServer.StartAsync();
        var client = ownedApi.CreateClient();
        var foreignToken = OwnedApiResourceServer.MintToken(
            OwnedApiResourceServer.ForeignAudience,
            OwnedApiResourceServer.RequiredScope);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignToken);

        var response = await client.GetAsync(OwnedApiResourceServer.ResourcePath);

        // Rejected, not merely logged: the status is 401 and the endpoint never ran.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, ownedApi.ResourceInvocations);

        // The challenge names the audience as the cause, so the rejection is attributable to this control.
        var challenge = string.Join(' ', response.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.Contains("invalid_token", challenge, StringComparison.Ordinal);
        Assert.Contains("audience", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithAudienceValidationRemoved_TheSameForeignAudienceTokenIsAdmitted()
    {
        // Control removed. If this still returned 401, the rejection above would not be evidence about
        // audience validation, and this test would fail.
        await using var ownedApi = await OwnedApiResourceServer.StartAsync(validateAudience: false);
        var client = ownedApi.CreateClient();
        var foreignToken = OwnedApiResourceServer.MintToken(
            OwnedApiResourceServer.ForeignAudience,
            OwnedApiResourceServer.RequiredScope);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignToken);

        var response = await client.GetAsync(OwnedApiResourceServer.ResourcePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, ownedApi.ResourceInvocations);
    }

    [Fact]
    public async Task TokenWithoutTheRequiredScope_IsRejectedAsAnAuthorizationFailure()
    {
        await using var ownedApi = await OwnedApiResourceServer.StartAsync();
        var client = ownedApi.CreateClient();
        var scopelessToken = OwnedApiResourceServer.MintToken(
            OwnedApiResourceServer.OwnedApiAudience,
            scope: null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopelessToken);

        var response = await client.GetAsync(OwnedApiResourceServer.ResourcePath);

        // 403-class, and asserted to be distinct from the 401-class authentication failure. The caller is
        // authenticated; it is the delegated permission that is missing.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, ownedApi.ResourceInvocations);
    }

    [Fact]
    public async Task TokenWithAnAdjacentButDifferentScope_IsRejectedAsAnAuthorizationFailure()
    {
        await using var ownedApi = await OwnedApiResourceServer.StartAsync();
        var client = ownedApi.CreateClient();
        // A prefix of the required scope. A substring check would wrongly admit this.
        var wrongScopeToken = OwnedApiResourceServer.MintToken(
            OwnedApiResourceServer.OwnedApiAudience,
            "access_as_user_readonly");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongScopeToken);

        var response = await client.GetAsync(OwnedApiResourceServer.ResourcePath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, ownedApi.ResourceInvocations);
    }

    [Fact]
    public async Task WithTheScopeRequirementRemoved_TheSameScopelessTokenIsAdmitted()
    {
        // Control removed. This is what makes the 403 above attributable to the scope requirement rather
        // than to the authorization pipeline denying by default.
        await using var ownedApi = await OwnedApiResourceServer.StartAsync(requireScope: false);
        var client = ownedApi.CreateClient();
        var scopelessToken = OwnedApiResourceServer.MintToken(
            OwnedApiResourceServer.OwnedApiAudience,
            scope: null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopelessToken);

        var response = await client.GetAsync(OwnedApiResourceServer.ResourcePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, ownedApi.ResourceInvocations);
    }

    [Fact]
    public async Task NoToken_IsRejectedAsAnAuthenticationFailure()
    {
        await using var ownedApi = await OwnedApiResourceServer.StartAsync();
        var client = ownedApi.CreateClient();

        var response = await client.GetAsync(OwnedApiResourceServer.ResourcePath);

        // Anchors the 401-class end of the distinction asserted in the scope tests above.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, ownedApi.ResourceInvocations);
    }

    [Fact]
    public async Task TokenWithTheCorrectAudienceAndScope_IsAccepted()
    {
        await using var ownedApi = await OwnedApiResourceServer.StartAsync();
        var client = ownedApi.CreateClient();
        var ownedApiToken = OwnedApiResourceServer.MintToken(
            OwnedApiResourceServer.OwnedApiAudience,
            OwnedApiResourceServer.RequiredScope);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownedApiToken);

        var response = await client.GetAsync(OwnedApiResourceServer.ResourcePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, ownedApi.ResourceInvocations);
        // The admitted principal is the one the token carried, so the handler validated rather than waved through.
        Assert.Contains(TestConfiguration.ObjectId, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredTokenForTheOwnedApiAudience_IsRejected()
    {
        await using var ownedApi = await OwnedApiResourceServer.StartAsync();
        var client = ownedApi.CreateClient();
        var expiredToken = OwnedApiResourceServer.MintToken(
            OwnedApiResourceServer.OwnedApiAudience,
            OwnedApiResourceServer.RequiredScope,
            lifetime: TimeSpan.FromMinutes(-5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync(OwnedApiResourceServer.ResourcePath);

        // A correctly audienced token still fails once it has expired, so acceptance above was not blanket.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, ownedApi.ResourceInvocations);
    }

    [Fact]
    public void TheOwnedApiAudience_IsProvablyDistinctFromTheForeignAudience()
    {
        // Structural fact underpinning the rejection tests: the two audience values are not the same string.
        Assert.NotEqual(OwnedApiResourceServer.ForeignAudience, OwnedApiResourceServer.OwnedApiAudience);
    }
}
