using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Auth;

/// <summary>Maps an external identity onto a Taskloom user, creating one (and a starter workspace) on first sign-in.</summary>
public class UserProvisioning(TaskloomDbContext db)
{
    public async Task<User> FindOrCreateAsync(string provider, string subject, string email, string? displayName)
    {
        email = email.Trim().ToLowerInvariant();
        var identity = await db.UserIdentities.FirstOrDefaultAsync(i => i.Provider == provider && i.Subject == subject);
        if (identity is not null)
            return await db.Users.FirstAsync(u => u.Id == identity.UserId);

        // Same email from another provider (or an invite placeholder) links to the existing user.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        var isNew = user is null;
        if (user is null)
        {
            user = new User { Email = email, DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName };
            db.Users.Add(user);
        }
        db.UserIdentities.Add(new UserIdentity { UserId = user.Id, Provider = provider, Subject = subject });

        var hasWorkspace = !isNew && await db.WorkspaceMembers.AnyAsync(m => m.UserId == user.Id);
        if (!hasWorkspace) AddStarterWorkspace(user);

        await db.SaveChangesAsync();
        return user;
    }

    private void AddStarterWorkspace(User user)
    {
        var workspace = new Workspace { Name = $"{user.DisplayName.Split(' ')[0]}'s Workspace" };
        var space = new Space { WorkspaceId = workspace.Id, Name = "Personal", Position = Positions.Step };
        var list = new TaskList { SpaceId = space.Id, Name = "Tasks", Position = Positions.Step };
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = user.Id, Role = WorkspaceRole.Owner });
        db.Spaces.Add(space);
        db.Lists.Add(list);
        db.Statuses.AddRange(ListStatus.Defaults(list.Id));
    }

    public static ClaimsPrincipal PrincipalFor(User user, string provider) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimsExtensions.UserIdClaim, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim("taskloom:provider", provider),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
}
