using System.Net.Http.Headers;
using Xunit;

namespace Croesus.Api.Tests;

// =====================================================================================================
// OPT-IN LIVE REDEMPTION TESTS — SKIPPED BY DEFAULT
// =====================================================================================================
//
// These tests contact the REAL Microsoft Entra token endpoint
// (https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token) to prove the platform-type behaviors
// documented in the research (falsifiable assertions 1-5): a `spa`-platform code can only be redeemed
// cross-origin (AADSTS9002327), and a public client cannot present a credential (AADSTS700025).
//
// They are OPT-IN and SKIPPED BY DEFAULT via the [LiveRedemptionFact] attribute below. They will not
// run — and cannot fail — in the standard CI unit pass. They only execute when the gate variable is set.
//
// SECURITY — NEVER COMMIT SECRETS:
//   Do NOT commit tenant IDs, app IDs, redirect URIs, authorization codes, PKCE code verifiers,
//   client secrets, client assertions, certificates, or access tokens. Every value below is read from
//   environment variables at run time. Authorization codes are single-use and short-lived; obtain a
//   fresh code + code_verifier immediately before running (an interactive auth-code-with-PKCE grant),
//   and clear the variables from your shell afterwards.
//
// REQUIRED ENVIRONMENT VARIABLES:
//   Gate (required to run any test in this file):
//     CROESUS_LIVE_REDEMPTION_TESTS   Set to "1" to enable the suite. Absent/other value = all skip.
//   Common:
//     CROESUS_TENANT_ID               Entra tenant GUID (or verified domain) for the /{tenant}/ segment.
//     CROESUS_TOKEN_SCOPE             (optional) Scope for redemption. Default: "User.Read openid profile".
//   SPA (public) app — Steps 4.2 (a)/(b) and 4.3 (d):
//     CROESUS_SPA_CLIENT_ID           App (client) ID of the `spa`-platform registration.
//     CROESUS_SPA_REDIRECT_URI        Redirect URI registered under the app's `spa` platform node.
//     CROESUS_SPA_AUTH_CODE           Fresh single-use authorization_code obtained for the SPA redirect URI.
//     CROESUS_SPA_CODE_VERIFIER       PKCE code_verifier that matches the code_challenge used to get the code.
//     CROESUS_SPA_ORIGIN              Origin header value matching the SPA redirect URI origin (e.g. https://app.example.com).
//     CROESUS_SPA_CLIENT_SECRET       A client secret to present against the public SPA app (Step 4.3 (d)) — expected to be REJECTED with AADSTS700025.
//   WEB (confidential) app — Step 4.3 (c):
//     CROESUS_WEB_CLIENT_ID           App (client) ID of the `web`-platform registration with a certificate.
//     CROESUS_WEB_REDIRECT_URI        Redirect URI registered under the app's `web` platform node.
//     CROESUS_WEB_AUTH_CODE           Fresh single-use authorization_code obtained for the web redirect URI.
//     CROESUS_WEB_CODE_VERIFIER       PKCE code_verifier matching the web code_challenge.
//     CROESUS_WEB_CLIENT_ASSERTION    Certificate-signed client_assertion JWT (urn:ietf:params:oauth:client-assertion-type:jwt-bearer).
//
// ENABLE + RUN (PowerShell):
//     $env:CROESUS_LIVE_REDEMPTION_TESTS = '1'
//     # ...set the CROESUS_* variables above with fresh, non-committed values...
//     dotnet test api/Tests/Croesus.Api.Tests.csproj
//
// The suite (and the previously passing unit tests) run under `dotnet test`. With the gate unset, every
// [LiveRedemptionFact] reports as Skipped, never Failed.
// =====================================================================================================

/// <summary>
/// Marks a test as an opt-in live redemption test. The test is skipped unless the
/// <c>CROESUS_LIVE_REDEMPTION_TESTS</c> environment variable is set to <c>1</c>. This project does not
/// reference Xunit.SkippableFact, so this custom <see cref="FactAttribute"/> subclass sets the
/// <see cref="FactAttribute.Skip"/> property in its constructor to produce a Skipped (not Failed) result
/// in the default CI unit pass.
/// </summary>
internal sealed class LiveRedemptionFactAttribute : FactAttribute
{
    private const string GateVariable = "CROESUS_LIVE_REDEMPTION_TESTS";

    public LiveRedemptionFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(GateVariable) != "1")
        {
            Skip = $"Opt-in live redemption test. Set {GateVariable}=1 (and the CROESUS_* app/config variables) to run against a real Microsoft Entra tenant.";
        }
    }
}

/// <summary>
/// Live, gated redemption evidence against the real Microsoft Entra <c>/oauth2/v2.0/token</c> endpoint.
/// Each test is skipped by default (see <see cref="LiveRedemptionFactAttribute"/>) and only runs when the
/// operator explicitly enables the suite and supplies fresh, non-committed authorization codes and
/// credentials via environment variables. These tests exercise Entra behavior directly; they do not host
/// or touch Croesus production code.
/// </summary>
public sealed class LiveRedemptionTests
{
    private const string DefaultScope = "User.Read openid profile";

    private static readonly HttpClient Http = new();

    // --- Step 4.2 (a): spa code, NO Origin header -> AADSTS9002327 ---------------------------------------

    [LiveRedemptionFact]
    public async Task SpaCode_RedeemedWithoutOrigin_IsRejectedWith_AADSTS9002327()
    {
        var tenant = RequireEnv("CROESUS_TENANT_ID");
        var form = SpaAuthCodeForm();

        // Server-side redemption of a `spa`-platform code with no Origin header — exactly what a genuine
        // backend-to-Entra POST looks like. Entra rejects it because the code was issued to a Single-Page
        // Application client-type, which may only be redeemed via a cross-origin (browser) request.
        var (status, body) = await PostTokenAsync(tenant, form, origin: null);

        Assert.False(IsSuccess(status), $"Expected a redemption failure but Entra returned {(int)status}. Body: {body}");
        Assert.Contains("AADSTS9002327", body);
    }

    // --- Step 4.2 (b): same spa code, WITH matching Origin header -> 200 + access_token ------------------

    [LiveRedemptionFact]
    public async Task SpaCode_RedeemedWithMatchingOrigin_Succeeds_WithAccessToken()
    {
        var tenant = RequireEnv("CROESUS_TENANT_ID");
        var origin = RequireEnv("CROESUS_SPA_ORIGIN");
        var form = SpaAuthCodeForm();

        // The same authorization code, redeemed as a cross-origin browser request (Origin header present and
        // matching the SPA redirect URI origin). This is the supported `spa`-platform redemption and returns
        // a token. NOTE: an authorization code is single-use, so (a) and (b) cannot share one code in a
        // single run — supply the code intended for whichever assertion you are exercising.
        var (status, body) = await PostTokenAsync(tenant, form, origin);

        Assert.True(IsSuccess(status), $"Expected 200 with an access_token but Entra returned {(int)status}. Body: {body}");
        Assert.Contains("access_token", body);
    }

    // --- Step 4.3 (c): web (confidential) code + client_assertion, NO Origin -> 200 ---------------------

    [LiveRedemptionFact]
    public async Task WebCode_RedeemedServerSideWithCertificateAssertion_Succeeds()
    {
        var tenant = RequireEnv("CROESUS_TENANT_ID");
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = RequireEnv("CROESUS_WEB_CLIENT_ID"),
            ["code"] = RequireEnv("CROESUS_WEB_AUTH_CODE"),
            ["code_verifier"] = RequireEnv("CROESUS_WEB_CODE_VERIFIER"),
            ["redirect_uri"] = RequireEnv("CROESUS_WEB_REDIRECT_URI"),
            ["scope"] = ScopeOrDefault(),
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = RequireEnv("CROESUS_WEB_CLIENT_ASSERTION")
        };

        // A `web`-platform confidential client redeems its code server-side (no Origin header) while
        // presenting a certificate-signed client_assertion. This is the supported confidential redemption
        // and succeeds. Entra also blocks confidential credentials in the presence of an Origin header, so
        // this call must NOT carry one.
        var (status, body) = await PostTokenAsync(tenant, form, origin: null);

        Assert.True(IsSuccess(status), $"Expected 200 for confidential server-side redemption but Entra returned {(int)status}. Body: {body}");
        Assert.Contains("access_token", body);
    }

    // --- Step 4.3 (d): public (spa) app presented a client_secret -> AADSTS700025 -----------------------

    [LiveRedemptionFact]
    public async Task PublicSpaApp_PresentedClientSecret_IsRejectedWith_AADSTS700025()
    {
        var tenant = RequireEnv("CROESUS_TENANT_ID");
        var form = SpaAuthCodeForm();
        form["client_secret"] = RequireEnv("CROESUS_SPA_CLIENT_SECRET");

        // Presenting a client_secret against the public single-page-application registration is rejected:
        // a public client may present neither client_assertion nor client_secret. Sent without an Origin
        // header so the failure is the public-client-with-credential rejection, not a cross-origin block.
        var (status, body) = await PostTokenAsync(tenant, form, origin: null);

        Assert.False(IsSuccess(status), $"Expected a public-client-with-credential rejection but Entra returned {(int)status}. Body: {body}");
        Assert.Contains("AADSTS700025", body);
    }

    // --- Helpers ----------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the shared SPA authorization-code redemption form (public client, no credential). Callers add
    /// an <c>Origin</c> header (or not) and may add a credential to exercise the rejection path.
    /// </summary>
    private static Dictionary<string, string> SpaAuthCodeForm() => new()
    {
        ["grant_type"] = "authorization_code",
        ["client_id"] = RequireEnv("CROESUS_SPA_CLIENT_ID"),
        ["code"] = RequireEnv("CROESUS_SPA_AUTH_CODE"),
        ["code_verifier"] = RequireEnv("CROESUS_SPA_CODE_VERIFIER"),
        ["redirect_uri"] = RequireEnv("CROESUS_SPA_REDIRECT_URI"),
        ["scope"] = ScopeOrDefault()
    };

    /// <summary>
    /// POSTs a form-encoded redemption request to the real Entra token endpoint, optionally attaching an
    /// <c>Origin</c> header to simulate a cross-origin browser request. Returns the status code and raw body.
    /// </summary>
    private static async Task<(System.Net.HttpStatusCode Status, string Body)> PostTokenAsync(
        string tenant, IDictionary<string, string> form, string? origin)
    {
        var uri = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(origin))
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body);
    }

    private static bool IsSuccess(System.Net.HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static string ScopeOrDefault() =>
        Environment.GetEnvironmentVariable("CROESUS_TOKEN_SCOPE") is { Length: > 0 } scope ? scope : DefaultScope;

    /// <summary>
    /// Reads a required environment variable, throwing a clear message if it is missing. Only reached when
    /// the suite is enabled (the gate skips every test otherwise), so a missing value is an operator error.
    /// </summary>
    private static string RequireEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrEmpty(value),
            $"Live redemption suite is enabled but required environment variable '{name}' is not set. See the header comment for the full list.");
        return value!;
    }
}
