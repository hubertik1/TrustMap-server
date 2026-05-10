using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    PlaceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlaceReviewId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DishReviewId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FriendRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeduplicationKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.CheckConstraint("CK_notifications_references_match_type", "(\"Type\" = 1\n    AND \"PlaceId\" IS NOT NULL\n    AND \"PlaceReviewId\" IS NOT NULL\n    AND \"DishReviewId\" IS NULL\n    AND \"FriendRequestId\" IS NULL)\nOR (\"Type\" = 2\n    AND \"PlaceId\" IS NOT NULL\n    AND \"PlaceReviewId\" IS NULL\n    AND \"DishReviewId\" IS NOT NULL\n    AND \"FriendRequestId\" IS NULL)\nOR (\"Type\" = 3\n    AND \"PlaceId\" IS NULL\n    AND \"PlaceReviewId\" IS NULL\n    AND \"DishReviewId\" IS NULL\n    AND \"FriendRequestId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_notifications_dish_reviews_DishReviewId",
                        column: x => x.DishReviewId,
                        principalTable: "dish_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notifications_friend_requests_FriendRequestId",
                        column: x => x.FriendRequestId,
                        principalTable: "friend_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_place_reviews_PlaceReviewId",
                        column: x => x.PlaceReviewId,
                        principalTable: "place_reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notifications_places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_ActorUserId_CreatedAt",
                table: "notifications",
                columns: new[] { "ActorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_DeduplicationKey",
                table: "notifications",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_DishReviewId",
                table: "notifications",
                column: "DishReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_FriendRequestId",
                table: "notifications",
                column: "FriendRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_PlaceId",
                table: "notifications",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_PlaceReviewId",
                table: "notifications",
                column: "PlaceReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RecipientUserId_CreatedAt",
                table: "notifications",
                columns: new[] { "RecipientUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RecipientUserId_ReadAt_CreatedAt",
                table: "notifications",
                columns: new[] { "RecipientUserId", "ReadAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");
        }
    }
}
