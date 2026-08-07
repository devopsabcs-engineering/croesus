using System;
using System.Diagnostics;

namespace Croesus.LegacyNet452.Authentication
{
    public static class AuthenticationEventLogger
    {
        public static void AuthenticationFailed(Exception exception)
        {
            var category = exception == null
                ? "UnknownAuthenticationFailure"
                : exception.GetType().Name;
            Trace.TraceWarning(
                "AuthenticationFailed category={0} details={1}",
                Redactor.RedactValue("exception", category),
                "[REDACTED]");
        }
    }
}
