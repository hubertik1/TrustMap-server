using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlacePrivacyMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_places_IdentityFingerprint",
                table: "places");

            migrationBuilder.AddColumn<bool>(
                name: "IsPubliclyListed",
                table: "places",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE places
                SET SourceType = 'ApprovedPublic'
                WHERE SourceType = 'CustomPin'
                  AND CreatedByUserId IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE places
                SET IsPubliclyListed = CASE
                    WHEN SourceType IN ('ProviderVenue', 'ApprovedPublic') THEN 1
                    ELSE 0
                END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE places
                SET CreatedByUserId = NULL
                WHERE SourceType IN ('ProviderVenue', 'ApprovedPublic');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_places_CreatedByUserId_IdentityFingerprint",
                table: "places",
                columns: new[] { "CreatedByUserId", "IdentityFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_places_IdentityFingerprint",
                table: "places",
                column: "IdentityFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_places_IsPubliclyListed",
                table: "places",
                column: "IsPubliclyListed");

            migrationBuilder.CreateIndex(
                name: "IX_places_SourceType",
                table: "places",
                column: "SourceType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_places_CreatedByUserId_IdentityFingerprint",
                table: "places");

            migrationBuilder.DropIndex(
                name: "IX_places_IdentityFingerprint",
                table: "places");

            migrationBuilder.DropIndex(
                name: "IX_places_IsPubliclyListed",
                table: "places");

            migrationBuilder.DropIndex(
                name: "IX_places_SourceType",
                table: "places");

            migrationBuilder.Sql(
                """
                UPDATE places
                SET SourceType = 'CustomPin'
                WHERE SourceType = 'ApprovedPublic';
                """);

            migrationBuilder.DropColumn(
                name: "IsPubliclyListed",
                table: "places");

            migrationBuilder.CreateIndex(
                name: "IX_places_IdentityFingerprint",
                table: "places",
                column: "IdentityFingerprint",
                unique: true);
        }
    }
}
