using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Croesus.LegacyNet452.Authentication
{
    internal sealed class AuthenticationFailureClassification
    {
        internal AuthenticationFailureClassification(
            string exceptionType,
            string protocolCode)
        {
            ExceptionType = exceptionType;
            ProtocolCode = protocolCode;
        }

        internal string ExceptionType { get; private set; }

        internal string ProtocolCode { get; private set; }
    }

    public static class AuthenticationEventLogger
    {
        private const int MaxComponentLength = 32;
        private const int MaxExceptionDepth = 6;
        private const int MaxExceptionTypeLength = 128;
        private const int MaxMessageScanLength = 2048;
        private const int MaxPhaseLength = 64;
        private static readonly Regex ProtocolCodePattern = new Regex(
            @"(?<![A-Za-z0-9])(?:IDX[0-9]{4,10}|AADSTS[0-9]{4,10})(?![A-Za-z0-9])",
            RegexOptions.CultureInvariant);

        public static void AuthenticationFailed(Exception exception)
        {
            Trace.TraceWarning(FormatSafeFailure(exception));
        }

        internal static void AuthenticationPhaseFailed(
            string component,
            string phase,
            Exception exception)
        {
            Trace.TraceWarning(FormatSafePhaseFailure(component, phase, exception));
        }

        internal static string FormatSafePhaseFailure(
            string component,
            string phase,
            Exception exception)
        {
            var classification = Classify(exception);
            return string.Format(
                CultureInfo.InvariantCulture,
                "AuthenticationPhaseFailed component={0} phase={1} category={2} hresult=0x{3:X8} protocolCode={4}",
                BoundField(component, MaxComponentLength, "Unknown"),
                BoundField(phase, MaxPhaseLength, "Unknown"),
                classification.ExceptionType,
                exception == null ? 0 : exception.HResult,
                classification.ProtocolCode ?? "none");
        }

        internal static string FormatSafeFailure(Exception exception)
        {
            var classification = Classify(exception);
            return string.Format(
                CultureInfo.InvariantCulture,
                "AuthenticationFailed category={0} protocolCode={1}",
                classification.ExceptionType,
                classification.ProtocolCode ?? "none");
        }

        internal static AuthenticationFailureClassification Classify(Exception exception)
        {
            var fallbackType = SafeExceptionType(exception);
            var current = exception;

            for (var depth = 0; current != null && depth < MaxExceptionDepth; depth++)
            {
                var message = current.Message ?? string.Empty;
                if (message.Length > MaxMessageScanLength)
                {
                    message = message.Substring(0, MaxMessageScanLength);
                }

                var match = ProtocolCodePattern.Match(message);
                if (match.Success)
                {
                    return new AuthenticationFailureClassification(
                        SafeExceptionType(current),
                        match.Value);
                }

                current = current.InnerException;
            }

            return new AuthenticationFailureClassification(fallbackType, null);
        }

        private static string SafeExceptionType(Exception exception)
        {
            var typeName = exception == null
                ? "UnknownException"
                : exception.GetType().Name;
            return typeName.Length <= MaxExceptionTypeLength
                ? typeName
                : typeName.Substring(0, MaxExceptionTypeLength);
        }

        private static string BoundField(string value, int maxLength, string fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }
    }
}
