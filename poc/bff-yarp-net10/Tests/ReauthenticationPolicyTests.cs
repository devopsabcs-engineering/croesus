using System.Security.Claims;
using Croesus.BffYarp.Security;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// Covers the freshness controls accepted by <c>/bff/login</c>. The values arrive as query string input and end
/// up in a request sent to the identity provider, so the accepted set is asserted rather than assumed, and the
/// freshness check the application performs on the returned identity is asserted alongside it.
/// </summary>
public class ReauthenticationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static ClaimsPrincipal PrincipalAuthenticatedAt(DateTimeOffset authenticatedAt) =>
        new(new ClaimsIdentity([new Claim("auth_time", authenticatedAt.ToUnixTimeSeconds().ToString())], "Test"));

    [Theory]
    [InlineData("login")]
    [InlineData("select_account")]
    [InlineData("consent")]
    public void TryReadPrompt_AcceptsTheValuesThisApplicationSends(string requested)
    {
        Assert.True(ReauthenticationPolicy.TryReadPrompt(requested, out var prompt));
        Assert.Equal(requested, prompt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("login consent")]
    [InlineData("Login")]
    public void TryReadPrompt_RefusesAnythingElse(string? requested) =>
        Assert.False(ReauthenticationPolicy.TryReadPrompt(requested, out _));

    [Fact]
    public void TryReadMaxAge_AcceptsZeroAsAnImmediateReauthenticationRequest()
    {
        Assert.True(ReauthenticationPolicy.TryReadMaxAge("0", out var maxAge));
        Assert.Equal(TimeSpan.Zero, maxAge);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("99999999")]
    public void TryReadMaxAge_RefusesValuesOutsideABoundedNumberOfSeconds(string? requested) =>
        Assert.False(ReauthenticationPolicy.TryReadMaxAge(requested, out _));

    [Fact]
    public void IsAuthenticationFreshEnough_AcceptsAnAuthenticationInsideTheWindow() =>
        Assert.True(ReauthenticationPolicy.IsAuthenticationFreshEnough(
            PrincipalAuthenticatedAt(Now.AddMinutes(-4)),
            TimeSpan.FromMinutes(10),
            Now));

    [Fact]
    public void IsAuthenticationFreshEnough_RefusesAnAuthenticationOlderThanTheWindow() =>
        Assert.False(ReauthenticationPolicy.IsAuthenticationFreshEnough(
            PrincipalAuthenticatedAt(Now.AddDays(-195)),
            TimeSpan.Zero,
            Now));

    [Fact]
    public void IsAuthenticationFreshEnough_RefusesAnIdentityThatDoesNotReportAuthenticationTime()
    {
        // Entra does not always emit auth_time. A requirement that cannot be checked has not been met, so the
        // absence is a refusal rather than a pass.
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "abc")], "Test"));

        Assert.False(ReauthenticationPolicy.IsAuthenticationFreshEnough(principal, TimeSpan.Zero, Now));
    }
}
