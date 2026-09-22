namespace Croesus.BffYarp.Models;

/// <summary>Cookie hardening flags as actually configured on the session cookie, not as aspired to.</summary>
public sealed record CookieHardeningEvidence(
    string Name,
    bool HasHostPrefix,
    bool HttpOnly,
    bool Secure,
    string SameSite,
    string Path,
    bool HasDomain);

/// <summary>
/// Sanitized metadata for one token acquisition. It deliberately carries no token, no token fragment, and no
/// token identifier that could be correlated back to token material.
/// </summary>
public sealed record TokenAcquisitionEvidence(
    string OperationId,
    string Resource,
    IReadOnlyList<string> GrantedScopes,
    string TokenSource,
    DateTimeOffset AcquiredAt,
    DateTimeOffset? ExpiresOn,
    string Outcome);

/// <summary>
/// The typed evidence allowlist. Every field is enumerated here on purpose: the surface is an allowlist, not
/// a claims dump and not a tenant log browser.
/// <para>
/// Deliberately absent: access tokens, refresh tokens, authorization codes, client secrets and assertions,
/// complete or partial ID tokens, cookie values, token endpoint payloads, and any claim decoded from a
/// Microsoft Graph token. Graph tokens are opaque to clients and are never inspected.
/// </para>
/// <para>
/// Also deliberately absent: any On-Behalf-Of assertion. This application acquires a delegated token for the
/// owned API and forwards it. It performs no On-Behalf-Of exchange, so it claims none.
/// </para>
/// </summary>
public sealed record EvidenceResponse(
    string ApplicationRunId,
    string TokenCustody,
    string TokenCustodyDetail,
    CookieHardeningEvidence SessionCookie,
    string OwnedApiAudienceValidation,
    string? AuthenticationMethod,
    DateTimeOffset? AuthenticationTime,
    IReadOnlyList<string> GrantedDelegatedScopes,
    IReadOnlyList<TokenAcquisitionEvidence> Acquisitions);
