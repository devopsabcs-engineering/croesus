using System;
using Croesus.LegacyNet452.Configuration;

namespace Croesus.LegacyNet452.Tests
{
    internal static class TestSettings
    {
        internal const string HomeTenantId = "11111111-1111-1111-1111-111111111111";
        internal const string AllowedTenantId = "33333333-3333-3333-3333-333333333333";

        internal static LegacyAuthenticationSettings SingleTenant()
        {
            return Create("SingleTenant", string.Empty);
        }

        internal static LegacyAuthenticationSettings Organizations()
        {
            return Create("Organizations", AllowedTenantId);
        }

        internal static LegacyAuthenticationSettings Create(
            string mode,
            string allowedTenantIds)
        {
            return LegacyAuthenticationSettings.CreateAndValidate(
                "22222222-2222-2222-2222-222222222222",
                HomeTenantId,
                mode,
                allowedTenantIds,
                "https://localhost:44352/signin-oidc",
                "https://localhost:44352/",
                Guid.NewGuid().ToString("N"));
        }
    }
}
