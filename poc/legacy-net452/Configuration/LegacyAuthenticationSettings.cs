using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace Croesus.LegacyNet452.Configuration
{
    public sealed class LegacyAuthenticationSettings
    {
        private LegacyAuthenticationSettings()
        {
        }

        public string ClientId { get; private set; }

        public string ClientSecret { get; private set; }

        public Guid HomeTenantId { get; private set; }

        public bool IsOrganizationsMode { get; private set; }

        public ISet<Guid> AllowedTenantIds { get; private set; }

        public string RedirectUri { get; private set; }

        public string PostLogoutRedirectUri { get; private set; }

        public string Authority
        {
            get
            {
                var tenantSegment = IsOrganizationsMode
                    ? "organizations"
                    : HomeTenantId.ToString("D");
                return "https://login.microsoftonline.com/" + tenantSegment + "/v2.0";
            }
        }

        public static LegacyAuthenticationSettings LoadAndValidate()
        {
            var secretEnvironmentVariable = RequiredSetting(
                "ClientSecretEnvironmentVariable");
            var secret = Environment.GetEnvironmentVariable(secretEnvironmentVariable);
            return CreateAndValidate(
                RequiredSetting("ClientId"),
                RequiredSetting("TenantId"),
                ConfigurationManager.AppSettings["AuthorityMode"],
                ConfigurationManager.AppSettings["AllowedTenantIds"],
                RequiredSetting("RedirectUri"),
                RequiredSetting("PostLogoutRedirectUri"),
                secret);
        }

        public static LegacyAuthenticationSettings CreateAndValidate(
            string clientId,
            string tenantId,
            string authorityMode,
            string allowedTenantIds,
            string redirectUri,
            string postLogoutRedirectUri,
            string clientSecret)
        {
            Guid parsedClientId;
            Guid parsedTenantId;
            if (!Guid.TryParse(clientId, out parsedClientId) || parsedClientId == Guid.Empty)
            {
                throw new ConfigurationErrorsException("ClientId must be a non-empty GUID.");
            }

            if (!Guid.TryParse(tenantId, out parsedTenantId) || parsedTenantId == Guid.Empty)
            {
                throw new ConfigurationErrorsException("TenantId must be a non-empty GUID.");
            }

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new ConfigurationErrorsException(
                    "The configured client-secret environment variable is empty.");
            }

            Uri parsedRedirectUri;
            Uri parsedPostLogoutRedirectUri;
            if (!TryValidateLocalHttpsUri(redirectUri, out parsedRedirectUri) ||
                !TryValidateLocalHttpsUri(postLogoutRedirectUri, out parsedPostLogoutRedirectUri))
            {
                throw new ConfigurationErrorsException(
                    "Redirect URIs must use HTTPS on localhost.");
            }

            var organizationsMode = string.Equals(
                authorityMode,
                "Organizations",
                StringComparison.OrdinalIgnoreCase);
            if (!organizationsMode &&
                !string.IsNullOrWhiteSpace(authorityMode) &&
                !string.Equals(authorityMode, "SingleTenant", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigurationErrorsException(
                    "AuthorityMode must be SingleTenant or Organizations.");
            }

            var allowlist = ParseTenantIds(allowedTenantIds);
            if (organizationsMode && allowlist.Count == 0)
            {
                throw new ConfigurationErrorsException(
                    "Organizations mode requires at least one allowed tenant GUID.");
            }

            if (!organizationsMode)
            {
                allowlist.Clear();
                allowlist.Add(parsedTenantId);
            }

            return new LegacyAuthenticationSettings
            {
                ClientId = parsedClientId.ToString("D"),
                ClientSecret = clientSecret,
                HomeTenantId = parsedTenantId,
                IsOrganizationsMode = organizationsMode,
                AllowedTenantIds = allowlist,
                RedirectUri = parsedRedirectUri.AbsoluteUri,
                PostLogoutRedirectUri = parsedPostLogoutRedirectUri.AbsoluteUri
            };
        }

        public bool IsTenantAllowed(Guid tenantId)
        {
            return AllowedTenantIds.Contains(tenantId);
        }

        private static HashSet<Guid> ParseTenantIds(string value)
        {
            var result = new HashSet<Guid>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            foreach (var item in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Guid tenantId;
                if (!Guid.TryParse(item.Trim(), out tenantId) || tenantId == Guid.Empty)
                {
                    throw new ConfigurationErrorsException(
                    "AllowedTenantIds must contain only non-empty GUID values.");
                }

                result.Add(tenantId);
            }

            return result;
        }

        private static bool TryValidateLocalHttpsUri(string value, out Uri uri)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
                string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        }

        private static string RequiredSetting(string name)
        {
            var value = ConfigurationManager.AppSettings[name];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ConfigurationErrorsException(name + " is required.");
            }

            return value.Trim();
        }
    }
}
