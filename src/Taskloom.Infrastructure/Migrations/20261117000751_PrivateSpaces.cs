using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskloom.Infrastructure.Migrations
{
    /// <summary>
    /// Security 3: private spaces. Only additions (a column that defaults to false and an empty table), so every existing
    /// space stays visible to everyone in its workspace, exactly as before.
    /// </summary>
    public partial class PrivateSpaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "spaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "space_members",
                columns: table => new
                {
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_space_members", x => new { x.SpaceId, x.UserId });
                    table.ForeignKey(
                        name: "FK_space_members_spaces_SpaceId",
                        column: x => x.SpaceId,
                        principalTable: "spaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_space_members_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_space_members_UserId",
                table: "space_members",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "space_members");

            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "spaces");
        }
    }
}
