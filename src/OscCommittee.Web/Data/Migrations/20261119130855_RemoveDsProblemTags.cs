using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDsProblemTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TagsCsv",
                table: "DsProblems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TagsCsv",
                table: "DsProblems",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }
    }
}
