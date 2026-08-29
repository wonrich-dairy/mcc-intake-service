using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "chilling_tanks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosed",
                table: "chilling_tanks",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "dispatch_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Reference = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    BowserRegistration = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DriverName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    DispatchedAtLocal = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DispatchDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalQuantityLitres = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    FatPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Snf = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    KqColour = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    StabilityGrade = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    TemperatureCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    DispatchedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispatch_notes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "dispatch_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DispatchNoteId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TankId = table.Column<Guid>(type: "char(36)", nullable: false),
                    QuantityLitres = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispatch_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dispatch_sources_chilling_tanks_TankId",
                        column: x => x.TankId,
                        principalTable: "chilling_tanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dispatch_sources_dispatch_notes_DispatchNoteId",
                        column: x => x.DispatchNoteId,
                        principalTable: "dispatch_notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0001-4d5e-8f60-000000000001"),
                columns: new[] { "ClosedAtUtc", "IsClosed" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0002-4d5e-8f60-000000000002"),
                columns: new[] { "ClosedAtUtc", "IsClosed" },
                values: new object[] { null, false });

            migrationBuilder.UpdateData(
                table: "chilling_tanks",
                keyColumn: "Id",
                keyValue: new Guid("9a1c2b30-0003-4d5e-8f60-000000000003"),
                columns: new[] { "ClosedAtUtc", "IsClosed" },
                values: new object[] { null, false });

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_notes_date",
                table: "dispatch_notes",
                column: "DispatchDate");

            migrationBuilder.CreateIndex(
                name: "ux_dispatch_notes_reference",
                table: "dispatch_notes",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dispatch_sources_TankId",
                table: "dispatch_sources",
                column: "TankId");

            migrationBuilder.CreateIndex(
                name: "ux_dispatch_sources_note_tank",
                table: "dispatch_sources",
                columns: new[] { "DispatchNoteId", "TankId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispatch_sources");

            migrationBuilder.DropTable(
                name: "dispatch_notes");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "chilling_tanks");

            migrationBuilder.DropColumn(
                name: "IsClosed",
                table: "chilling_tanks");
        }
    }
}
