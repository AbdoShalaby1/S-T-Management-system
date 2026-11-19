using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSubmissionBoardNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardNote",
                table: "CSharpSubmissions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoardNote",
                table: "CSharpSubmissions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }
    }
}
