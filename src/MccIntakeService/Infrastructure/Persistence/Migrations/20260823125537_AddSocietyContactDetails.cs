using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MccIntakeService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSocietyContactDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactNumber",
                table: "societies",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ContactPerson",
                table: "societies",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

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
        }
    }
}
