using Croesus.ModernBff.Security;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Croesus.ModernBff.Tests;

public sealed class OidcRemoteFailureDiagnosticTests
{
    [Theory]
    [InlineData("IDX10214")]
    [InlineData("AADSTS7000215")]
    public void LogExtractsProtocolCodeFromWrappedFailure(string protocolCode)
    {
        var logger = new CapturingLogger();
        var failure = new InvalidOperationException(
            "Authentication wrapper failed.",
            new OpenIdConnectProtocolException($"{protocolCode}: Credential rejected."));

        OidcRemoteFailureDiagnostic.Log(logger, failure);

        Assert.Equal(
            "OIDC remote failure: ExceptionType=OpenIdConnectProtocolException; " +
            $"ProtocolCode={protocolCode}",
            logger.Message);
    }

    [Fact]
    public void LogDoesNotEmitUnrecognizedOrSecretBearingText()
    {
        var logger = new CapturingLogger();
        const string secretText =
            "IDX10214SECRET query=?code=authorization-code&state=state-value&nonce=nonce-value " +
            "access_token=token-value client_secret=credential-value claim=email@example.com";
        var failure = new InvalidOperationException(
            secretText,
            new Exception("Inner secret: password-value"));

        OidcRemoteFailureDiagnostic.Log(logger, failure);

        Assert.Equal(
            "OIDC remote failure: ExceptionType=InvalidOperationException; ProtocolCode=none",
            logger.Message);
        Assert.DoesNotContain("SECRET", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("authorization-code", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("state-value", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("nonce-value", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("credential-value", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("email@example.com", logger.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password-value", logger.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger
    {
        public string? Message { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Warning, logLevel);
            Assert.Null(exception);
            Message = formatter(state, exception);
        }
    }
}