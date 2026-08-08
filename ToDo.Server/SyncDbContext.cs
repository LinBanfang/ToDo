using Microsoft.EntityFrameworkCore;
using ToDo.Server.Models;

namespace ToDo.Server;

public class SyncDbContext : DbContext
{
    public SyncDbContext(DbContextOptions<SyncDbContext> options) : base(options) { }

    public DbSet<SyncEntity> SyncEntities => Set<SyncEntity>();
    public DbSet<SyncMeta> SyncMeta => Set<SyncMeta>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SyncEntity>(e =>
        {
            e.HasKey(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.ServerSeq);   // incremental pull reads by cursor
        });
        modelBuilder.Entity<SyncMeta>(e => e.HasKey(x => x.Key));
    }
}
