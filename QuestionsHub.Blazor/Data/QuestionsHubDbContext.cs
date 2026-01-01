using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Data;

public class QuestionsHubDbContext(DbContextOptions<QuestionsHubDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<Tour> Tours => Set<Tour>();
    public DbSet<Question> Questions => Set<Question>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Package>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Title).IsRequired().HasMaxLength(500);
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.Property(p => p.Status).HasDefaultValue(PackageStatus.Draft);

            entity.HasOne(p => p.Owner)
                .WithMany()
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(p => p.Tours)
                .WithOne(t => t.Package)
                .HasForeignKey(t => t.PackageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Tour>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Number).IsRequired().HasMaxLength(50);
            entity.Property(t => t.Preamble);
            entity.Property(t => t.Comment).HasMaxLength(2000);

            entity.HasMany(t => t.Questions)
                .WithOne(q => q.Tour)
                .HasForeignKey(q => q.TourId)
                .OnDelete(DeleteBehavior.Cascade);
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
        });
    }
}

