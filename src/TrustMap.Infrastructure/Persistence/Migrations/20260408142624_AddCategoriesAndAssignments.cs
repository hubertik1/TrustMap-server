using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoriesAndAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IconName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categories_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "place_category_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlaceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CategoryId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AssignedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_place_category_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_place_category_assignments_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_place_category_assignments_places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_place_category_assignments_users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_saved_categories",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CategoryId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_categories_IsDefault_NormalizedName",
                table: "categories",
                columns: new[] { "IsDefault", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_categories_OwnerUserId_NormalizedName",
                table: "categories",
                columns: new[] { "OwnerUserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_place_category_assignments_AssignedByUserId",
                table: "place_category_assignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_place_category_assignments_CategoryId_PlaceId",
                table: "place_category_assignments",
                columns: new[] { "CategoryId", "PlaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_place_category_assignments_PlaceId_AssignedByUserId",
                table: "place_category_assignments",
                columns: new[] { "PlaceId", "AssignedByUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_saved_categories_CategoryId",
                table: "user_saved_categories",
                column: "CategoryId");

            migrationBuilder.InsertData(
                table: "categories",
                columns: new[] { "Id", "Name", "NormalizedName", "IconName", "OwnerUserId", "IsDefault", "CreatedAt", "UpdatedAt" },
                values: new object[] { new Guid("d53a109f-9617-4a0a-b95a-5ad277a30764"), "Restaurant", "restaurant", null, null, true, new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "place_category_assignments");

            migrationBuilder.DropTable(
                name: "user_saved_categories");

            migrationBuilder.DropTable(
                name: "categories");
        }
    }
}
