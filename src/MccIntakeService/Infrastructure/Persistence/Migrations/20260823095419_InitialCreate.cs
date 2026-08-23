using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "societies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CanLabelPrefix = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_societies", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "consignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Reference = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SocietyId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ArrivalAtLocal = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ArrivalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalQuantityLitres = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RegisteredBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consignments_societies_SocietyId",
                        column: x => x.SocietyId,
                        principalTable: "societies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "consignment_cans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConsignmentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CanLabel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CanNumber = table.Column<int>(type: "int", nullable: false),
                    QuantityLitres = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consignment_cans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consignment_cans_consignments_ConsignmentId",
                        column: x => x.ConsignmentId,
                        principalTable: "consignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "societies",
                columns: new[] { "Id", "CanLabelPrefix", "Code", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("6f0f6f1a-0001-4a2b-9c3d-000000000001"), "KC", "KC", true, "Kandy Co-operative Dairy Society" },
                    { new Guid("6f0f6f1a-0002-4a2b-9c3d-000000000002"), "MT", "MT", true, "Matale Farmers' Milk Society" },
                    { new Guid("6f0f6f1a-0003-4a2b-9c3d-000000000003"), "NW", "NW", true, "Nuwara Eliya Highland Society" },
                    { new Guid("6f0f6f1a-0004-4a2b-9c3d-000000000004"), "BD", "BD", true, "Badulla Uva Milk Society" }
                });

            migrationBuilder.CreateIndex(
                name: "ux_consignment_cans_consignment_can_number",
                table: "consignment_cans",
                columns: new[] { "ConsignmentId", "CanNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_consignments_society_arrival_date",
                table: "consignments",
                columns: new[] { "SocietyId", "ArrivalDate" });

            migrationBuilder.CreateIndex(
                name: "ux_consignments_reference",
                table: "consignments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_societies_code",
                table: "societies",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consignment_cans");

            migrationBuilder.DropTable(
                name: "consignments");

            migrationBuilder.DropTable(
                name: "societies");
        }
    }
}
