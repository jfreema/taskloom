using Taskloom.Domain;
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
    }
}
