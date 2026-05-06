using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFriendListPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FriendsVisibleToOthers",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FriendsVisibleToOthers",
                table: "users");
        }
    }
}
