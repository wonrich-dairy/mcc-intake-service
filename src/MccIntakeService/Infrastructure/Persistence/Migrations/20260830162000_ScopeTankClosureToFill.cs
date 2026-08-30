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
            migrationBuilder.DropIndex(
                name: "IX_dispatch_sources_TankId",
                table: "dispatch_sources");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.RenameColumn(
                name: "LastClosedAtUtc",
                table: "chilling_tanks",
                newName: "ClosedAtUtc");

            migrationBuilder.AddColumn<bool>(
                name: "IsClosed",
                table: "chilling_tanks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0001-4d5e-8f60-000000000001"),
                column: "IsClosed",
                value: false);

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0002-4d5e-8f60-000000000002"),
                column: "IsClosed",
                value: false);

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0003-4d5e-8f60-000000000003"),
                column: "IsClosed",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_dispatch_sources_TankId",
                table: "dispatch_sources",
                column: "TankId");
        }
    }
}
