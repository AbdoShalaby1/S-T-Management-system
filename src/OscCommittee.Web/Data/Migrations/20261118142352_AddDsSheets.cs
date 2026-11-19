using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDsSheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DsSheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonthSheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DescriptionMd = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DsSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DsSheets_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DsSheets_MonthSheets_MonthSheetId",
                        column: x => x.MonthSheetId,
                        principalTable: "MonthSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DsSheets_CreatedByUserId",
                table: "DsSheets",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DsSheets_MonthSheetId_SortOrder",
                table: "DsSheets",
                columns: new[] { "MonthSheetId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DsSheets_OneActivePerMonth",
                table: "DsSheets",
                column: "MonthSheetId",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DsSheets");
        }
    }
}
