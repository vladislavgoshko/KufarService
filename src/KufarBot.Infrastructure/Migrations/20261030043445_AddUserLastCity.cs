using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KufarBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLastCity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastCityId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastCityName",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastCityId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastCityName",
                table: "Users");
        }
    }
}
