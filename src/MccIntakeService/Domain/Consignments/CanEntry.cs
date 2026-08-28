namespace MccIntakeService.Domain.Consignments;

/// <summary>
/// One line of the officer's can sheet: the society can number and the weight received in it.
/// The full can label is derived from the society's prefix when the can is created.
/// </summary>
/// <remarks>
/// Weight is what the sheet records, because the can goes on a scale at the gate. Litres are
/// derived from it, never entered.
/// </remarks>
/// <param name="CanNumber">Can number within the society's numbering, e.g. 1 for "KC 01".</param>
/// <param name="QuantityKg">Kilograms of milk weighed in the can.</param>
public sealed record CanEntry(int CanNumber, decimal QuantityKg);
