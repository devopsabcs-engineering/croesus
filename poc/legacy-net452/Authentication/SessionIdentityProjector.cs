using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.Owin.Security;

namespace Croesus.LegacyNet452.Authentication
{
    public static class SessionIdentityProjector
    {
        private static readonly string[] NameClaimTypes =
        {
            "name",
            ClaimTypes.Name
        };

        private static readonly string[] SubjectClaimTypes =
        {
            "http://schemas.microsoft.com/identity/claims/objectidentifier",
            ClaimTypes.NameIdentifier,
            "sub"
        };

        public static AuthenticationTicket CreateMinimalTicket(
            AuthenticationTicket source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            var sourceIdentity = source.Identity;
            var claims = new List<Claim>();
            AddFirstClaim(claims, sourceIdentity, SubjectClaimTypes, ClaimTypes.NameIdentifier);
            AddFirstClaim(claims, sourceIdentity, NameClaimTypes, ClaimTypes.Name);
            AddFirstClaim(claims, sourceIdentity, new[] { "tid" }, "tid");

            var identity = new ClaimsIdentity(
                claims,
                CookieOptionsFactory.AuthenticationType,
                ClaimTypes.Name,
                ClaimTypes.Role);
            var properties = new AuthenticationProperties
            {
                AllowRefresh = false,
                IsPersistent = false,
                IssuedUtc = source.Properties.IssuedUtc,
                ExpiresUtc = source.Properties.ExpiresUtc,
                RedirectUri = source.Properties.RedirectUri
            };

            return new AuthenticationTicket(identity, properties);
        }

        private static void AddFirstClaim(
            ICollection<Claim> destination,
            ClaimsIdentity source,
            IEnumerable<string> sourceTypes,
            string destinationType)
        {
            foreach (var sourceType in sourceTypes)
            {
                var claim = source.FindFirst(sourceType);
                if (claim != null && !string.IsNullOrWhiteSpace(claim.Value))
                {
                    destination.Add(new Claim(destinationType, claim.Value));
                    return;
                }
            }
        }
    }
}
