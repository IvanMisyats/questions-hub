using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Data;

public class QuestionsHubDbContext(DbContextOptions<QuestionsHubDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<Tour> Tours => Set<Tour>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PackageImportJob> PackageImportJobs => Set<PackageImportJob>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Author>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(a => a.LastName).IsRequired().HasMaxLength(100);
            entity.HasIndex(a => new { a.FirstName, a.LastName }).IsUnique();

            // One-to-one optional relationship with ApplicationUser
            entity.HasOne(a => a.User)
                .WithOne(u => u.Author)
                .HasForeignKey<Author>(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(a => a.Questions)
                .WithMany(q => q.Authors)
                .UsingEntity("QuestionAuthors");

            entity.HasMany(a => a.Tours)
                .WithMany(t => t.Editors)
                .UsingEntity("TourEditors");

            entity.HasMany(a => a.Blocks)
                .WithMany(b => b.Editors)
                .UsingEntity("BlockEditors");

            entity.HasMany(a => a.Packages)
                .WithMany(p => p.PackageEditors)
                .UsingEntity("PackageEditors");
        });

        builder.Entity<Package>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Title).IsRequired().HasMaxLength(500);
            entity.Property(p => p.SourceUrl).HasMaxLength(2000);
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.Property(p => p.Status).HasDefaultValue(PackageStatus.Draft);
            entity.Property(p => p.NumberingMode).HasDefaultValue(QuestionNumberingMode.Global);
            entity.Property(p => p.SharedEditors).HasDefaultValue(false);

            entity.HasOne(p => p.Owner)
                .WithMany()
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(p => p.Tours)
                .WithOne(t => t.Package)
                .HasForeignKey(t => t.PackageId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes for home page query optimization
            entity.HasIndex(p => p.Status)
                .HasDatabaseName("IX_Packages_Status");

            entity.HasIndex(p => p.OwnerId)
                .HasDatabaseName("IX_Packages_OwnerId");

            entity.HasIndex(p => new { p.Status, p.AccessLevel })
                .HasDatabaseName("IX_Packages_Status_AccessLevel");
        });

        builder.Entity<Tour>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Number).IsRequired().HasMaxLength(50);
            entity.Property(t => t.Preamble);
            entity.Property(t => t.Comment).HasMaxLength(2000);
            entity.Property(t => t.OrderIndex).HasDefaultValue(0);
            entity.Property(t => t.Type).HasDefaultValue(TourType.Regular);
            entity.Ignore(t => t.IsWarmup);
            entity.Ignore(t => t.IsShootout);
            entity.Ignore(t => t.IsSpecial);

            entity.HasMany(t => t.Questions)
                .WithOne(q => q.Tour)
                .HasForeignKey(q => q.TourId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(t => t.Blocks)
                .WithOne(b => b.Tour)
                .HasForeignKey(b => b.TourId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Block>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Name).HasMaxLength(200);
            entity.Property(b => b.Preamble);

            // Unique constraint on OrderIndex within Tour
            entity.HasIndex(b => new { b.TourId, b.OrderIndex })
                .IsUnique()
                .HasDatabaseName("IX_Blocks_TourId_OrderIndex");

            entity.HasMany(b => b.Questions)
                .WithOne(q => q.Block)
                .HasForeignKey(q => q.BlockId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Question>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Number).IsRequired().HasMaxLength(20);
            entity.Property(q => q.HostInstructions).HasMaxLength(1000);
            entity.Property(q => q.Text).IsRequired();
            entity.Property(q => q.Answer).IsRequired().HasMaxLength(1000);
            entity.Property(q => q.AcceptedAnswers).HasMaxLength(1000);
            entity.Property(q => q.RejectedAnswers).HasMaxLength(1000);
            entity.Property(q => q.HandoutUrl).HasMaxLength(500);
            entity.Property(q => q.CommentAttachmentUrl).HasMaxLength(500);

            // Search columns (database-generated, read-only)
            entity.Property(q => q.SearchTextNorm)
                .ValueGeneratedOnAddOrUpdate();
            entity.Property(q => q.SearchVector)
                .HasColumnType("tsvector")
                .ValueGeneratedOnAddOrUpdate();
        });

        builder.Entity<Tag>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(100);

            // Case-insensitive unique index on tag name
            entity.HasIndex(t => t.Name)
                .IsUnique()
                .HasDatabaseName("IX_Tags_Name_CI")
                .UseCollation("und-x-icu");

            entity.HasMany(t => t.Packages)
                .WithMany(p => p.Tags)
                .UsingEntity("PackageTags");
        });

        builder.Entity<PackageImportJob>(entity =>
        {
            entity.HasKey(j => j.Id);

            entity.Property(j => j.OwnerId).IsRequired();
            entity.Property(j => j.InputFileName).IsRequired().HasMaxLength(500);
            entity.Property(j => j.InputFilePath).IsRequired().HasMaxLength(1000);
            entity.Property(j => j.ConvertedFilePath).HasMaxLength(1000);
            entity.Property(j => j.CurrentStep).HasMaxLength(100);
            entity.Property(j => j.ErrorMessage).HasMaxLength(1000);

            entity.HasOne(j => j.Owner)
                .WithMany()
                .HasForeignKey(j => j.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(j => j.Package)
                .WithMany()
                .HasForeignKey(j => j.PackageId)
                .OnDelete(DeleteBehavior.SetNull);

            // Index for efficient job queue polling
            entity.HasIndex(j => new { j.Status, j.CreatedAt })
                .HasDatabaseName("IX_PackageImportJobs_Status_CreatedAt");
        });
    }
}

