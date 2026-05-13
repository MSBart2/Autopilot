using Microsoft.EntityFrameworkCore;

namespace Cyberpilot.Persistence;

/// <summary>
/// Entity Framework Core context for web-triggered Cyberpilot pipeline runs.
/// </summary>
public sealed class CyberpilotDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CyberpilotDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public CyberpilotDbContext(DbContextOptions<CyberpilotDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets Cyberpilot pipeline run records.
    /// </summary>
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();

    /// <summary>
    /// Gets Cyberpilot pipeline stage logs.
    /// </summary>
    public DbSet<PipelineStageLog> PipelineStageLogs => Set<PipelineStageLog>();

    /// <summary>
    /// Gets orchestrator dispatch events.
    /// </summary>
    public DbSet<PipelineDispatch> PipelineDispatches => Set<PipelineDispatch>();

    /// <summary>
    /// Gets human approval requests.
    /// </summary>
    public DbSet<PipelineApproval> PipelineApprovals => Set<PipelineApproval>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PipelineRun>(entity =>
        {
            entity.HasIndex(e => e.IssueNumber);
            entity.HasIndex(e => e.Status);
            entity.HasMany(e => e.StageLogs)
                .WithOne(e => e.Run)
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Dispatches)
                .WithOne()
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Approvals)
                .WithOne(e => e.Run)
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PipelineStageLog>(entity =>
        {
            entity.HasIndex(e => e.RunId);
            entity.HasIndex(e => e.StageName);
        });

        modelBuilder.Entity<PipelineDispatch>(entity =>
        {
            entity.HasIndex(e => e.RunId);
        });

        modelBuilder.Entity<PipelineApproval>(entity =>
        {
            entity.HasIndex(e => e.RunId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.StageName);
        });
    }
}