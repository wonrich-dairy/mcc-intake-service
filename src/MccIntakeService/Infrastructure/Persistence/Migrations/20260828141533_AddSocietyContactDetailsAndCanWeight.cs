using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSocietyContactDetailsAndCanWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactNumber",
                table: "societies",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPerson",
                table: "societies",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true);

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

            migrationBuilder.UpdateData(
                table: "societies",
                keyColumn: "Id",
                keyValue: new Guid("6f0f6f1a-0001-4a2b-9c3d-000000000001"),
                columns: new[] { "ContactNumber", "ContactPerson" },
                values: new object[] { "+94 81 222 3344", "Sunil Perera" });

            migrationBuilder.UpdateData(
                table: "societies",
                keyColumn: "Id",
                keyValue: new Guid("6f0f6f1a-0002-4a2b-9c3d-000000000002"),
                columns: new[] { "ContactNumber", "ContactPerson" },
                values: new object[] { "+94 66 222 5566", "Kamala Ranasinghe" });

            migrationBuilder.UpdateData(
                table: "societies",
                keyColumn: "Id",
                keyValue: new Guid("6f0f6f1a-0003-4a2b-9c3d-000000000003"),
                columns: new[] { "ContactNumber", "ContactPerson" },
                values: new object[] { "+94 52 222 7788", "Ravi Kumar" });

            migrationBuilder.UpdateData(
                table: "societies",
                keyColumn: "Id",
                keyValue: new Guid("6f0f6f1a-0004-4a2b-9c3d-000000000004"),
                columns: new[] { "ContactNumber", "ContactPerson" },
                values: new object[] { "+94 55 222 9900", "Anoma Jayasuriya" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactNumber",
                table: "societies");

            migrationBuilder.DropColumn(
                name: "ContactPerson",
                table: "societies");

            migrationBuilder.DropColumn(
                name: "TotalQuantityKg",
                table: "consignments");

            migrationBuilder.DropColumn(
                name: "QuantityKg",
                table: "consignment_cans");
        }
    }
}
