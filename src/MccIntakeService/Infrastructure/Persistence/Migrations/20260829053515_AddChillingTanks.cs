using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChillingTanks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chilling_tanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    CapacityLitres = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chilling_tanks", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tank_pours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    TankId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConsignmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    QuantityLitres = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    QuantityKg = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PouredBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    PouredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PourDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tank_pours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tank_pours_chilling_tanks_TankId",
                        column: x => x.TankId,
                        principalTable: "chilling_tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tank_pours_consignments_ConsignmentId",
                        column: x => x.ConsignmentId,
                        principalTable: "consignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "chilling_tanks",
                columns: new[] { "Id", "CapacityLitres", "Code", "Name" },
                values: new object[,]
                {
                    { new Guid("9a1c2b30-0001-4d5e-8f60-000000000001"), 5000m, "T1", "Chilling Tank 1" },
                    { new Guid("9a1c2b30-0002-4d5e-8f60-000000000002"), 5000m, "T2", "Chilling Tank 2" },
                    { new Guid("9a1c2b30-0003-4d5e-8f60-000000000003"), 3000m, "T3", "Chilling Tank 3" }
                });

            migrationBuilder.CreateIndex(
                name: "ux_chilling_tanks_code",
                table: "chilling_tanks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tank_pours_tank_date",
                table: "tank_pours",
                columns: new[] { "TankId", "PourDate" });

            migrationBuilder.CreateIndex(
                name: "ux_tank_pours_consignment",
                table: "tank_pours",
                column: "ConsignmentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tank_pours");

            migrationBuilder.DropTable(
                name: "chilling_tanks");
        }
    }
}
