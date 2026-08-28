using MccIntakeService.Domain.Common;
using MccIntakeService.Domain.Societies;

namespace MccIntakeService.Tests.Domain;

public class SocietyTests
{
    [Fact]
    public void A_society_code_and_can_prefix_are_normalised_to_upper_case()
    {
        var society = new Society(Guid.NewGuid(), " kc ", "  Kandy Society  ", "kc");

        Assert.Equal("KC", society.Code);
        Assert.Equal("KC", society.CanLabelPrefix);
        Assert.Equal("Kandy Society", society.Name);
    }

    [Fact]
    public void A_society_is_active_by_default()
    {
        var society = new Society(Guid.NewGuid(), "KC", "Kandy Society", "KC");

        Assert.True(society.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_society_name_is_required(string name)
    {
        Assert.Throws<DomainValidationException>(() => new Society(Guid.NewGuid(), "KC", name, "KC"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_society_code_is_required(string code)
    {
        Assert.Throws<DomainValidationException>(() => new Society(Guid.NewGuid(), code, "Kandy Society", "KC"));
    }
}
