using MccIntakeService.Domain.Common;

namespace MccIntakeService.Domain.Consignments;

/// <summary>
/// A single physical can delivered as part of a consignment, identified by the society's
/// can label (e.g. "KC 01") and carrying the quantity of milk received in it.
/// </summary>
/// <remarks>
/// The can is weighed at the gate, so kilograms are the recorded measurement and litres are
/// derived from them using the centre's configured milk density. Both are stored: litres is what
/// the tank manifests and downstream reports are expressed in, and recomputing it later from a
/// density that has since been retuned would silently restate history.
/// </remarks>
public class ConsignmentCan
{
    /// <summary>Maximum kilograms one can may hold; guards against fat-fingered entry at the gate.</summary>
    public const decimal MaxQuantityKg = 1_000m;

    /// <summary>EF Core materialisation constructor.</summary>
    private ConsignmentCan()
    {
        CanLabel = string.Empty;
    }

    internal ConsignmentCan(
        Guid id,
        string canLabelPrefix,
        int canNumber,
        decimal quantityKg,
        decimal densityKgPerLitre)
    {
        if (canNumber <= 0)
        {
            throw new DomainValidationException($"Can number must be greater than zero, but was {canNumber}.");
        }

        if (quantityKg <= 0)
        {
            throw new DomainValidationException(
                $"Quantity for can {canNumber:00} must be greater than zero kilograms.");
        }

        if (quantityKg > MaxQuantityKg)
        {
            throw new DomainValidationException(
                $"Quantity for can {canNumber:00} is {quantityKg} kg, which exceeds the {MaxQuantityKg} kg limit for a single can.");
        }

        if (densityKgPerLitre <= 0)
        {
            throw new DomainValidationException(
                $"Milk density must be greater than zero, but was {densityKgPerLitre} kg/L.");
        }

        Id = id;
        CanNumber = canNumber;
        QuantityKg = decimal.Round(quantityKg, 2, MidpointRounding.AwayFromZero);
        QuantityLitres = decimal.Round(QuantityKg / densityKgPerLitre, 2, MidpointRounding.AwayFromZero);
        CanLabel = $"{canLabelPrefix} {canNumber:00}";
    }

    public Guid Id { get; private set; }

    public Guid ConsignmentId { get; private set; }

    /// <summary>Label as painted on the can, composed of the society prefix and the can number, e.g. "KC 01".</summary>
    public string CanLabel { get; private set; }

    /// <summary>The can's number within the society's own numbering, e.g. 1 for "KC 01".</summary>
    public int CanNumber { get; private set; }

    /// <summary>Kilograms of milk weighed in this can — the measurement taken at the gate.</summary>
    public decimal QuantityKg { get; private set; }

    /// <summary>Litres of milk in this can, derived from <see cref="QuantityKg"/> at registration.</summary>
    public decimal QuantityLitres { get; private set; }
}
