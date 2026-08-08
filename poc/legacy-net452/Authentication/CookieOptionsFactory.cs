using System;
using Microsoft.Owin;
using Microsoft.Owin.Host.SystemWeb;
using Microsoft.Owin.Security.Cookies;

namespace Croesus.LegacyNet452.Authentication
{
    public static class CookieOptionsFactory
    {
        public const string AuthenticationType = "Croesus.Legacy.Session";

        public static CookieAuthenticationOptions Create()
        {
            return new CookieAuthenticationOptions
            {
                AuthenticationType = AuthenticationType,
                CookieName = "__Host-Croesus.Legacy.Session",
                CookiePath = "/",
                CookieHttpOnly = true,
                CookieSecure = CookieSecureOption.Always,
                CookieSameSite = SameSiteMode.Lax,
                CookieManager = new SystemWebCookieManager(),
                ExpireTimeSpan = TimeSpan.FromMinutes(30),
                SlidingExpiration = false,
                Provider = new CookieAuthenticationProvider
                {
                    OnException = context =>
                    {
                        AuthenticationEventLogger.AuthenticationPhaseFailed(
                            "Cookie",
                            context.Location.ToString(),
                            context.Exception);
                    }
                }
            };
        }
    }
}
