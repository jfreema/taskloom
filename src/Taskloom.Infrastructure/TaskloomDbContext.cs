using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Taskloom.Domain;

namespace Taskloom.Infrastructure;

public class TaskloomDbContext(DbContextOptions<TaskloomDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<TaskList> Lists => Set<TaskList>();
    public DbSet<ListStatus> Statuses => Set<ListStatus>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskComment> Comments => Set<TaskComment>();
    public DbSet<TaskAttachment> Attachments => Set<TaskAttachment>();
    /// <summary>ASP.NET Core data protection keys, so cookies survive restarts and work across replicas.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.Email).HasMaxLength(320);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.HasMany(x => x.Identities).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<UserIdentity>(e =>
        {
            e.ToTable("user_identities");
            e.Property(x => x.Provider).HasMaxLength(50);
            e.Property(x => x.Subject).HasMaxLength(255);
            e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
        });
        b.Entity<Workspace>(e =>
        {
            e.ToTable("workspaces");
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Spaces).WithOne().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WorkspaceMember>(e =>
        {
            e.ToTable("workspace_members");
            e.HasKey(x => new { x.WorkspaceId, x.UserId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        });
        b.Entity<Space>(e =>
        {
            e.ToTable("spaces");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Color).HasMaxLength(20);
            e.HasMany(x => x.Lists).WithOne(x => x.Space).HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TaskList>(e =>
        {
            e.ToTable("lists");
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Statuses).WithOne().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ListStatus>(e =>
        {
            e.ToTable("list_statuses");
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Color).HasMaxLength(20);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
        });
        b.Entity<TaskItem>(e =>
        {
            e.ToTable("tasks");
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.HasOne<TaskList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
            // NO ACTION (checked at end of statement) so deleting a list removes its tasks and statuses together.
            e.HasOne<ListStatus>().WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.ParentTaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ListId, x.StatusId, x.Position });
            e.HasIndex(x => x.ParentTaskId);
        });
        b.Entity<TaskComment>(e =>
        {
            e.ToTable("task_comments");
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TaskId);
        });
        b.Entity<TaskAttachment>(e =>
        {
            e.ToTable("task_attachments");
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(255);
            e.Property(x => x.StorageKey).HasMaxLength(500);
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TaskId);
        });
    }
}
