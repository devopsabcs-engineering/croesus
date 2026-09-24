using System.Globalization;
using System.Security.Claims;

namespace Croesus.BffYarp.Security;

/// <summary>
/// Turns the freshness controls accepted by <c>/bff/login</c> into authorize-request parameters, and checks
/// the resulting identity against what was asked for.
/// <para>
/// Both values reach this application as query string input and are placed into a request sent to the identity
/// provider, so neither is forwarded as supplied. <c>prompt</c> is matched against a closed set and
/// <c>max_age</c> must parse as a bounded number of seconds.
/// </para>
/// <para>
/// The OpenID Connect handler only validates <c>auth_time</c> against its own <c>MaxAge</c> option, which is
/// fixed for the process. A per-request <c>max_age</c> is therefore a request the provider may honour rather
/// than a control the application enforces, so <see cref="IsAuthenticationFreshEnough"/> performs the check
/// that closes that gap.
/// </para>
/// </summary>
public static class ReauthenticationPolicy
{
    /// <summary>Authentication property key carrying the requested <c>prompt</c> through the challenge.</summary>
    public const string PromptItemKey = "croesus:prompt";

    /// <summary>Authentication property key carrying the requested <c>max_age</c> seconds through the challenge.</summary>
    public const string MaxAgeItemKey = "croesus:max_age";

    /// <summary>An older request is refused rather than sent, so a caller cannot widen the window arbitrarily.</summary>
    public static readonly TimeSpan MaximumAge = TimeSpan.FromDays(1);

    // Tolerates disagreement between this host's clock and the provider's without widening a max_age of zero
    // into a window a stale authentication could pass through.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    // prompt=none is absent deliberately: it resolves to a login_required error delivered to the callback, which
    // is not a useful outcome for a navigation a person just started.
    private static readonly string[] AllowedPrompts = ["login", "select_account", "consent"];

    /// <summary>Accepts a <c>prompt</c> value only when it is one this application is willing to send.</summary>
    public static bool TryReadPrompt(string? requested, out string prompt)
    {
        prompt = string.Empty;
        if (string.IsNullOrWhiteSpace(requested))
        {
            return false;
        }

        var candidate = requested.Trim();
        if (!AllowedPrompts.Contains(candidate, StringComparer.Ordinal))
        {
            return false;
        }

        prompt = candidate;
        return true;
    }

    /// <summary>Accepts a <c>max_age</c> only as a whole number of seconds within <see cref="MaximumAge"/>.</summary>
    public static bool TryReadMaxAge(string? requested, out TimeSpan maxAge)
    {
        maxAge = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(requested))
        {
            return false;
        }

        if (!long.TryParse(requested.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        if (seconds < 0 || seconds > (long)MaximumAge.TotalSeconds)
        {
            return false;
        }

        maxAge = TimeSpan.FromSeconds(seconds);
        return true;
    }

    /// <summary>Renders a validated <c>max_age</c> the way the authorize request carries it.</summary>
    public static string FormatMaxAge(TimeSpan maxAge) =>
        ((long)maxAge.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Reports whether the identity asserts an <c>auth_time</c> within the requested window. A missing or
    /// unreadable <c>auth_time</c> is treated as a failure, because a freshness requirement that cannot be
    /// checked has not been met.
    /// </summary>
    public static bool IsAuthenticationFreshEnough(ClaimsPrincipal? principal, TimeSpan maxAge, DateTimeOffset now)
    {
        var claim = principal?.FindFirst("auth_time")?.Value;
        if (!long.TryParse(claim, NumberStyles.None, CultureInfo.InvariantCulture, out var epochSeconds))
        {
            return false;
        }

        var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        return authenticatedAt + maxAge + ClockSkew >= now;
    }
}
