using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockingAndReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRemovedByModeration",
                table: "place_reviews",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRemovedByModeration",
                table: "dish_reviews",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ContentReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EmailProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeliveryAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextDeliveryAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserBlocks",
                columns: table => new
                {
                    BlockerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlockedUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBlocks", x => new { x.BlockerUserId, x.BlockedUserId });
                    table.ForeignKey(
                        name: "FK_UserBlocks_users_BlockedUserId",
                        column: x => x.BlockedUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBlocks_users_BlockerUserId",
                        column: x => x.BlockerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportPhoto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourcePhotoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviewJpeg = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportPhoto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportPhoto_ContentReports_ContentReportId",
                        column: x => x.ContentReportId,
                        principalTable: "ContentReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_EmailProcessedAtUtc_NextDeliveryAtUtc",
                table: "ContentReports",
                columns: new[] { "EmailProcessedAtUtc", "NextDeliveryAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_ReporterUserId_ReviewType_ReviewId",
                table: "ContentReports",
                columns: new[] { "ReporterUserId", "ReviewType", "ReviewId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportPhoto_ContentReportId",
                table: "ReportPhoto",
                column: "ContentReportId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockedUserId",
                table: "UserBlocks",
                column: "BlockedUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportPhoto");

            migrationBuilder.DropTable(
                name: "UserBlocks");

            migrationBuilder.DropTable(
                name: "ContentReports");

            migrationBuilder.DropColumn(
                name: "IsRemovedByModeration",
                table: "place_reviews");

            migrationBuilder.DropColumn(
                name: "IsRemovedByModeration",
                table: "dish_reviews");
        }
    }
}
