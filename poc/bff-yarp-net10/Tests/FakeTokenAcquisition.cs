using System.Security.Claims;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// A token acquisition stand-in. It lets the boundary tests exercise the success, interaction-required, and
/// service-failure paths deterministically, with no Microsoft Entra dependency of any kind.
/// </summary>
internal sealed class FakeTokenAcquisition : ITokenAcquisition
{
    public const string AccessToken = "server-acquired-access-token-" + TestConfiguration.SentinelSecret;

    public enum Behavior
    {
        Succeed,
        InteractionRequired,
        ServiceFailure
    }

    public Behavior NextBehavior { get; set; } = Behavior.Succeed;

    public int CallCount { get; private set; }

    public Task<string> GetAccessTokenForUserAsync(
        IEnumerable<string> scopes,
        string? authenticationScheme,
        string? tenantId = null,
        string? userFlow = null,
        ClaimsPrincipal? user = null,
        TokenAcquisitionOptions? tokenAcquisitionOptions = null) =>
        Task.FromResult(Produce(scopes).AccessToken);

    public Task<AuthenticationResult> GetAuthenticationResultForUserAsync(
        IEnumerable<string> scopes,
        string? authenticationScheme,
        string? tenantId = null,
        string? userFlow = null,
        ClaimsPrincipal? user = null,
        TokenAcquisitionOptions? tokenAcquisitionOptions = null) =>
        Task.FromResult(Produce(scopes));

    public Task<string> GetAccessTokenForAppAsync(
        string scope,
        string? authenticationScheme,
        string? tenant = null,
        TokenAcquisitionOptions? tokenAcquisitionOptions = null) =>
        Task.FromResult(Produce([scope]).AccessToken);

    public Task<AuthenticationResult> GetAuthenticationResultForAppAsync(
        string scope,
        string? authenticationScheme,
        string? tenant = null,
        TokenAcquisitionOptions? tokenAcquisitionOptions = null) =>
        Task.FromResult(Produce([scope]));

    public Task ReplyForbiddenWithWwwAuthenticateHeaderAsync(
        IEnumerable<string> scopes,
        MsalUiRequiredException msalServiceException,
        HttpResponse? httpResponse = null) =>
        Task.CompletedTask;

    public Task ReplyForbiddenWithWwwAuthenticateHeaderAsync(
        IEnumerable<string> scopes,
        MsalUiRequiredException msalServiceException,
        string? authenticationScheme,
        HttpResponse? httpResponse = null) =>
        Task.CompletedTask;

    public void ReplyForbiddenWithWwwAuthenticateHeader(
        IEnumerable<string> scopes,
        MsalUiRequiredException msalServiceException,
        HttpResponse? httpResponse = null)
    {
    }

    public void ReplyForbiddenWithWwwAuthenticateHeader(
        IEnumerable<string> scopes,
        MsalUiRequiredException msalServiceException,
        string? authenticationScheme,
        HttpResponse? httpResponse = null)
    {
    }

    public string GetEffectiveAuthenticationScheme(string? authenticationScheme) =>
        authenticationScheme ?? string.Empty;

    private AuthenticationResult Produce(IEnumerable<string> scopes)
    {
        CallCount++;
        return NextBehavior switch
        {
            Behavior.InteractionRequired => throw new MsalUiRequiredException(
                "interaction_required",
                "Synthetic interaction-required condition."),
            Behavior.ServiceFailure => throw new MsalServiceException(
                "service_unavailable",
                "Synthetic token service failure."),
            _ => new AuthenticationResult(
                AccessToken,
                false,
                TestConfiguration.ObjectId,
                DateTimeOffset.UtcNow.AddMinutes(30),
                DateTimeOffset.UtcNow.AddMinutes(30),
                TestConfiguration.TenantId,
                null!,
                string.Empty,
                [.. scopes],
                Guid.NewGuid(),
                new AuthenticationResultMetadata(TokenSource.IdentityProvider),
                "Bearer")
        };
    }
}
