using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PRCockpit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWalkthroughPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HeadCommitSha",
                table: "pr_reading_paths",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "pr_reading_paths",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeadCommitSha",
                table: "pr_reading_paths");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "pr_reading_paths");
        }
    }
}
