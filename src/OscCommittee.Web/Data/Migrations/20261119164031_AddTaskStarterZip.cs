using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskStarterZip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StarterZipFileName",
                table: "CSharpTasks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StarterZipPath",
                table: "CSharpTasks",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StarterZipSizeBytes",
                table: "CSharpTasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StarterZipFileName",
                table: "CSharpTasks");

            migrationBuilder.DropColumn(
                name: "StarterZipPath",
                table: "CSharpTasks");

            migrationBuilder.DropColumn(
                name: "StarterZipSizeBytes",
                table: "CSharpTasks");
        }
    }
}
