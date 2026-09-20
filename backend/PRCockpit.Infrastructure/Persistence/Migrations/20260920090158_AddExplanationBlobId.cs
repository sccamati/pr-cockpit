using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PRCockpit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExplanationBlobId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlobId",
                table: "pr_file_explanations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlobId",
                table: "pr_file_explanations");
        }
    }
}
