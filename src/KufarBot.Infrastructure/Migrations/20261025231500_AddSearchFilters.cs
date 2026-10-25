using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KufarBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FiltersJson",
                table: "UserSearches",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FiltersJson",
                table: "UserSearches");
        }
    }
}
