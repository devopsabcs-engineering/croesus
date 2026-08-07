using System;
using System.Collections.Generic;

namespace Croesus.LegacyNet452.Authentication
{
    public static class Redactor
    {
        private static readonly ISet<string> SensitiveNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "access_token",
                "authorization",
                "client_secret",
                "code",
                "code_verifier",
                "cookie",
                "id_token",
                "refresh_token",
                "secret",
                "state"
            };

        public static string RedactValue(string name, string value)
        {
            if (SensitiveNames.Contains(name ?? string.Empty))
            {
                return "[REDACTED]";
            }

            return string.IsNullOrWhiteSpace(value) ? "[EMPTY]" : value;
        }
    }
}
