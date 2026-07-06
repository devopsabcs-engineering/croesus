using System.Text.RegularExpressions;
using Microsoft.ApplicationInsights;

namespace Croesus.Api.Telemetry;

/// <summary>
/// Logs the decoded, NON-sensitive claims of both On-Behalf-Of legs to Application Insights as
/// structured evidence that the flow is "bound, not replayed":
/// <list type="bullet">
/// <item>Leg 1 (inbound SPA→API token A): <c>aud</c>, <c>scp</c>, <c>appid</c>, <c>oid</c>, <c>iss</c>, <c>iat</c>, <c>jti</c>.</item>
/// <item>OBO request shape: <c>grant_type=jwt-bearer</c>, <c>requested_token_use=on_behalf_of</c>, target Graph scope,
/// and the credential SOURCE only (Key Vault certificate name / thumbprint / kid) — never the credential material.</item>
/// <item>Leg 2 (outbound API→Graph token B): <c>aud == graph</c>, granted scopes, and a distinct issuance signal
/// (correlation id + expiry) derived from the MSAL result. The Graph JWT itself is never cracked.</item>
/// </list>
/// A redaction guard guarantees that a raw token string can never be written, even by accident.
/// </summary>
public sealed class OboClaimLogger
{
    // A JWT is three base64url segments separated by dots. Any value matching this shape is redacted.
    private static readonly Regex JwtShaped = new(
        @"^[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}$",
        RegexOptions.Compiled);

    private readonly ILogger<OboClaimLogger> _logger;
    private readonly TelemetryClient? _telemetry;

    public OboClaimLogger(ILogger<OboClaimLogger> logger, TelemetryClient? telemetry = null)
    {
        _logger = logger;
        _telemetry = telemetry;
    }

    /// <summary>
    /// Records a single OBO exchange. All values pass through <see cref="Redact"/> before they leave the process,
    /// so a token-shaped string is replaced with a placeholder rather than logged.
    /// </summary>
    public void LogExchange(
        IReadOnlyDictionary<string, string?> leg1Inbound,
        IReadOnlyDictionary<string, string?> oboRequestShape,
        IReadOnlyDictionary<string, string?> leg2Outbound)
    {
        var properties = new Dictionary<string, string>();
        foreach (var (key, value) in leg1Inbound)
        {
            properties[$"leg1.{key}"] = Redact(value);
        }

        foreach (var (key, value) in oboRequestShape)
        {
            properties[$"obo.{key}"] = Redact(value);
        }

        foreach (var (key, value) in leg2Outbound)
        {
            properties[$"leg2.{key}"] = Redact(value);
        }

        _telemetry?.TrackEvent("OboExchange", properties);

        _logger.LogInformation(
            "OBO exchange: leg1.aud={Leg1Aud} leg1.scp={Leg1Scp} leg1.appid={Leg1AppId} leg1.jti={Leg1Jti} " +
            "obo.grant_type={Grant} obo.requested_token_use={Use} obo.credentialSource={CredSource} " +
            "leg2.aud={Leg2Aud} leg2.scp={Leg2Scp} leg2.correlationId={Leg2Corr}",
            Redact(GetOrEmpty(leg1Inbound, "aud")),
            Redact(GetOrEmpty(leg1Inbound, "scp")),
            Redact(GetOrEmpty(leg1Inbound, "appid")),
            Redact(GetOrEmpty(leg1Inbound, "jti")),
            Redact(GetOrEmpty(oboRequestShape, "grant_type")),
            Redact(GetOrEmpty(oboRequestShape, "requested_token_use")),
            Redact(GetOrEmpty(oboRequestShape, "credentialSource")),
            Redact(GetOrEmpty(leg2Outbound, "aud")),
            Redact(GetOrEmpty(leg2Outbound, "scp")),
            Redact(GetOrEmpty(leg2Outbound, "correlationId")));
    }

    /// <summary>
    /// Records a single deliberate token-replay attempt (Tier 2 negative control). The server re-presents a
    /// client-forwarded Graph token to a fixed target and captures the claims-only outcome. All values pass
    /// through <see cref="Redact"/>, so the forwarded token — or any token-shaped value — is never logged.
    /// Emitted as the distinct <c>ReplayAttempt</c> event so replay evidence queries stay separate from the
    /// legitimate <c>OboExchange</c> path, which is left unchanged.
    /// </summary>
    public void LogReplayAttempt(IReadOnlyDictionary<string, string?> replayEvidence)
    {
        var properties = new Dictionary<string, string>();
        foreach (var (key, value) in replayEvidence)
        {
            properties[$"replay.{key}"] = Redact(value);
        }

        _telemetry?.TrackEvent("ReplayAttempt", properties);

        _logger.LogInformation(
            "Replay attempt: replay.attemptedTarget={Target} replay.tokenAudience={Aud} replay.tokenScope={Scp} " +
            "replay.tokenJti={Jti} replay.hasCnf={HasCnf} replay.status={Status} replay.ok={Ok}",
            Redact(GetOrEmpty(replayEvidence, "attemptedTarget")),
            Redact(GetOrEmpty(replayEvidence, "tokenAudience")),
            Redact(GetOrEmpty(replayEvidence, "tokenScope")),
            Redact(GetOrEmpty(replayEvidence, "tokenJti")),
            Redact(GetOrEmpty(replayEvidence, "hasCnf")),
            Redact(GetOrEmpty(replayEvidence, "status")),
            Redact(GetOrEmpty(replayEvidence, "ok")));
    }

    /// <summary>
    /// Returns the value unchanged for ordinary claim values, but replaces any raw-token-shaped string
    /// (a three-part base64url JWT) with a placeholder. This is the last line of defence ensuring no raw
    /// access token is ever emitted to logs.
    /// </summary>
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return JwtShaped.IsMatch(value) ? "[REDACTED:token]" : value;
    }

    private static string? GetOrEmpty(IReadOnlyDictionary<string, string?> source, string key)
        => source.TryGetValue(key, out var value) ? value : string.Empty;
}
