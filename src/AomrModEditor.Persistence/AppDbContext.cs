using AomrModEditor.Application.Abstractions;
using AomrModEditor.Domain;
using Microsoft.EntityFrameworkCore;

namespace AomrModEditor.Persistence;

public sealed class ProjectMetadata
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Directory { get; set; } = "";
    public string SourceFingerprintHash { get; set; } = "";
    public int Version { get; set; }
}

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ProjectMetadata> Projects => Set<ProjectMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectMetadata>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Directory).HasMaxLength(1024);
            e.Property(x => x.SourceFingerprintHash).HasMaxLength(128);
        });
    }
}

public sealed class ProjectMetadataStore
{
    private readonly AppDbContext _db;

    public ProjectMetadataStore(AppDbContext db) => _db = db;

    public async Task UpsertAsync(ProjectInfo project, CancellationToken ct)
    {
        await _db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        var existing = await _db.Projects.FindAsync([project.Id], ct).ConfigureAwait(false);
        if (existing is null)
        {
            _db.Projects.Add(new ProjectMetadata
            {
                Id = project.Id,
                Name = project.Name,
                Directory = project.Directory,
                SourceFingerprintHash = project.SourceFingerprintHash,
                Version = project.Version
            });
        }
        else
        {
            existing.Name = project.Name;
            existing.Directory = project.Directory;
            existing.SourceFingerprintHash = project.SourceFingerprintHash;
            existing.Version = project.Version;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
