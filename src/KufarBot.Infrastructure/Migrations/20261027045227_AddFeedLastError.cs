using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KufarBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedLastError : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastErrorAtUtc",
                table: "PollingFeeds",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastErrorAtUtc",
                table: "PollingFeeds");
        }
    }
}
