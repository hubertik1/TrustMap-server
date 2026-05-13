using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnumCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // If existing rows contain undefined enum values, these constraints should fail rather than guessing privacy or workflow state.
            migrationBuilder.AddCheckConstraint(
                name: "CK_users_friend_list_visibility",
                table: "users",
                sql: "\"FriendListVisibility\" IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_profile_picture_visibility",
                table: "users",
                sql: "\"ProfilePictureVisibility\" IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_profile_visibility",
                table: "users",
                sql: "\"ProfileVisibility\" IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_review_visibility",
                table: "users",
                sql: "\"ReviewVisibility\" IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_status",
                table: "users",
                sql: "\"Status\" IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_user_category_memberships_source",
                table: "user_category_memberships",
                sql: "\"Source\" IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_places_source_type",
                table: "places",
                sql: "\"SourceType\" IN ('ProviderVenue', 'CustomPin', 'ApprovedPublic')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_place_reviews_visibility",
                table: "place_reviews",
                sql: "\"Visibility\" IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notifications_type",
                table: "notifications",
                sql: "\"Type\" IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_friend_requests_status",
                table: "friend_requests",
                sql: "\"Status\" IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_dish_reviews_visibility",
                table: "dish_reviews",
                sql: "\"Visibility\" IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_users_friend_list_visibility",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_profile_picture_visibility",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_profile_visibility",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_review_visibility",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_status",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_user_category_memberships_source",
                table: "user_category_memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_places_source_type",
                table: "places");

            migrationBuilder.DropCheckConstraint(
                name: "CK_place_reviews_visibility",
                table: "place_reviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notifications_type",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_friend_requests_status",
                table: "friend_requests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_dish_reviews_visibility",
                table: "dish_reviews");
        }
    }
}
