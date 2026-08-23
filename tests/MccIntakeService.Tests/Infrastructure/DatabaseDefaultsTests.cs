using MccIntakeService.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace MccIntakeService.Tests.Infrastructure;

public class DatabaseDefaultsTests
{
    private static IConfiguration ConfigurationWith(string? serverVersion)
    {
        var values = new Dictionary<string, string?>
        {
            [DatabaseDefaults.ServerVersionKey] = serverVersion
        };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void The_configured_server_version_is_used_when_one_is_supplied()
    {
        var version = DatabaseDefaults.ServerVersionFrom(ConfigurationWith("8.4.0-mysql"));

        Assert.Equal(new Version(8, 4, 0), version.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_default_server_version_is_used_when_configuration_says_nothing(string? configured)
    {
        var version = DatabaseDefaults.ServerVersionFrom(ConfigurationWith(configured));

        Assert.Equal(new Version(8, 0, 36), version.Version);
    }
}
