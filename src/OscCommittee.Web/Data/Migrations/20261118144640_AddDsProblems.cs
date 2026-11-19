using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDsProblems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DsProblems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DsSheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    PointsOverride = table.Column<int>(type: "int", nullable: true),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    TagsCsv = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IsPaidOnly = table.Column<bool>(type: "bit", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DsProblems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DsProblems_DsSheets_DsSheetId",
                        column: x => x.DsSheetId,
                        principalTable: "DsSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DsProblems_DsSheet_Platform_ExternalId",
                table: "DsProblems",
                columns: new[] { "DsSheetId", "Platform", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DsProblems_DsSheetId_SortOrder",
                table: "DsProblems",
                columns: new[] { "DsSheetId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DsProblems_Platform_ExternalId",
                table: "DsProblems",
                columns: new[] { "Platform", "ExternalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DsProblems");
        }
    }
}
