using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomPlaceDisplayNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomDisplayName",
                table: "places",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomDisplayNameSetByUserId",
                table: "places",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustomDisplayNameUpdatedAt",
                table: "places",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedCustomDisplayName",
                table: "places",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "places",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "CustomPin");

            migrationBuilder.Sql(
                """
                UPDATE places
                SET SourceType = CASE
                    WHEN Provider IS NOT NULL
                         AND trim(Provider) <> ''
                         AND lower(trim(Provider)) <> 'manual'
                         AND ProviderPlaceId IS NOT NULL
                         AND trim(ProviderPlaceId) <> ''
                    THEN 'ProviderVenue'
                    ELSE 'CustomPin'
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_places_CustomDisplayNameSetByUserId",
                table: "places",
                column: "CustomDisplayNameSetByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_places_NormalizedCustomDisplayName",
                table: "places",
                column: "NormalizedCustomDisplayName");

            migrationBuilder.AddForeignKey(
                name: "FK_places_users_CustomDisplayNameSetByUserId",
                table: "places",
                column: "CustomDisplayNameSetByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_places_users_CustomDisplayNameSetByUserId",
                table: "places");

            migrationBuilder.DropIndex(
                name: "IX_places_CustomDisplayNameSetByUserId",
                table: "places");

            migrationBuilder.DropIndex(
                name: "IX_places_NormalizedCustomDisplayName",
                table: "places");

            migrationBuilder.DropColumn(
                name: "CustomDisplayName",
                table: "places");

            migrationBuilder.DropColumn(
                name: "CustomDisplayNameSetByUserId",
                table: "places");

            migrationBuilder.DropColumn(
                name: "CustomDisplayNameUpdatedAt",
                table: "places");

            migrationBuilder.DropColumn(
                name: "NormalizedCustomDisplayName",
                table: "places");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "places");
        }
    }
}
