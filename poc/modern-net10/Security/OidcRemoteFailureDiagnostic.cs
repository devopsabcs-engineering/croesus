using System.Text.RegularExpressions;

namespace Croesus.ModernBff.Security;

internal readonly record struct OidcRemoteFailureClassification(
    string ExceptionType,
    string? ProtocolCode);

internal static partial class OidcRemoteFailureDiagnostic
{
    private const int MaxExceptionDepth = 6;
    private const int MaxExceptionTypeLength = 128;
    private const int MaxMessageScanLength = 2048;

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?:IDX[0-9]{4,10}|AADSTS[0-9]{4,10})(?![A-Za-z0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProtocolCodePattern();

    internal static OidcRemoteFailureClassification Classify(Exception? failure)
    {
        var fallbackType = SafeExceptionType(failure);
        var current = failure;

        for (var depth = 0; current is not null && depth < MaxExceptionDepth; depth++)
        {
            var message = current.Message;
            if (message.Length > MaxMessageScanLength)
            {
                message = message[..MaxMessageScanLength];
            }

            var match = ProtocolCodePattern().Match(message);
            if (match.Success)
            {
                return new OidcRemoteFailureClassification(
                    SafeExceptionType(current),
                    match.Value);
            }

            current = current.InnerException;
        }

        return new OidcRemoteFailureClassification(fallbackType, null);
    }

    internal static void Log(ILogger logger, Exception? failure)
    {
        var classification = Classify(failure);
        logger.LogWarning(
            "OIDC remote failure: ExceptionType={ExceptionType}; ProtocolCode={ProtocolCode}",
            classification.ExceptionType,
            classification.ProtocolCode ?? "none");
    }

    private static string SafeExceptionType(Exception? failure)
    {
        var typeName = failure?.GetType().Name ?? "UnknownException";
        return typeName.Length <= MaxExceptionTypeLength
            ? typeName
            : typeName[..MaxExceptionTypeLength];
    }
}