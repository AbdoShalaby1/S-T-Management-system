using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDsSolvesAndPointEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DsSolves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DsProblemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlatformSubmissionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PlatformUrl = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IsLate = table.Column<bool>(type: "bit", nullable: false),
                    IsManual = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DsSolves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DsSolves_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DsSolves_DsProblems_DsProblemId",
                        column: x => x.DsProblemId,
                        principalTable: "DsProblems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PointEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonthSheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BasePoints = table.Column<int>(type: "int", nullable: false),
                    FinalPoints = table.Column<int>(type: "int", nullable: false),
                    IsManualOverride = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PointEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PointEntries_MonthSheets_MonthSheetId",
                        column: x => x.MonthSheetId,
                        principalTable: "MonthSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DsSolves_DsProblemId",
                table: "DsSolves",
                column: "DsProblemId");

            migrationBuilder.CreateIndex(
                name: "IX_DsSolves_User_Platform_ExternalId",
                table: "DsSolves",
                columns: new[] { "UserId", "Platform", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PointEntries_Category_SourceId",
                table: "PointEntries",
                columns: new[] { "Category", "SourceId" },
                unique: true,
                filter: "[SourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PointEntries_MonthSheetId_UserId",
                table: "PointEntries",
                columns: new[] { "MonthSheetId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PointEntries_UserId",
                table: "PointEntries",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DsSolves");

            migrationBuilder.DropTable(
                name: "PointEntries");
        }
    }
}
