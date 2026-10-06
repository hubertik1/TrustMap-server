using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppleRevocationAndDeletionOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                table: "apple_identities",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedRefreshToken",
                table: "apple_identities",
                type: "TEXT",
                maxLength: 16384,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_deletion_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_deletion_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_deletion_jobs_NextAttemptAtUtc",
                table: "account_deletion_jobs",
                column: "NextAttemptAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_deletion_jobs");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "apple_identities");

            migrationBuilder.DropColumn(
                name: "ProtectedRefreshToken",
                table: "apple_identities");
        }
    }
}
