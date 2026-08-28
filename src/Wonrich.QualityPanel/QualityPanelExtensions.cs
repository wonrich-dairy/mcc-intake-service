using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wonrich.QualityPanel.Configuration;

namespace Wonrich.QualityPanel;

/// <summary>Registers the shared quality panel in a consuming service (SCRUM-50).</summary>
public static class QualityPanelExtensions
{
    /// <summary>
    /// Binds <see cref="QualityThresholds"/> from configuration and registers the evaluator.
    /// </summary>
    /// <remarks>
    /// Every consumer wires it exactly this way, so the MCC service and the Lab service differ
    /// only in the values they configure — never in the logic they run.
    /// </remarks>
    public static IServiceCollection AddQualityPanel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<QualityThresholds>()
            .Bind(configuration.GetSection(QualityThresholds.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IQualityPanelEvaluator, QualityPanelEvaluator>();

        return services;
    }
}
