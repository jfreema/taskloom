using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskloom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProfilePhotosAndListLooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PhotoChosen",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PhotoVersion",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "lists",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "lists",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_photos",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_photos", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_user_photos_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_photos");

            migrationBuilder.DropColumn(
                name: "PhotoChosen",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PhotoVersion",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "lists");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "lists");
        }
    }
}
