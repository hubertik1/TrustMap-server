using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResetPlacesForRestaurantsOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "photos";
                DELETE FROM "dish_reviews";
                DELETE FROM "place_reviews";
                DELETE FROM "place_category_assignments";
                DELETE FROM "places";
                DELETE FROM "user_saved_categories";
                DELETE FROM "categories" WHERE "IsDefault" = 0;
                """);

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111101"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111102"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111103"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111104"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111105"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111106"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111107"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111108"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111109"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-11111111110a"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-11111111110b"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-11111111110c"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-11111111110d"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-11111111110e"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-11111111110f"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111110"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111112"));

            migrationBuilder.DeleteData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111113"));

            migrationBuilder.UpdateData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("d53a109f-9617-4a0a-b95a-5ad277a30764"),
                columns: new[] { "Name", "NormalizedName" },
                values: new object[] { "Restaurants", "restaurants" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "categories",
                keyColumn: "Id",
                keyValue: new Guid("d53a109f-9617-4a0a-b95a-5ad277a30764"),
                columns: new[] { "Name", "NormalizedName" },
                values: new object[] { "Restaurant", "restaurant" });

            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "Id", "CreatedAt", "IconName", "IsDefault", "Name", "NormalizedName", "OwnerUserId", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111101"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "BBQ Spots", "bbq spots", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111102"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Football Fields", "football fields", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111103"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Cafés", "cafes", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111104"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Bars", "bars", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111105"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Parks", "parks", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111106"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Gyms", "gyms", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111107"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Playgrounds", "playgrounds", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111108"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Basketball Courts", "basketball courts", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111109"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Tennis Courts", "tennis courts", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-11111111110a"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Swimming Pools", "swimming pools", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-11111111110b"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Picnic Spots", "picnic spots", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-11111111110c"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Beaches", "beaches", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-11111111110d"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Lakes", "lakes", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-11111111110e"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Shopping Places", "shopping places", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-11111111110f"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Coworking Spaces", "coworking spaces", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111110"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Study Spots", "study spots", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111111"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Dog Parks", "dog parks", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111112"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Cinemas", "cinemas", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111113"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Museums", "museums", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) }
                });
        }
    }
}
