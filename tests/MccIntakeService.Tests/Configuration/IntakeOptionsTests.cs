using System.ComponentModel.DataAnnotations;
using MccIntakeService.Configuration;

namespace MccIntakeService.Tests.Configuration;

public class IntakeOptionsTests
{
    private static IReadOnlyList<ValidationResult> Validate(IntakeOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }

    [Fact]
    public void The_defaults_describe_a_centre_closing_at_four_in_the_afternoon_in_Colombo()
    {
        var options = new IntakeOptions();

        Assert.Equal(new TimeOnly(16, 0), options.ParsedDailyCutoff);
        Assert.Equal("Asia/Colombo", options.TimeZone);
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("06:30")]
    [InlineData("23:59")]
    [InlineData("00:00")]
    public void A_valid_cutoff_passes_validation_and_parses(string cutoff)
    {
        var options = new IntakeOptions { DailyCutoff = cutoff };

        Assert.Empty(Validate(options));
        Assert.Equal(TimeOnly.ParseExact(cutoff, "HH:mm"), options.ParsedDailyCutoff);
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("6:30")]
    [InlineData("16:60")]
    [InlineData("four o'clock")]
    [InlineData("")]
    public void A_malformed_cutoff_fails_validation(string cutoff)
    {
        var options = new IntakeOptions { DailyCutoff = cutoff };

        Assert.NotEmpty(Validate(options));
    }

    [Fact]
    public void Parsing_a_malformed_cutoff_fails_loudly_rather_than_silently_defaulting()
    {
        var options = new IntakeOptions { DailyCutoff = "nonsense" };

        Assert.Throws<InvalidOperationException>(() => options.ParsedDailyCutoff);
    }

    [Fact]
    public void The_configured_time_zone_resolves_to_a_real_zone()
    {
        var options = new IntakeOptions();

        Assert.Equal(TimeSpan.FromMinutes(330), options.ResolvedTimeZone.BaseUtcOffset);
    }

    [Fact]
    public void An_unknown_time_zone_is_reported_with_the_offending_value()
    {
        var options = new IntakeOptions { TimeZone = "Mars/Olympus_Mons" };

        var exception = Assert.Throws<InvalidOperationException>(() => options.ResolvedTimeZone);

        Assert.Contains("Mars/Olympus_Mons", exception.Message, StringComparison.Ordinal);
    }
}
