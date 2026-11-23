using Taskloom.Domain;
using Microsoft.AspNetCore.Http;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>Development-only endpoints. Program.cs maps them only when Auth:DevLogin:Enabled is true.</summary>
public static class DevEndpoints
{
    public static void MapDevEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/dev/seed", async (DevSeedRequest req, Access access, TaskloomDbContext db, CancellationToken ct) =>
        {
            await access.RequireWorkspaceAsync(req.WorkspaceId, WorkspaceRole.Owner);
            var fillers = req.Fillers ?? DevSeed.DefaultFillers;
            if (fillers is < 0 or > DevSeed.MaxFillers) return Problems.BadRequest("Fillers must be between 0 and 2000.");
            await new DevSeed(db).RunAsync(access.UserId, req.WorkspaceId, fillers, ct);
            return Results.NoContent();
        });

        // Demo data for the dashboard and the views (DemoSeed): only adds, and "replace" rebuilds only its own spaces.
        api.MapPost("/dev/demo-seed", async (DemoSeedRequest req, Access access, TaskloomDbContext db, HttpContext http, CancellationToken ct) =>
        {
            await access.RequireWorkspaceAsync(req.WorkspaceId, WorkspaceRole.Owner);
            var seed = new DemoSeed(db);
            if (req.Replace != true && await seed.ExistsAsync(req.WorkspaceId, ct))
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    detail: "This workspace already has the demo data. Send replace: true to rebuild it (its demo spaces are deleted first).");
            var result = await seed.RunAsync(access.UserId, req.WorkspaceId, ClientDate.Today(http, Clock.Now()), ct);
            return Results.Ok(result);
        });

        api.MapDelete("/dev/demo-seed/{workspaceId:guid}", async (Guid workspaceId, Access access, TaskloomDbContext db, CancellationToken ct) =>
        {
            await access.RequireWorkspaceAsync(workspaceId, WorkspaceRole.Owner);
            var removed = await new DemoSeed(db).RemoveAsync(workspaceId, ct);
            return Results.Ok(new { removedSpaces = removed });
        });
    }
}
