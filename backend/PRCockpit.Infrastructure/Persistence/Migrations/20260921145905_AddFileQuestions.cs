using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PRCockpit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pr_file_questions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FilePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    AskedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TurnJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Organization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RepositoryId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PullRequestId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pr_file_questions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pr_file_questions_Organization_Project_RepositoryId_PullRequestId_FilePath",
                table: "pr_file_questions",
                columns: new[] { "Organization", "Project", "RepositoryId", "PullRequestId", "FilePath" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pr_file_questions");
        }
    }
}
