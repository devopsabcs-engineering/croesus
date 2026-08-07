using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class PkceBehaviorTests
    {
        [Fact]
        public void Rfc7636VerifierProducesExpectedS256Challenge()
        {
            const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

            string challenge;
            using (var sha256 = SHA256.Create())
            {
                var digest = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
                challenge = Convert.ToBase64String(digest)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
            }

            Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
        }
    }
}
