using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTankManagementAndTemperatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "chilling_tanks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "tank_temperature_readings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TankId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FillNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Celsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RecordedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReadingDate = table.Column<DateTime>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tank_temperature_readings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tank_temperature_readings_chilling_tanks_TankId",
                        column: x => x.TankId,
                        principalTable: "chilling_tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0001-4d5e-8f60-000000000001"),
                columns: new string[0],
                values: new object[0]);

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0002-4d5e-8f60-000000000002"),
                columns: new string[0],
                values: new object[0]);

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0003-4d5e-8f60-000000000003"),
                columns: new string[0],
                values: new object[0]);

            migrationBuilder.CreateIndex(
                name: "ix_tank_temperature_readings_tank_time",
                table: "tank_temperature_readings",
                columns: new[] { "TankId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tank_temperature_readings");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "chilling_tanks");
        }
    }
}
