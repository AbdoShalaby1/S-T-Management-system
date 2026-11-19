using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskReferenceFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceSolution",
                table: "CSharpTasks");

            migrationBuilder.DropColumn(
                name: "ReferenceSolutionFileName",
                table: "CSharpTasks");

            migrationBuilder.DropColumn(
                name: "RubricNotes",
                table: "CSharpTasks");

            migrationBuilder.DropColumn(
                name: "ShowReferenceToMembers",
                table: "CSharpTasks");

            migrationBuilder.AddColumn<int>(
                name: "ReferenceFileCount",
                table: "CSharpTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceFolderPath",
                table: "CSharpTasks",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceFileCount",
                table: "CSharpTasks");

            migrationBuilder.DropColumn(
                name: "ReferenceFolderPath",
                table: "CSharpTasks");

            migrationBuilder.AddColumn<string>(
                name: "ReferenceSolution",
                table: "CSharpTasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceSolutionFileName",
                table: "CSharpTasks",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RubricNotes",
                table: "CSharpTasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowReferenceToMembers",
                table: "CSharpTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
