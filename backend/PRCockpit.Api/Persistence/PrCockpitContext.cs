using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PRCockpit.Api.Persistence.Entities;

namespace PRCockpit.Api.Persistence;

/// <summary>
/// The local review database. It holds only what the reviewer decided — checklists,
/// generated summaries, which files were read and in what order — never a copy of Azure
/// DevOps data, which is always fetched on demand.
/// </summary>
public sealed class PrCockpitContext(DbContextOptions<PrCockpitContext> options) : DbContext(options)
{
    public const int MaxPathLength = 512;
    private const int MaxNameLength = 200;
    private const int ShaLength = 40;

    public DbSet<PrChecklistRow> Checklists => Set<PrChecklistRow>();
    public DbSet<PrSummaryRow> Summaries => Set<PrSummaryRow>();
    public DbSet<PrFileReviewRow> FileReviews => Set<PrFileReviewRow>();
    public DbSet<PrReadingPathRow> ReadingPaths => Set<PrReadingPathRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PrChecklistRow>(entity =>
        {
            entity.ToTable("pr_checklists");
            ScopedKey(entity);
        });

        modelBuilder.Entity<PrSummaryRow>(entity =>
        {
            entity.ToTable("pr_summaries");
            ScopedKey(entity);
            entity.Property(row => row.HeadCommitSha).HasMaxLength(ShaLength);
            entity.Property(row => row.ResponseJson).IsRequired();
        });

        modelBuilder.Entity<PrFileReviewRow>(entity =>
        {
            entity.ToTable("pr_file_reviews");
            // The file path is part of the key: one row per reviewed file.
            entity.HasKey(row => new
            {
                row.Organization, row.Project, row.RepositoryId, row.PullRequestId, row.FilePath,
            });
            ScopedColumns(entity);
            entity.Property(row => row.FilePath).HasMaxLength(MaxPathLength).IsRequired();
            entity.Property(row => row.ReviewedBlobId).HasMaxLength(ShaLength);
            entity.Property(row => row.ReviewedHeadSha).HasMaxLength(ShaLength);
        });

        modelBuilder.Entity<PrReadingPathRow>(entity =>
        {
            entity.ToTable("pr_reading_paths");
            ScopedKey(entity);
            entity.Property(row => row.PathsJson).IsRequired();
        });
    }

    private static void ScopedKey<T>(EntityTypeBuilder<T> entity) where T : PullRequestScopedRow
    {
        entity.HasKey(row => new { row.Organization, row.Project, row.RepositoryId, row.PullRequestId });
        ScopedColumns(entity);
    }

    private static void ScopedColumns<T>(EntityTypeBuilder<T> entity) where T : PullRequestScopedRow
    {
        entity.Property(row => row.Organization).HasMaxLength(MaxNameLength);
        entity.Property(row => row.Project).HasMaxLength(MaxNameLength);
        entity.Property(row => row.RepositoryId).HasMaxLength(MaxNameLength);
    }
}
