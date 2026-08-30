using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeTankClosureToFill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsClosed",
                table: "chilling_tanks");

            migrationBuilder.RenameColumn(
                name: "ClosedAtUtc",
                table: "chilling_tanks",
                newName: "LastClosedAtUtc");

            migrationBuilder.AddColumn<int>(
                name: "FillNumber",
                table: "tank_pours",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "FillNumber",
                table: "dispatch_sources",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "FillNumber",
                table: "chilling_tanks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0001-4d5e-8f60-000000000001"),
                column: "FillNumber",
                value: 1);

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0002-4d5e-8f60-000000000002"),
                column: "FillNumber",
                value: 1);

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0003-4d5e-8f60-000000000003"),
                column: "FillNumber",
                value: 1);

            migrationBuilder.CreateIndex(
                name: "ix_tank_pours_tank_fill",
                table: "tank_pours",
                columns: new[] { "TankId", "FillNumber" });

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_sources_tank_fill",
                table: "dispatch_sources",
                columns: new[] { "TankId", "FillNumber" });

            // Dropped last, and only once the composite above is in place to cover the same
            // column. InnoDB refuses to drop the only index covering a foreign key column, and
            // this one backs the FK from dispatch_sources.TankId. Scaffolding ordered the drop
            // first, which fails on every database that already carries the index.
            migrationBuilder.DropIndex(
                name: "IX_dispatch_sources_TankId",
                table: "dispatch_sources");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreated first, for the reason the Up drops its counterpart last: the composite
            // below is what currently covers the FK on dispatch_sources.TankId.
            migrationBuilder.CreateIndex(
                name: "IX_dispatch_sources_TankId",
                table: "dispatch_sources",
                column: "TankId");

            migrationBuilder.DropIndex(
                name: "ix_tank_pours_tank_fill",
                table: "tank_pours");

            migrationBuilder.DropIndex(
                name: "ix_dispatch_sources_tank_fill",
                table: "dispatch_sources");

            migrationBuilder.DropColumn(
                name: "FillNumber",
                table: "tank_pours");

            migrationBuilder.DropColumn(
                name: "FillNumber",
                table: "dispatch_sources");

            migrationBuilder.DropColumn(
                name: "FillNumber",
                table: "chilling_tanks");

            // The column type has to be spelled out going back: MySQL renames with CHANGE, which
            // restates the type, and the target name is no longer in the model to read it from.
            // Without it the provider cannot even script this direction.
            migrationBuilder.RenameColumn(
                name: "LastClosedAtUtc",
                table: "chilling_tanks",
                newName: "ClosedAtUtc")
                .Annotation("Relational:ColumnType", "datetime(6)");

            migrationBuilder.AddColumn<bool>(
                name: "IsClosed",
                table: "chilling_tanks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // The scaffold followed this with an UpdateData per seeded tank setting IsClosed to
            // false. The AddColumn default already writes false into every row, and the column is
            // no longer in the model, so those data operations only made this direction
            // unscriptable.
        }
    }
}
