using Croesus.LegacyNet452.Authentication;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class RedactorTests
    {
        [Theory]
        [InlineData("access_token")]
        [InlineData("client_secret")]
        [InlineData("code")]
        [InlineData("code_verifier")]
        [InlineData("cookie")]
        [InlineData("refresh_token")]
        [InlineData("state")]
        public void RedactValueRemovesSensitiveProtocolValues(string name)
        {
            Assert.Equal("[REDACTED]", Redactor.RedactValue(name, "sensitive-value"));
        }

        [Fact]
        public void RedactValuePreservesNonsensitiveEventCategory()
        {
            Assert.Equal(
                "SecurityTokenExpiredException",
                Redactor.RedactValue("category", "SecurityTokenExpiredException"));
        }
    }
}
