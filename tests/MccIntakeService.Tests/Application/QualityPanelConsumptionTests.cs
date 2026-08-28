using MccIntakeService.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wonrich.QualityPanel;
using Wonrich.QualityPanel.Configuration;

namespace MccIntakeService.Tests.Application;

/// <summary>
/// SCRUM-50 requires the MCC service to consume the shared panel rather than reimplement it, and
/// requires identical readings to produce identical results wherever they are evaluated.
/// </summary>
public class QualityPanelConsumptionTests
{
    private static PanelReadings Readings() => new(
        3.9m,
        27.4m,
        29.0m,
        new Dictionary<AlcoholStage, StageOutcome>
        {
            [AlcoholStage.Alcohol80] = StageOutcome.Positive,
            [AlcoholStage.Alcohol75] = StageOutcome.Negative
        },
        KqColour.BluishGreen);

    [Fact]
    public void The_hosted_service_resolves_the_shared_evaluator()
    {
        using var factory = new IntakeApiFactory();

        using var scope = factory.Services.CreateScope();
        var evaluator = scope.ServiceProvider.GetService<IQualityPanelEvaluator>();

        Assert.NotNull(evaluator);
        Assert.IsType<QualityPanelEvaluator>(evaluator);
    }

    [Fact]
    public void The_hosted_service_binds_the_configured_thresholds()
    {
        using var factory = new IntakeApiFactory();

        using var scope = factory.Services.CreateScope();
        var thresholds = scope.ServiceProvider.GetRequiredService<IOptions<QualityThresholds>>().Value;

        Assert.Equal(3.5m, thresholds.MinimumFatPercent);
        Assert.Equal(8.5m, thresholds.MinimumSnf);
        Assert.Equal(26.0m, thresholds.MinimumCorrectedClr);
    }

    [Fact]
    public void The_service_reaches_the_same_verdict_as_the_library_called_directly()
    {
        using var factory = new IntakeApiFactory();

        using var scope = factory.Services.CreateScope();
        var hosted = scope.ServiceProvider.GetRequiredService<IQualityPanelEvaluator>();
        var thresholds = scope.ServiceProvider.GetRequiredService<IOptions<QualityThresholds>>();

        var direct = new QualityPanelEvaluator(thresholds);

        var fromService = hosted.Evaluate(Readings());
        var fromLibrary = direct.Evaluate(Readings());

        // Any drift here would mean the service had grown its own copy of the calculation.
        Assert.Equal(fromLibrary.Composition, fromService.Composition);
        Assert.Equal(fromLibrary.Cascade.Grade, fromService.Cascade.Grade);
        Assert.Equal(fromLibrary.Passed, fromService.Passed);
    }

    [Fact]
    public void The_service_carries_no_calculation_of_its_own()
    {
        // The formulae live in the library; the service must reference them, never restate them.
        var serviceTypes = typeof(Program).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("MccIntakeService", StringComparison.Ordinal) == true)
            .Select(type => type.Name);

        Assert.DoesNotContain("MilkComposition", serviceTypes);
        Assert.DoesNotContain("AlcoholCascade", serviceTypes);
        Assert.DoesNotContain("KqColour", serviceTypes);
    }
}
