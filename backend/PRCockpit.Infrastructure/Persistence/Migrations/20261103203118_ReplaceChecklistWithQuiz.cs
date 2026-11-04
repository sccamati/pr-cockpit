using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PRCockpit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceChecklistWithQuiz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pr_checklists");

            migrationBuilder.CreateTable(
                name: "pr_quizzes",
                columns: table => new
                {
                    Organization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PullRequestId = table.Column<int>(type: "int", nullable: false),
                    HeadCommitSha = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    QuizJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Correct = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pr_quizzes", x => new { x.Organization, x.Project, x.RepositoryId, x.PullRequestId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pr_quizzes");

            migrationBuilder.CreateTable(
                name: "pr_checklists",
                columns: table => new
                {
                    Organization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PullRequestId = table.Column<int>(type: "int", nullable: false),
                    AiReview = table.Column<bool>(type: "bit", nullable: false),
                    Architecture = table.Column<bool>(type: "bit", nullable: false),
                    Debug = table.Column<bool>(type: "bit", nullable: false),
                    DebugNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Quality = table.Column<bool>(type: "bit", nullable: false),
                    Ready = table.Column<bool>(type: "bit", nullable: false),
                    Understand = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pr_checklists", x => new { x.Organization, x.Project, x.RepositoryId, x.PullRequestId });
                });
        }
    }
}
