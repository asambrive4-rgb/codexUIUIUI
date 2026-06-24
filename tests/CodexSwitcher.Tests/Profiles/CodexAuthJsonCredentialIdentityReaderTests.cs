using CodexSwitcher.Infrastructure.Profiles;

namespace CodexSwitcher.Tests.Profiles;

[TestClass]
public sealed class CodexAuthJsonCredentialIdentityReaderTests
{
    [TestMethod]
    public void TryReadAccountId_WithAccountId_ReturnsValue()
    {
        var reader = new CodexAuthJsonCredentialIdentityReader();

        var accountId = reader.TryReadAccountId(
            """
            {
              "auth_mode": "chatgpt",
              "tokens": {
                "account_id": "acct_123",
                "access_token": "redacted"
              }
            }
            """u8);

        Assert.AreEqual("acct_123", accountId);
    }

    [TestMethod]
    public void TryReadAccountId_WithoutAccountId_ReturnsNull()
    {
        var reader = new CodexAuthJsonCredentialIdentityReader();

        var accountId = reader.TryReadAccountId(
            """
            {
              "auth_mode": "chatgpt",
              "tokens": {
                "access_token": "redacted"
              }
            }
            """u8);

        Assert.IsNull(accountId);
    }
}
