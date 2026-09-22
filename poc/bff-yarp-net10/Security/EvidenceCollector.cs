using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Croesus.BffYarp.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Croesus.BffYarp.Security;

/// <summary>
/// Builds the sanitized evidence projection. The collector only ever receives metadata; no caller hands it a
/// token, a cookie value, or a protocol payload, so there is nothing in its state that could leak one.
/// </summary>
internal sealed class EvidenceCollector(
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
    TimeProvider timeProvider)
{
    private const int MaxRecordsPerSession = 20;
    private const string NotExercised = "not-exercised";

    private readonly ConcurrentDictionary<string, SessionEvidence> _sessions = new(StringComparer.Ordinal);

    public string ApplicationRunId { get; } = Guid.NewGuid().ToString("n");

    public string RecordAcquisition(
        ClaimsPrincipal principal,
        string resource,
        IReadOnlyList<string> grantedScopes,
        string tokenSource,
        DateTimeOffset? expiresOn,
        string outcome)
    {
        var operationId = Guid.NewGuid().ToString("n");
        var session = _sessions.GetOrAdd(SessionKey(principal), _ => new SessionEvidence());
        session.Add(new TokenAcquisitionEvidence(
            operationId,
            resource,
            grantedScopes,
            tokenSource,
            timeProvider.GetUtcNow(),
            expiresOn,
            outcome));
        return operationId;
    }

    /// <summary>
    /// Records what the owned API actually did with the forwarded token. The verdict is derived from the
    /// downstream response status, so it reports an observed outcome rather than an assumed one.
    /// </summary>
    public void RecordOwnedApiVerdict(ClaimsPrincipal principal, int downstreamStatusCode)
    {
        var verdict = downstreamStatusCode switch
        {
            >= 200 and < 300 => "accepted-by-owned-api",
            401 => "rejected-by-owned-api-authentication",
            403 => "rejected-by-owned-api-authorization",
            _ => $"inconclusive-downstream-status-{downstreamStatusCode}"
        };

        _sessions.GetOrAdd(SessionKey(principal), _ => new SessionEvidence()).SetVerdict(verdict);
    }

    public EvidenceResponse Project(ClaimsPrincipal principal)
    {
        var options = cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var cookie = new CookieHardeningEvidence(
            options.Cookie.Name ?? string.Empty,
            (options.Cookie.Name ?? string.Empty).StartsWith("__Host-", StringComparison.Ordinal),
            options.Cookie.HttpOnly,
            options.Cookie.SecurePolicy == CookieSecurePolicy.Always,
            options.Cookie.SameSite.ToString(),
            options.Cookie.Path ?? string.Empty,
            !string.IsNullOrEmpty(options.Cookie.Domain));

        _sessions.TryGetValue(SessionKey(principal), out var session);
        var acquisitions = session?.Snapshot() ?? [];

        return new EvidenceResponse(
            ApplicationRunId,
            TokenCustody: "server-side",
            TokenCustodyDetail:
                "The browser holds an opaque session reference. The authentication ticket is serialized into the "
                + "distributed ticket store and access tokens stay in the server-side token cache. SaveTokens is false.",
            cookie,
            session?.Verdict ?? NotExercised,
            ReadAuthenticationMethod(principal),
            ReadAuthenticationTime(principal),
            acquisitions.SelectMany(record => record.GrantedScopes).Distinct(StringComparer.Ordinal).ToArray(),
            acquisitions);
    }

    /// <summary>
    /// A pseudonymous, per-session key. It is a truncated hash of the session and subject identifiers, so it
    /// is stable within a session, is not a directory identifier, and is never returned to the browser.
    /// </summary>
    internal static string SessionKey(ClaimsPrincipal principal)
    {
        var source = principal.FindFirstValue("sid")
            ?? principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "anonymous";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..32];
    }

    private static string? ReadAuthenticationMethod(ClaimsPrincipal principal)
    {
        var methods = principal.FindAll("amr").Select(claim => claim.Value).ToArray();
        return methods.Length == 0 ? null : string.Join(' ', methods);
    }

    private static DateTimeOffset? ReadAuthenticationTime(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue("auth_time"), out var epochSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(epochSeconds)
            : null;

    private sealed class SessionEvidence
    {
        private readonly Lock _gate = new();
        private readonly Queue<TokenAcquisitionEvidence> _records = new();

        public string Verdict { get; private set; } = NotExercised;

        public void Add(TokenAcquisitionEvidence record)
        {
            lock (_gate)
            {
                _records.Enqueue(record);
                while (_records.Count > MaxRecordsPerSession)
                {
                    _records.Dequeue();
                }
            }
        }

        public void SetVerdict(string verdict)
        {
            lock (_gate)
            {
                Verdict = verdict;
            }
        }

        public TokenAcquisitionEvidence[] Snapshot()
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }
    }
}
