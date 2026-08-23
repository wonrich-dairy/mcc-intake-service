using MccIntakeService.Domain.Common;

namespace MccIntakeService.Domain.Consignments;

/// <summary>
/// A single physical can delivered as part of a consignment, identified by the society's
/// can label (e.g. "KC 01") and carrying the quantity of milk received in it.
/// </summary>
public class ConsignmentCan
{
    /// <summary>Maximum litres one can may hold; guards against fat-fingered entry at the gate.</summary>
    public const decimal MaxQuantityLitres = 1_000m;

    /// <summary>EF Core materialisation constructor.</summary>
    private ConsignmentCan()
    {
        CanLabel = string.Empty;
    }

    internal ConsignmentCan(Guid id, string canLabelPrefix, int canNumber, decimal quantityLitres)
    {
        if (canNumber <= 0)
        {
            throw new DomainValidationException($"Can number must be greater than zero, but was {canNumber}.");
        }

        if (quantityLitres <= 0)
        {
            throw new DomainValidationException(
                $"Quantity for can {canNumber:00} must be greater than zero litres.");
        }

        if (quantityLitres > MaxQuantityLitres)
        {
            throw new DomainValidationException(
                $"Quantity for can {canNumber:00} is {quantityLitres} L, which exceeds the {MaxQuantityLitres} L limit for a single can.");
        }

        Id = id;
        CanNumber = canNumber;
        QuantityLitres = decimal.Round(quantityLitres, 2, MidpointRounding.AwayFromZero);
        CanLabel = $"{canLabelPrefix} {canNumber:00}";
    }

    public Guid Id { get; private set; }

    public Guid ConsignmentId { get; private set; }

    /// <summary>Label as painted on the can, composed of the society prefix and the can number, e.g. "KC 01".</summary>
    public string CanLabel { get; private set; }

    /// <summary>The can's number within the society's own numbering, e.g. 1 for "KC 01".</summary>
    public int CanNumber { get; private set; }

    /// <summary>Litres of milk received in this can.</summary>
    public decimal QuantityLitres { get; private set; }
}
