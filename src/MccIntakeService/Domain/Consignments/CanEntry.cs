namespace MccIntakeService.Domain.Consignments;

/// <summary>
/// One line of the officer's can sheet: the society can number and the litres received in it.
/// The full can label is derived from the society's prefix when the can is created.
/// </summary>
/// <param name="CanNumber">Can number within the society's numbering, e.g. 1 for "KC 01".</param>
/// <param name="QuantityLitres">Litres of milk received in the can.</param>
public sealed record CanEntry(int CanNumber, decimal QuantityLitres);
