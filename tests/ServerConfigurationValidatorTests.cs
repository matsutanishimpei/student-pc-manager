using Microsoft.Extensions.Configuration;
using Server;
using Xunit;

namespace Tests;

public class ServerConfigurationValidatorTests
{
    [Theory]
    [InlineData("MaxUploadBytes")]
    [InlineData("CommandTimeoutSeconds")]
    [InlineData("MaxConcurrentApiRequests")]
    public void Validate_RejectsNonPositiveValues(string key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = "0" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => ServerConfigurationValidator.Validate(configuration));
    }

    [Fact]
    public void Validate_AcceptsDefaults()
    {
        ServerConfigurationValidator.Validate(new ConfigurationBuilder().Build());
    }
}
