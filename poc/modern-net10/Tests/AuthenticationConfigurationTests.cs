using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Croesus.ModernBff.Tests;

public sealed class AuthenticationConfigurationTests(ModernBffFactory factory)
    : IClassFixture<ModernBffFactory>
{
    [Fact]
    public void OpenIdConnectUsesConfidentialCodeFlowWithPkce()
    {
        _ = factory.CreateClient();
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal(OpenIdConnectResponseType.Code, options.ResponseType);
        Assert.Equal(OpenIdConnectResponseMode.Query, options.ResponseMode);
        Assert.True(options.UsePkce);
        Assert.False(options.SaveTokens);
        Assert.NotNull(options.TokenValidationParameters.IssuerValidator);
    }

    [Fact]
    public void SessionCookieUsesProductionSecuritySettings()
    {
        _ = factory.CreateClient();
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal("__Host-Croesus.ModernBff.Session", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Null(options.Cookie.Domain);
        Assert.False(options.SlidingExpiration);
    }
}
