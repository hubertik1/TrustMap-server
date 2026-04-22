using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CategoryMembershipRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_categories_OwnerUserId_NormalizedName",
                table: "categories");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.RenameTable(
                name: "user_saved_categories",
                newName: "user_category_memberships");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "user_category_memberships",
                newName: "AddedAt");

            migrationBuilder.RenameIndex(
                name: "IX_user_saved_categories_CategoryId",
                table: "user_category_memberships",
                newName: "IX_user_category_memberships_CategoryId");

            migrationBuilder.AddColumn<bool>(
                name: "IsVisible",
                table: "user_category_memberships",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "user_category_memberships",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenAt",
                table: "user_category_memberships",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "user_category_memberships",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                """
                UPDATE "user_category_memberships"
                SET "Source" = (
                    SELECT CASE
                        WHEN "categories"."IsDefault" = 1 THEN 1
                        WHEN "categories"."OwnerUserId" = "user_category_memberships"."UserId" THEN 2
                        ELSE 3
                    END
                    FROM "categories"
                    WHERE "categories"."Id" = "user_category_memberships"."CategoryId"
                ),
                "UpdatedAt" = "AddedAt";
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "user_category_memberships" ("UserId", "CategoryId", "AddedAt", "Source", "IsVisible", "HiddenAt", "UpdatedAt")
                SELECT "categories"."OwnerUserId", "categories"."Id", "categories"."CreatedAt", 2, 1, NULL, "categories"."UpdatedAt"
                FROM "categories"
                WHERE "categories"."OwnerUserId" IS NOT NULL
                  AND "categories"."IsDefault" = 0
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "user_category_memberships"
                      WHERE "user_category_memberships"."UserId" = "categories"."OwnerUserId"
                        AND "user_category_memberships"."CategoryId" = "categories"."Id"
                  );
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "user_category_memberships" ("UserId", "CategoryId", "AddedAt", "Source", "IsVisible", "HiddenAt", "UpdatedAt")
                SELECT "users"."Id", "categories"."Id", "categories"."CreatedAt", 1, 1, NULL, "categories"."UpdatedAt"
                FROM "users"
                CROSS JOIN "categories"
                WHERE "categories"."IsDefault" = 1
                  AND "categories"."IsDeleted" = 0
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "user_category_memberships"
                      WHERE "user_category_memberships"."UserId" = "users"."Id"
                        AND "user_category_memberships"."CategoryId" = "categories"."Id"
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_categories_OwnerUserId_NormalizedName_IsDeleted",
                table: "categories",
                columns: new[] { "OwnerUserId", "NormalizedName", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_category_memberships_UserId_IsVisible",
                table: "user_category_memberships",
                columns: new[] { "UserId", "IsVisible" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_category_memberships_UserId_IsVisible",
                table: "user_category_memberships");

            migrationBuilder.DropIndex(
                name: "IX_categories_OwnerUserId_NormalizedName_IsDeleted",
                table: "categories");

            migrationBuilder.CreateTable(
                name: "user_saved_categories",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_saved_categories", x => new { x.UserId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_user_saved_categories_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_saved_categories_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "user_saved_categories" ("UserId", "CategoryId", "CreatedAt")
                SELECT "UserId", "CategoryId", "AddedAt"
                FROM "user_category_memberships";
                """);

            migrationBuilder.DropTable(
                name: "user_category_memberships");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "categories");

            migrationBuilder.CreateIndex(
                name: "IX_categories_OwnerUserId_NormalizedName",
                table: "categories",
                columns: new[] { "OwnerUserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_saved_categories_CategoryId",
                table: "user_saved_categories",
                column: "CategoryId");
        }
    }
}
