using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrustMap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_users_status",
                table: "users");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Status_DeletedAtUtc",
                table: "users",
                columns: new[] { "Status", "DeletedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_status",
                table: "users",
                sql: "\"Status\" IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_Status_DeletedAtUtc",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_status",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "users");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "users",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_status",
                table: "users",
                sql: "\"Status\" IN (1, 2)");
        }
    }
}
