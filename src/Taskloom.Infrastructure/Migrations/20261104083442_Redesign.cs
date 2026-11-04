using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Taskloom.Infrastructure.Migrations
{
    /// <summary>
    /// Iteration 2 (spec 4.3): several assignees, workspace tags, activity, rich-text descriptions,
    /// editable comments and the Loom colours. Hand-ordered: the old assignees are copied before
    /// <c>AssigneeId</c> is dropped, and descriptions are converted in one statement.
    /// </summary>
    public partial class Redesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Who a task is assigned to.
            migrationBuilder.CreateTable(
                name: "task_assignees",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_assignees", x => new { x.TaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_task_assignees_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_assignees_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_assignees_UserId",
                table: "task_assignees",
                column: "UserId");

            // 2. Copy existing assignees who are still members of the task's workspace
            //    (the first draft never cleared AssigneeId when a member was removed).
            migrationBuilder.Sql("""
                INSERT INTO task_assignees ("TaskId", "UserId", "CreatedAt")
                SELECT t."Id", t."AssigneeId", t."UpdatedAt"
                FROM tasks t
                JOIN lists l ON l."Id" = t."ListId"
                JOIN spaces s ON s."Id" = l."SpaceId"
                JOIN workspace_members m ON m."WorkspaceId" = s."WorkspaceId" AND m."UserId" = t."AssigneeId"
                WHERE t."AssigneeId" IS NOT NULL;
                """);

            // 3. Only now drop the single assignee.
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_users_AssigneeId",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_AssigneeId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AssigneeId",
                table: "tasks");

            // 4. Plain-text copy, then convert the old plain-text descriptions to escaped HTML
            //    (one statement, so the right-hand side sees the old value).
            migrationBuilder.AddColumn<string>(
                name: "DescriptionText",
                table: "tasks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE tasks SET
                  "DescriptionText" = left(replace("Description", E'\r\n', E'\n'), 10000),
                  "Description" = '<p>' || replace(replace(replace(replace(replace(replace(
                      "Description", E'\r\n', E'\n'), '&', '&amp;'), '<', '&lt;'), '>', '&gt;'), '"', '&quot;'),
                      E'\n', '</p><p>') || '</p>'
                WHERE "Description" <> '';
                """);

            // 5. Workspace tags, task tags and activity.
            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "#7A868C"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tags_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tags_WorkspaceId_NormalizedName",
                table: "tags",
                columns: new[] { "WorkspaceId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "task_tags",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_tags", x => new { x.TaskId, x.TagId });
                    table.ForeignKey(
                        name: "FK_task_tags_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_tags_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_tags_TagId",
                table: "task_tags",
                column: "TagId");

            migrationBuilder.CreateTable(
                name: "task_activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FromValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ToValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_activities_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_activities_users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_activities_ActorId",
                table: "task_activities",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_task_activities_TaskId_CreatedAt",
                table: "task_activities",
                columns: new[] { "TaskId", "CreatedAt" });

            // 6. Editable comments.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EditedAt",
                table: "task_comments",
                type: "timestamp with time zone",
                nullable: true);

            // 7. Remap the colours the first draft generated to the Loom palette (user-picked colours stay).
            migrationBuilder.Sql("""
                UPDATE list_statuses SET "Color" = CASE lower("Color")
                  WHEN '#87909e' THEN '#7A868C' WHEN '#4194f6' THEN '#3D7DF5' WHEN '#a875ff' THEN '#9B5DE5'
                  WHEN '#3db88b' THEN '#2E9F5E' WHEN '#f5a623' THEN '#B07A0A' WHEN '#e5484d' THEN '#E0484E'
                  WHEN '#0c9ca6' THEN '#1C93B8' WHEN '#d6409f' THEN '#DB4C8B' ELSE "Color" END;
                UPDATE spaces SET "Color" = '#14998A' WHERE lower("Color") = '#7b68ee';
                """);
        }

        /// <inheritdoc />
        /// <remarks>Best effort and keeps data: one assignee per task comes back, descriptions return as their
        /// plain-text copy, and colours are not reverted.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeId",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE tasks t SET "AssigneeId" = a."UserId"
                FROM (SELECT DISTINCT ON ("TaskId") "TaskId", "UserId" FROM task_assignees ORDER BY "TaskId", "CreatedAt") a
                WHERE a."TaskId" = t."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_AssigneeId",
                table: "tasks",
                column: "AssigneeId");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_users_AssigneeId",
                table: "tasks",
                column: "AssigneeId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""UPDATE tasks SET "Description" = "DescriptionText";""");

            migrationBuilder.DropTable(
                name: "task_activities");

            migrationBuilder.DropTable(
                name: "task_tags");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "task_assignees");

            migrationBuilder.DropColumn(
                name: "DescriptionText",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "task_comments");
        }
    }
}
