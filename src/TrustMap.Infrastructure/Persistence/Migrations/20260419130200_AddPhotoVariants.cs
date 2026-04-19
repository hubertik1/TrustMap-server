using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediumStorageKey",
                table: "photos",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediumUrl",
                table: "photos",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailStorageKey",
                table: "photos",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "photos",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediumStorageKey",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "MediumUrl",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "ThumbnailStorageKey",
                table: "photos");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "photos");
        }
    }
}
