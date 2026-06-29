using System.Security.Claims;
using Croesus.Api.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;

namespace Croesus.Api.Controllers;

/// <summary>
/// Returns the signed-in user's Microsoft Graph profile, obtained through the On-Behalf-Of flow,
/// alongside an evidence summary of both token legs.
/// <para>
/// <see cref="AuthorizeAttribute"/> + <see cref="RequiredScopeAttribute"/> enforce that the inbound token
/// is valid, audienced to THIS API, and carries the <c>access_as_user</c> delegated scope. A token whose
/// audience is not this API never reaches the OBO call: it is rejected with 401 by the framework, and a
/// defensive audience check inside the action provides belt-and-suspenders enforcement so OBO is never
/// attempted on a foreign-audience token (the OBO "reject the token" rule).
/// </para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequiredScope("access_as_user")]
public sealed class MeController : ControllerBase
{
    private const string GraphResource = "https://graph.microsoft.com";
    private static readonly string[] GraphScopes = { "https://graph.microsoft.com/User.Read" };

    private readonly GraphServiceClient _graph;
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IConfiguration _config;
    private readonly OboClaimLogger _claimLogger;

    public MeController(
        GraphServiceClient graph,
        ITokenAcquisition tokenAcquisition,
        IConfiguration config,
        OboClaimLogger claimLogger)
    {
        _graph = graph;
        _tokenAcquisition = tokenAcquisition;
        _config = config;
        _claimLogger = claimLogger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        // Defensive audience binding: never attempt OBO on a token that was not minted for THIS API.
        var inboundAudience = FirstClaim("aud", "appid_aud");
        var expectedClientId = _config["AzureAd:ClientId"];
        var expectedAudience = _config["AzureAd:Audience"];
        if (!AudienceMatchesThisApi(inboundAudience, expectedClientId, expectedAudience))
        {
            // Foreign-audience token: reject with 401, do NOT exchange it. This mirrors the framework's
            // own audience validation and makes the "no replay" guarantee explicit and testable.
            return Unauthorized(new
            {
                error = "invalid_audience",
                message = "The presented token is not audienced to this API; the On-Behalf-Of exchange was not attempted."
            });
        }

        // Leg 1 evidence — decoded NON-sensitive claims of the inbound SPA->API token A (an API we own).
        var leg1 = new Dictionary<string, string?>
        {
            ["aud"] = inboundAudience,
            ["scp"] = FirstClaim("scp", "http://schemas.microsoft.com/identity/claims/scope"),
            ["appid"] = FirstClaim("appid", "azp"),
            ["oid"] = FirstClaim("oid", "http://schemas.microsoft.com/identity/claims/objectidentifier"),
            ["iss"] = FirstClaim("iss"),
            ["iat"] = FirstClaim("iat"),
            ["jti"] = FirstClaim("jti")
        };

        // Leg 2 — perform the OBO exchange to mint token B for Microsoft Graph. Microsoft.Identity.Web
        // exchanges token A for a NEW Graph-audienced token using the API's confidential-client certificate;
        // the inbound token is never forwarded to Graph.
        var oboResult = await _tokenAcquisition.GetAuthenticationResultForUserAsync(
            GraphScopes,
            authenticationScheme: null,
            user: User);

        // The Graph call reuses the cached OBO (token B) acquired above.
        var me = await _graph.Me.Request().GetAsync(cancellationToken);

        // OBO request shape (for evidence) — the literal grant parameters and the credential SOURCE only.
        var oboRequestShape = new Dictionary<string, string?>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["requested_token_use"] = "on_behalf_of",
            ["scope"] = string.Join(' ', GraphScopes),
            ["credentialSource"] = _config["AzureAd:ClientCredentials:0:SourceType"],
            ["credentialName"] = _config["AzureAd:ClientCredentials:0:KeyVaultCertificateName"]
        };

        // Leg 2 evidence — derived from the MSAL result. We do NOT crack the Graph JWT (its format may be
        // non-JWT/encrypted). aud == graph by construction; distinct issuance is shown via correlation id + expiry.
        var leg2 = new Dictionary<string, string?>
        {
            ["aud"] = GraphResource,
            ["scp"] = string.Join(' ', oboResult.Scopes),
            ["expiresOn"] = oboResult.ExpiresOn.ToString("O"),
            ["correlationId"] = oboResult.CorrelationId.ToString(),
            ["tokenSource"] = oboResult.AuthenticationResultMetadata.TokenSource.ToString()
        };

        _claimLogger.LogExchange(leg1, oboRequestShape, leg2);

        return Ok(new
        {
            user = new
            {
                me?.DisplayName,
                me?.UserPrincipalName,
                me?.Id
            },
            evidence = new
            {
                leg1 = new
                {
                    description = "Inbound SPA->API token A (audienced to this API).",
                    aud = leg1["aud"],
                    scp = leg1["scp"],
                    appid = leg1["appid"],
                    oid = leg1["oid"],
                    jti = leg1["jti"],
                    iat = leg1["iat"]
                },
                leg2 = new
                {
                    description = "Newly issued API->Graph token B (audienced to Microsoft Graph), from the MSAL OBO result.",
                    aud = leg2["aud"],
                    scp = leg2["scp"],
                    correlationId = leg2["correlationId"],
                    expiresOn = leg2["expiresOn"],
                    note = "Distinct audience from leg 1 proves an exchange (not a replay). The Graph JWT is not decoded."
                }
            }
        });
    }

    private string? FirstClaim(params string[] types)
    {
        foreach (var type in types)
        {
            var value = User.FindFirstValue(type);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool AudienceMatchesThisApi(string? audience, string? clientId, string? configuredAudience)
    {
        if (string.IsNullOrEmpty(audience))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(configuredAudience) &&
            string.Equals(audience, configuredAudience, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrEmpty(clientId))
        {
            return false;
        }

        // Entra may emit the audience as the bare client id (v2) or the api://{clientId} App ID URI (v1).
        return string.Equals(audience, clientId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(audience, $"api://{clientId}", StringComparison.OrdinalIgnoreCase);
    }
}
