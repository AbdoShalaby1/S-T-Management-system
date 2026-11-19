using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class PerSheetSolveCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DsSolves_User_Platform_ExternalId",
                table: "DsSolves");

            migrationBuilder.DropColumn(
                name: "LastSeenSubmissionId",
                table: "SyncStates");

            migrationBuilder.CreateIndex(
                name: "IX_DsSolves_User_ProblemId",
                table: "DsSolves",
                columns: new[] { "UserId", "DsProblemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DsSolves_User_ProblemId",
                table: "DsSolves");

            migrationBuilder.AddColumn<string>(
                name: "LastSeenSubmissionId",
                table: "SyncStates",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DsSolves_User_Platform_ExternalId",
                table: "DsSolves",
                columns: new[] { "UserId", "Platform", "ExternalId" },
                unique: true);
        }
    }
}
