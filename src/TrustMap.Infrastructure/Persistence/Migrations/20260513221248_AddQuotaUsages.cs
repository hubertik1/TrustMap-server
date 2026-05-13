using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotaUsages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quota_usages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    SubjectKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    QuotaName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PeriodStartUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PeriodEndUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_usages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quota_usages_PeriodEndUtc",
                table: "quota_usages",
                column: "PeriodEndUtc");

            migrationBuilder.CreateIndex(
                name: "IX_quota_usages_Scope_SubjectKey_QuotaName_PeriodStartUtc",
                table: "quota_usages",
                columns: new[] { "Scope", "SubjectKey", "QuotaName", "PeriodStartUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quota_usages");
        }
    }
}
