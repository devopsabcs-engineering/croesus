using System.Security.Claims;

namespace Croesus.LegacyNet452.Web
{
    public sealed class SessionProjection
    {
        public bool IsAuthenticated { get; set; }

        public string DisplayName { get; set; }

        public string TenantId { get; set; }

        public static SessionProjection FromIdentity(ClaimsIdentity identity)
        {
            return new SessionProjection
            {
                IsAuthenticated = identity != null && identity.IsAuthenticated,
                DisplayName = identity == null ? null : identity.FindFirst(ClaimTypes.Name)?.Value,
                TenantId = identity == null ? null : identity.FindFirst("tid")?.Value
            };
        }
    }
}
