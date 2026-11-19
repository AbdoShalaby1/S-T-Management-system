using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGradingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BundleHash",
                table: "CSharpSubmissions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputChars",
                table: "CSharpSubmissions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromptVersion",
                table: "CSharpSubmissions",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BundleHash",
                table: "CSharpSubmissions");

            migrationBuilder.DropColumn(
                name: "InputChars",
                table: "CSharpSubmissions");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                table: "CSharpSubmissions");
        }
    }
}
