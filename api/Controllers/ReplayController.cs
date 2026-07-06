using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Croesus.Api.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace Croesus.Api.Controllers;

/// <summary>
/// Tier 2 negative control — a DELIBERATE server-side token replay, reproduced faithfully so the evidence
/// shows what happens when a client-forwarded Microsoft Graph token is re-presented to Graph by the API
/// (the "replay" anti-pattern) rather than exchanged via On-Behalf-Of.
/// <para>
/// The endpoint is intentionally reversible and gated: it is only mapped when <c>Demo:EnableReplay</c> is
/// true (see <c>Program.cs</c>), so it is absent (404) in the default configuration. Even when enabled, the
/// <see cref="AuthorizeAttribute"/> + <see cref="RequiredScopeAttribute"/> gate requires a valid token
/// audienced to THIS API carrying the <c>access_as_user</c> scope, exactly like <c>MeController</c>.
/// </para>
/// <para>
/// The replay target is HARD-CODED to Microsoft Graph <c>/me</c>; no caller-supplied target is ever honored.
/// The forwarded token is never echoed, returned, or logged: only bounded, non-sensitive claims and the
/// Graph response status leave the process, and every emitted value passes through the redaction guard.
/// </para>
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequiredScope("access_as_user")]
public sealed class ReplayController : ControllerBase
{
    // The replay target is fixed by the server. A caller-supplied target is never read or honored.
    private const string FixedGraphTarget = "https://graph.microsoft.com/v1.0/me";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OboClaimLogger _claimLogger;

    public ReplayController(IHttpClientFactory httpClientFactory, OboClaimLogger claimLogger)
    {
        _httpClientFactory = httpClientFactory;
        _claimLogger = claimLogger;
    }

    /// <summary>Request body for the replay attempt. Only the forwarded Graph token is accepted.</summary>
    public sealed class ReplayRequest
    {
        /// <summary>The Graph-audienced token the client acquired and forwarded to be replayed server-side.</summary>
        public string? GraphToken { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] ReplayRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.GraphToken))
        {
            return BadRequest(new
            {
                error = "missing_token",
                message = "A forwarded Graph token is required in the request body ({ \"graphToken\": \"...\" })."
            });
        }

        // Decode ONLY bounded, non-sensitive claims of the forwarded token for evidence. The raw token is
        // never returned or logged; the decoder mirrors the SPA's decodeJwtClaims contract.
        var claims = DecodeNonSensitiveClaims(request.GraphToken);

        // Deliberately re-present the forwarded token to the FIXED Graph target (the replay anti-pattern).
        // This is exactly the motion an audience-bound / token-protected environment must reject.
        var httpClient = _httpClientFactory.CreateClient();
        using var graphRequest = new HttpRequestMessage(HttpMethod.Get, FixedGraphTarget);
        graphRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.GraphToken);

        int status;
        bool ok;
        try
        {
            using var graphResponse = await httpClient.SendAsync(graphRequest, cancellationToken);
            status = (int)graphResponse.StatusCode;
            ok = graphResponse.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // Network/transport failure reaching Graph. Report as a non-success replay outcome without
            // surfacing exception detail that could leak request material.
            status = 0;
            ok = false;
        }

        var interpretation = Interpret(status, ok);

        // Claims-only evidence. NEVER include the raw token here.
        var evidence = new Dictionary<string, string?>
        {
            ["attemptedTarget"] = FixedGraphTarget,
            ["tokenAudience"] = claims.Audience,
            ["tokenScope"] = claims.Scope,
            ["tokenJti"] = claims.Jti,
            ["tokenIssuedAt"] = claims.IssuedAt,
            ["hasCnf"] = claims.HasCnf ? "true" : "false",
            ["status"] = status.ToString(),
            ["ok"] = ok ? "true" : "false",
            ["interpretation"] = interpretation
        };

        _claimLogger.LogReplayAttempt(evidence);

        return Ok(new
        {
            attemptedTarget = FixedGraphTarget,
            tokenAudience = claims.Audience,
            tokenScope = claims.Scope,
            tokenJti = claims.Jti,
            tokenIssuedAt = claims.IssuedAt,
            hasCnf = claims.HasCnf,
            status,
            ok,
            interpretation
        });
    }

    private static string Interpret(int status, bool ok)
    {
        if (ok)
        {
            return "Microsoft Graph ACCEPTED the replayed token. In this environment the token is not " +
                   "audience-bound to the caller; a hardened (token-protected / CA-1008) configuration would reject it.";
        }

        return status switch
        {
            401 => "Microsoft Graph REJECTED the replayed token with 401 (unauthorized) — the expected outcome " +
                   "when the token is bound and cannot simply be replayed.",
            403 => "Microsoft Graph REJECTED the replayed token with 403 (forbidden) — consistent with a policy " +
                   "or binding control blocking the replay.",
            0 => "The replay call could not reach Microsoft Graph (transport failure); no acceptance occurred.",
            _ => $"Microsoft Graph did not accept the replayed token (status {status})."
        };
    }

    /// <summary>Non-sensitive claim projection of a forwarded token, used only for evidence display.</summary>
    private sealed record NonSensitiveClaims(string? Audience, string? Scope, string? Jti, string? IssuedAt, bool HasCnf);

    /// <summary>
    /// Base64url-decodes and JSON-parses the payload segment of a JWT, returning only a bounded set of
    /// non-sensitive claims (aud, scp, jti, iat, cnf-presence). Never throws in a way that breaks the flow:
    /// any malformed input yields a safe partial object with <c>HasCnf == false</c>. The raw token and full
    /// payload are never returned. Mirrors the SPA <c>decodeJwtClaims</c> contract.
    /// </summary>
    private static NonSensitiveClaims DecodeNonSensitiveClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return new NonSensitiveClaims(null, null, null, null, false);
            }

            var segment = parts[1].Replace('-', '+').Replace('_', '/');
            var padded = segment.PadRight(segment.Length + ((4 - (segment.Length % 4)) % 4), '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            string? aud = root.TryGetProperty("aud", out var audEl) && audEl.ValueKind == JsonValueKind.String
                ? audEl.GetString()
                : null;
            string? scp = root.TryGetProperty("scp", out var scpEl) && scpEl.ValueKind == JsonValueKind.String
                ? scpEl.GetString()
                : null;
            string? jti = root.TryGetProperty("jti", out var jtiEl) && jtiEl.ValueKind == JsonValueKind.String
                ? jtiEl.GetString()
                : null;

            string? issuedAt = null;
            if (root.TryGetProperty("iat", out var iatEl) &&
                iatEl.ValueKind == JsonValueKind.Number &&
                iatEl.TryGetInt64(out var iat))
            {
                issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat).ToString("O");
            }

            var hasCnf = root.TryGetProperty("cnf", out _);

            return new NonSensitiveClaims(aud, scp, jti, issuedAt, hasCnf);
        }
        catch
        {
            return new NonSensitiveClaims(null, null, null, null, false);
        }
    }
}
