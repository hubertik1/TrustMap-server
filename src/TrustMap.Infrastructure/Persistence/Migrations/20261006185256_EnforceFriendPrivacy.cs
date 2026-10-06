using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceFriendPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE users SET "ReviewVisibility" = 2, "ProfileVisibility" = 3,
                    "FriendListVisibility" = CASE WHEN "FriendListVisibility" = 1 THEN 1 ELSE 2 END
                WHERE "Status" = 1;
                UPDATE place_reviews SET "Visibility" = 2 WHERE "Visibility" IN (3, 4);
                UPDATE dish_reviews SET "Visibility" = 2 WHERE "Visibility" IN (3, 4);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Previous privacy choices cannot be reconstructed after normalization.
        }
    }
}
