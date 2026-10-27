using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KufarBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DraftJson",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DraftJson",
                table: "Users");
        }
    }
}
