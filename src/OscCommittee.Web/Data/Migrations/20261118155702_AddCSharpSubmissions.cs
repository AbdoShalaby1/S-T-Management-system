using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OscCommittee.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCSharpSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CSharpSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNo = table.Column<int>(type: "int", nullable: false),
                    StoredFolderPath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BundlePath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    IgnoredFileCount = table.Column<int>(type: "int", nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    BundleChars = table.Column<int>(type: "int", nullable: false),
                    TruncatedFiles = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: true),
                    AiScore = table.Column<int>(type: "int", nullable: true),
                    IsManualScore = table.Column<bool>(type: "bit", nullable: false),
                    FeedbackJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    Verdict = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsLate = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GradedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GradingModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BoardNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CSharpSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CSharpSubmissions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CSharpSubmissions_CSharpTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "CSharpTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CSharpSubmissions_Status",
                table: "CSharpSubmissions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CSharpSubmissions_Task_User_AttemptNo",
                table: "CSharpSubmissions",
                columns: new[] { "TaskId", "UserId", "AttemptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CSharpSubmissions_TaskId_UserId",
                table: "CSharpSubmissions",
                columns: new[] { "TaskId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CSharpSubmissions_UserId_SubmittedAtUtc",
                table: "CSharpSubmissions",
                columns: new[] { "UserId", "SubmittedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CSharpSubmissions");
        }
    }
}
