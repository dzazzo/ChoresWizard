using Microsoft.EntityFrameworkCore;
using Zazzo.ChoresWizard2000.Models;

namespace Zazzo.ChoresWizard2000.Data;

public class ChoresDbContext : DbContext
{
    public ChoresDbContext(DbContextOptions<ChoresDbContext> options)
        : base(options)
    {
    }

    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<Chore> Chores => Set<Chore>();
    public DbSet<ChoreAssignment> ChoreAssignments => Set<ChoreAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure string columns with max length for SQL Server compatibility
        modelBuilder.Entity<FamilyMember>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Chore>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Category).HasMaxLength(100);
        });

        modelBuilder.Entity<ChoreAssignment>()
            .HasOne(ca => ca.FamilyMember)
            .WithMany()
            .HasForeignKey(ca => ca.FamilyMemberId);

        modelBuilder.Entity<ChoreAssignment>()
            .HasOne(ca => ca.Chore)
            .WithMany()
            .HasForeignKey(ca => ca.ChoreId);

        modelBuilder.Entity<ChoreAssignment>()
            .HasIndex(ca => new { ca.Month, ca.Year });
    }
}
