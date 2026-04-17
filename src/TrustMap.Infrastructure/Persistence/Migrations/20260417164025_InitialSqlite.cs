using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Handle = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NormalizedHandle = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedDisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Bio = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "apple_identities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    EmailVerified = table.Column<bool>(type: "INTEGER", nullable: true),
                    IsPrivateEmail = table.Column<bool>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apple_identities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_apple_identities_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IconName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "friend_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiverUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveRequestKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friend_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_friend_requests_users_ReceiverUserId",
                        column: x => x.ReceiverUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_friend_requests_users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserAId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserBId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PairKey = table.Column<string>(type: "TEXT", maxLength: 65, nullable: false),
                    SourceRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friendships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_friendships_users_UserAId",
                        column: x => x.UserAId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_friendships_users_UserBId",
                        column: x => x.UserBId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "places",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NormalizedAddress = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    IdentityFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ProviderPlaceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_places", x => x.Id);
                    table.ForeignKey(
                        name: "FK_places_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedByIpAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RevokedByIpAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "place_category_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "place_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Visibility = table.Column<int>(type: "INTEGER", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_place_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_place_reviews_places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_place_reviews_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dish_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaceReviewId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Visibility = table.Column<int>(type: "INTEGER", nullable: false),
                    DishName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    PriceAmount = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dish_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dish_reviews_place_reviews_PlaceReviewId",
                        column: x => x.PlaceReviewId,
                        principalTable: "place_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_dish_reviews_places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dish_reviews_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "photos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaceReviewId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DishReviewId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StorageProvider = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PublicUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photos", x => x.Id);
                    table.CheckConstraint("ck_photos_single_parent", "((PlaceReviewId IS NOT NULL AND DishReviewId IS NULL) OR (PlaceReviewId IS NULL AND DishReviewId IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_photos_dish_reviews_DishReviewId",
                        column: x => x.DishReviewId,
                        principalTable: "dish_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_photos_place_reviews_PlaceReviewId",
                        column: x => x.PlaceReviewId,
                        principalTable: "place_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_photos_places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_photos_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    { new Guid("11111111-1111-1111-1111-111111111113"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Museums", "museums", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("d53a109f-9617-4a0a-b95a-5ad277a30764"), new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Restaurant", "restaurant", null, new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_apple_identities_Subject",
                table: "apple_identities",
                column: "Subject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_apple_identities_UserId",
                table: "apple_identities",
                column: "UserId",
                unique: true);

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
                name: "IX_dish_reviews_AuthorUserId_UpdatedAt",
                table: "dish_reviews",
                columns: new[] { "AuthorUserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_dish_reviews_PlaceId_UpdatedAt",
                table: "dish_reviews",
                columns: new[] { "PlaceId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_dish_reviews_PlaceReviewId",
                table: "dish_reviews",
                column: "PlaceReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_dish_reviews_Visibility_UpdatedAt",
                table: "dish_reviews",
                columns: new[] { "Visibility", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_friend_requests_ActiveRequestKey",
                table: "friend_requests",
                column: "ActiveRequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_friend_requests_ReceiverUserId_Status_CreatedAt",
                table: "friend_requests",
                columns: new[] { "ReceiverUserId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_friend_requests_SenderUserId_Status_CreatedAt",
                table: "friend_requests",
                columns: new[] { "SenderUserId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_friendships_PairKey",
                table: "friendships",
                column: "PairKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_friendships_UserAId",
                table: "friendships",
                column: "UserAId");

            migrationBuilder.CreateIndex(
                name: "IX_friendships_UserBId",
                table: "friendships",
                column: "UserBId");

            migrationBuilder.CreateIndex(
                name: "IX_photos_DishReviewId",
                table: "photos",
                column: "DishReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_photos_OwnerUserId_CreatedAt",
                table: "photos",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_photos_PlaceId",
                table: "photos",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_photos_PlaceReviewId",
                table: "photos",
                column: "PlaceReviewId");

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
                name: "IX_place_reviews_AuthorUserId_UpdatedAt",
                table: "place_reviews",
                columns: new[] { "AuthorUserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_place_reviews_PlaceId_AuthorUserId",
                table: "place_reviews",
                columns: new[] { "PlaceId", "AuthorUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_place_reviews_PlaceId_UpdatedAt",
                table: "place_reviews",
                columns: new[] { "PlaceId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_place_reviews_Visibility_UpdatedAt",
                table: "place_reviews",
                columns: new[] { "Visibility", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_places_CreatedByUserId",
                table: "places",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_places_IdentityFingerprint",
                table: "places",
                column: "IdentityFingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_places_Latitude_Longitude",
                table: "places",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_places_NormalizedName",
                table: "places",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_places_Provider_ProviderPlaceId",
                table: "places",
                columns: new[] { "Provider", "ProviderPlaceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId_ExpiresAt",
                table: "refresh_tokens",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_user_saved_categories_CategoryId",
                table: "user_saved_categories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedDisplayName",
                table: "users",
                column: "NormalizedDisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedHandle",
                table: "users",
                column: "NormalizedHandle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apple_identities");

            migrationBuilder.DropTable(
                name: "friend_requests");

            migrationBuilder.DropTable(
                name: "friendships");

            migrationBuilder.DropTable(
                name: "photos");

            migrationBuilder.DropTable(
                name: "place_category_assignments");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "user_saved_categories");

            migrationBuilder.DropTable(
                name: "dish_reviews");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "place_reviews");

            migrationBuilder.DropTable(
                name: "places");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
