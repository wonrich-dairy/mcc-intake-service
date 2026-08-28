using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wonrich.QualityPanel;
using Wonrich.QualityPanel.Configuration;

namespace Wonrich.QualityPanel.Tests;

/// <summary>
/// Every consumer wires the panel the same way, which is what keeps the MCC service and the Lab
/// service differing only in configured values rather than in logic (SCRUM-50).
/// </summary>
public class QualityPanelRegistrationTests
{
    private static ServiceProvider Provider(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

        return new ServiceCollection()
            .AddQualityPanel(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void Registration_provides_an_evaluator()
    {
        using var provider = Provider();

        Assert.IsType<QualityPanelEvaluator>(provider.GetRequiredService<IQualityPanelEvaluator>());
    }

    [Fact]
    public void Thresholds_are_bound_from_the_configuration_section()
    {
        using var provider = Provider(
            ("QualityThresholds:MinimumFatPercent", "4.2"),
            ("QualityThresholds:MinimumSnf", "9.0"),
            ("QualityThresholds:WorstAcceptableKqColour", "Yellow"));

        var thresholds = provider.GetRequiredService<IOptions<QualityThresholds>>().Value;

        Assert.Equal(4.2m, thresholds.MinimumFatPercent);
        Assert.Equal(9.0m, thresholds.MinimumSnf);
        Assert.Equal(KqColour.Yellow, thresholds.WorstAcceptableKqColour);
    }

    [Fact]
    public void Defaults_apply_when_nothing_is_configured()
    {
        using var provider = Provider();

        var thresholds = provider.GetRequiredService<IOptions<QualityThresholds>>().Value;

        Assert.Equal(3.5m, thresholds.MinimumFatPercent);
        Assert.Equal(StabilityGrade.MarginallyStable, thresholds.WorstAcceptableStability);
    }

    [Fact]
    public void An_out_of_range_threshold_is_refused_rather_than_silently_accepted()
    {
        using var provider = Provider(("QualityThresholds:MinimumFatPercent", "99"));

        // Validation is on the options, so a nonsense limit surfaces on first resolve.
        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<QualityThresholds>>().Value);
    }

    [Fact]
    public void The_evaluator_is_shared_rather_than_rebuilt_per_use()
    {
        using var provider = Provider();

        Assert.Same(
            provider.GetRequiredService<IQualityPanelEvaluator>(),
            provider.GetRequiredService<IQualityPanelEvaluator>());
    }
}
