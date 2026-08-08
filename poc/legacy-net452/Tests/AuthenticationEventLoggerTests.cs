using System;
using Croesus.LegacyNet452.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class AuthenticationEventLoggerTests
    {
        [Theory]
        [InlineData("IDX10214")]
        [InlineData("AADSTS7000215")]
        public void ClassifyExtractsProtocolCodeFromWrappedFailure(string protocolCode)
        {
            var failure = new InvalidOperationException(
                "Authentication wrapper failed.",
                new OpenIdConnectProtocolException(
                    protocolCode + ": Credential rejected."));

            var logEntry = AuthenticationEventLogger.FormatSafeFailure(failure);

            Assert.Equal(
                "AuthenticationFailed category=OpenIdConnectProtocolException protocolCode=" +
                protocolCode,
                logEntry);
        }

        [Fact]
        public void ClassifyDoesNotReturnUnrecognizedOrSecretBearingText()
        {
            const string secretText =
                "IDX10214SECRET query=?code=authorization-code&state=state-value&nonce=nonce-value " +
                "access_token=token-value client_secret=credential-value claim=email@example.com";
            var failure = new InvalidOperationException(
                secretText,
                new Exception("Inner secret: password-value"));

            var logEntry = AuthenticationEventLogger.FormatSafeFailure(failure);

            Assert.Equal(
                "AuthenticationFailed category=InvalidOperationException protocolCode=none",
                logEntry);
            Assert.DoesNotContain("SECRET", logEntry);
            Assert.DoesNotContain("authorization-code", logEntry);
            Assert.DoesNotContain("state-value", logEntry);
            Assert.DoesNotContain("nonce-value", logEntry);
            Assert.DoesNotContain("token-value", logEntry);
            Assert.DoesNotContain("credential-value", logEntry);
            Assert.DoesNotContain("email@example.com", logEntry);
            Assert.DoesNotContain("password-value", logEntry);
        }

        [Fact]
        public void FormatSafePhaseFailureIncludesOnlyBoundedClassificationFields()
        {
            const string secretText =
                "query=?code=authorization-code&state=state-value&nonce=nonce-value " +
                "access_token=token-value client_secret=credential-value claim=email@example.com";
            var failure = new System.Web.HttpException(
                secretText,
                new OpenIdConnectProtocolException("Nested secret: password-value"));

            var logEntry = AuthenticationEventLogger.FormatSafePhaseFailure(
                "Cookie",
                "ApplyResponseGrant",
                failure);

            Assert.Equal(
                "AuthenticationPhaseFailed component=Cookie phase=ApplyResponseGrant " +
                "category=HttpException hresult=0x80004005 protocolCode=none",
                logEntry);
            Assert.DoesNotContain("authorization-code", logEntry);
            Assert.DoesNotContain("state-value", logEntry);
            Assert.DoesNotContain("nonce-value", logEntry);
            Assert.DoesNotContain("token-value", logEntry);
            Assert.DoesNotContain("credential-value", logEntry);
            Assert.DoesNotContain("email@example.com", logEntry);
            Assert.DoesNotContain("password-value", logEntry);
        }
    }
}