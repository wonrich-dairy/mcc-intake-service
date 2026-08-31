using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFactoryIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arrival_screenings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DispatchNoteId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ArrivedAtLocal = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ArrivalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SmellPassed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ColourPassed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TemperaturePassed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TemperatureCelsius = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    FailedParameters = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ScreenedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ScreenedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arrival_screenings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_arrival_screenings_dispatch_notes_DispatchNoteId",
                        column: x => x.DispatchNoteId,
                        principalTable: "dispatch_notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Reference = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    ArrivalScreeningId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DispatchNoteId = table.Column<Guid>(type: "char(36)", nullable: false),
                    BatchDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_batches_arrival_screenings_ArrivalScreeningId",
                        column: x => x.ArrivalScreeningId,
                        principalTable: "arrival_screenings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_batches_dispatch_notes_DispatchNoteId",
                        column: x => x.DispatchNoteId,
                        principalTable: "dispatch_notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_arrival_screenings_dispatch_note",
                table: "arrival_screenings",
                column: "DispatchNoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_batches_ArrivalScreeningId",
                table: "batches",
                column: "ArrivalScreeningId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_batches_date",
                table: "batches",
                column: "BatchDate");

            migrationBuilder.CreateIndex(
                name: "ux_batches_dispatch_note",
                table: "batches",
                column: "DispatchNoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_batches_reference",
                table: "batches",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropTable(
                name: "arrival_screenings");
        }
    }
}
