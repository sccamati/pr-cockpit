using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PRCockpit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileExplanations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pr_file_explanations",
                columns: table => new
                {
                    Organization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PullRequestId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    HeadCommitSha = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pr_file_explanations", x => new { x.Organization, x.Project, x.RepositoryId, x.PullRequestId, x.FilePath });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pr_file_explanations");
        }
    }
}
