using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quality_tests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ConsignmentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    FatPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RawLactometerReading = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TemperatureCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    WaterPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    KqColour = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    CorrectedClr = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Snf = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TotalSolids = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    StabilityGrade = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    PassedAlcoholAt = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Verdict = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    FailedParameter = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    FailedValue = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    TestedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    TestedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_tests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quality_tests_consignments_ConsignmentId",
                        column: x => x.ConsignmentId,
                        principalTable: "consignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "quality_test_alcohol_stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    QualityTestId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_test_alcohol_stages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quality_test_alcohol_stages_quality_tests_QualityTestId",
                        column: x => x.QualityTestId,
                        principalTable: "quality_tests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_quality_test_alcohol_stages_order",
                table: "quality_test_alcohol_stages",
                columns: new[] { "QualityTestId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_quality_tests_consignment",
                table: "quality_tests",
                column: "ConsignmentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quality_test_alcohol_stages");

            migrationBuilder.DropTable(
                name: "quality_tests");
        }
    }
}
