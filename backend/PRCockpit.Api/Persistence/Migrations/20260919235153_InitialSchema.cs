using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PRCockpit.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pr_checklists",
                columns: table => new
                {
                    Organization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PullRequestId = table.Column<int>(type: "int", nullable: false),
                    AiReview = table.Column<bool>(type: "bit", nullable: false),
                    Quality = table.Column<bool>(type: "bit", nullable: false),
                    Understand = table.Column<bool>(type: "bit", nullable: false),
                    Architecture = table.Column<bool>(type: "bit", nullable: false),
                    Debug = table.Column<bool>(type: "bit", nullable: false),
                    Ready = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pr_checklists", x => new { x.Organization, x.Project, x.RepositoryId, x.PullRequestId });
                });

            migrationBuilder.CreateTable(
                name: "pr_file_reviews",
                columns: table => new
                {
                    Organization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PullRequestId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ReviewedBlobId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ReviewedHeadSha = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ChangedFilesCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pr_file_reviews", x => new { x.Organization, x.Project, x.RepositoryId, x.PullRequestId, x.FilePath });
                });

            migrationBuilder.CreateTable(
                name: "pr_reading_paths",
                columns: table => new
                {
                    Organization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PullRequestId = table.Column<int>(type: "int", nullable: false),
                    PathsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pr_reading_paths", x => new { x.Organization, x.Project, x.RepositoryId, x.PullRequestId });
                });

            migrationBuilder.CreateTable(
                name: "pr_summaries",
                columns: table => new
                {
                    Organization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PullRequestId = table.Column<int>(type: "int", nullable: false),
                    HeadCommitSha = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pr_summaries", x => new { x.Organization, x.Project, x.RepositoryId, x.PullRequestId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pr_checklists");

            migrationBuilder.DropTable(
                name: "pr_file_reviews");

            migrationBuilder.DropTable(
                name: "pr_reading_paths");

            migrationBuilder.DropTable(
                name: "pr_summaries");
        }
    }
}
