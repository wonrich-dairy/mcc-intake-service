using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanWeightInKilograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalQuantityKg",
                table: "consignments",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityKg",
                table: "consignment_cans",
                type: "decimal(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Consignments recorded before this migration captured litres only. Weight is now the
            // measurement of record, so rebuild it from the litres already stored at the default
            // density (1.03 kg/L) rather than leaving those rows reading zero kilograms. This is
            // the inverse of the derivation the domain applies, so the pair stays consistent.
            migrationBuilder.Sql(
                "UPDATE consignment_cans SET QuantityKg = ROUND(QuantityLitres * 1.03, 2);");

            migrationBuilder.Sql(
                """
                UPDATE consignments SET TotalQuantityKg = (
                    SELECT COALESCE(ROUND(SUM(QuantityKg), 2), 0)
                    FROM consignment_cans
                    WHERE consignment_cans.ConsignmentId = consignments.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalQuantityKg",
                table: "consignments");

            migrationBuilder.DropColumn(
                name: "QuantityKg",
                table: "consignment_cans");
        }
    }
}
