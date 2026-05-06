using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPrivacySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FriendListVisibility",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "ProfilePictureVisibility",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "ProfileVisibility",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "ReviewVisibility",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FriendListVisibility",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ProfilePictureVisibility",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ProfileVisibility",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ReviewVisibility",
                table: "users");
        }
    }
}
