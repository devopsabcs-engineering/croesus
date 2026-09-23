namespace Croesus.OwnedApi.Models;

/// <summary>
/// What the owned API observed about one call. Claim values and boolean observations only: no token,
/// no assertion, no header value, and nothing that could be replayed if this response were captured.
/// </summary>
/// <param name="Audience">The <c>aud</c> claim. The audience boundary is visible here, not inferred.</param>
/// <param name="Issuer">The <c>iss</c> claim, so the authority that minted the token is explicit.</param>
/// <param name="TenantId">The <c>tid</c> claim.</param>
/// <param name="SubjectObjectId">The <c>oid</c> claim of the signed-in user the call acts for.</param>
/// <param name="CallingApplicationId">The <c>azp</c> or <c>appid</c> claim: which client presented the token.</param>
/// <param name="Scopes">Delegated scopes carried by the token.</param>
/// <param name="ReceivedCookie">True if a cookie reached the API. A back-end-for-frontend strips it, so
/// this is expected to be false and is reported rather than assumed.</param>
internal sealed record CallEvidence(
    string? Audience,
    string? Issuer,
    string? TenantId,
    string? SubjectObjectId,
    string? CallingApplicationId,
    IReadOnlyList<string> Scopes,
    bool ReceivedCookie);
