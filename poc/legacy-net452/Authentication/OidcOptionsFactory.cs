using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OpenIdConnect;
using Croesus.LegacyNet452.Configuration;

namespace Croesus.LegacyNet452.Authentication
{
    public static class OidcOptionsFactory
    {
        public static OpenIdConnectAuthenticationOptions Create(LegacyAuthenticationSettings settings)
        {
            var options = new OpenIdConnectAuthenticationOptions
            {
                Authority = settings.Authority,
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret,
                RedirectUri = settings.RedirectUri,
                PostLogoutRedirectUri = settings.PostLogoutRedirectUri,
                SignInAsAuthenticationType = CookieOptionsFactory.AuthenticationType,
                ResponseType = OpenIdConnectResponseType.Code,
                ResponseMode = OpenIdConnectResponseMode.Query,
                UsePkce = true,
                RedeemCode = true,
                SaveTokens = false,
                Scope = "openid profile",
                Notifications = new OpenIdConnectAuthenticationNotifications
                {
                    SecurityTokenValidated = notification =>
                    {
                        TenantPolicy.EnsureAllowedIdentity(
                            notification.AuthenticationTicket.Identity,
                            settings);
                        notification.AuthenticationTicket =
                            SessionIdentityProjector.CreateMinimalTicket(
                                notification.AuthenticationTicket);
                        return System.Threading.Tasks.Task.FromResult(0);
                    },
                    AuthenticationFailed = notification =>
                    {
                        AuthenticationEventLogger.AuthenticationFailed(
                            notification.Exception);
                        notification.HandleResponse();
                        notification.Response.StatusCode = 500;
                        notification.Response.ContentType = "text/plain";
                        return notification.Response.WriteAsync(
                            "Authentication could not be completed.");
                    }
                }
            };

            if (settings.IsOrganizationsMode)
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    IssuerValidator = TenantPolicy.ValidateOrganizationsIssuer
                };
            }

            return options;
        }
    }
}
