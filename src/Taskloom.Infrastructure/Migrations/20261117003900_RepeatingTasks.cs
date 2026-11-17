using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskloom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RepeatingTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NextRepeatId",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Repeat",
                table: "tasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_NextRepeatId",
                table: "tasks",
                column: "NextRepeatId");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_tasks_NextRepeatId",
                table: "tasks",
                column: "NextRepeatId",
                principalTable: "tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_tasks_NextRepeatId",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_NextRepeatId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "NextRepeatId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Repeat",
                table: "tasks");
        }
    }
}
