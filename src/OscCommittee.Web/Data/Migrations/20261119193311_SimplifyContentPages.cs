using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyContentPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContentPages_IsPublished_SortOrder",
                table: "ContentPages");

            migrationBuilder.DropIndex(
                name: "IX_ContentPages_Slug",
                table: "ContentPages");

            migrationBuilder.DropColumn(
                name: "ExternalUrl",
                table: "ContentPages");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "ContentPages");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "ContentPages");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "ContentPages");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_IsPublished_Title",
                table: "ContentPages",
                columns: new[] { "IsPublished", "Title" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContentPages_IsPublished_Title",
                table: "ContentPages");

            migrationBuilder.AddColumn<string>(
                name: "ExternalUrl",
                table: "ContentPages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "ContentPages",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "ContentPages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "ContentPages",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_IsPublished_SortOrder",
                table: "ContentPages",
                columns: new[] { "IsPublished", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_Slug",
                table: "ContentPages",
                column: "Slug",
                unique: true);
        }
    }
}
