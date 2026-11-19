using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGradingTextCaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Verdict",
                table: "CSharpSubmissions",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "CSharpSubmissions",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: true);

            // Rows saved under the old caps still have the full AI text in FeedbackJson.
            // Restore it so existing reviews stop mid-sentence too.
            migrationBuilder.Sql(
                """
                UPDATE submissions
                SET Summary = saved.summary
                FROM CSharpSubmissions AS submissions
                CROSS APPLY OPENJSON(submissions.FeedbackJson)
                    WITH (summary nvarchar(max) '$.summary') AS saved
                WHERE submissions.FeedbackJson IS NOT NULL
                  AND LEN(submissions.Summary) = 600
                  AND LEN(saved.summary) > 600;

                UPDATE submissions
                SET Verdict = saved.verdict
                FROM CSharpSubmissions AS submissions
                CROSS APPLY OPENJSON(submissions.FeedbackJson)
                    WITH (verdict nvarchar(max) '$.verdict') AS saved
                WHERE submissions.FeedbackJson IS NOT NULL
                  AND LEN(submissions.Verdict) = 40
                  AND LEN(saved.verdict) > 40;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Verdict",
                table: "CSharpSubmissions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "CSharpSubmissions",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
