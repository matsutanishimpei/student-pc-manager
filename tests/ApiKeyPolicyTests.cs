using Share.Security;
using Xunit;

namespace Tests;

public class ApiKeyPolicyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("short-key")]
    public void Validate_RejectsWeakKeys(string apiKey)
    {
        Assert.Throws<ArgumentException>(() => ApiKeyPolicy.Validate(apiKey));
    }

    [Fact]
    public void Validate_AcceptsSixteenCharacterKey()
    {
        ApiKeyPolicy.Validate("1234567890abcdef");
    }
}
