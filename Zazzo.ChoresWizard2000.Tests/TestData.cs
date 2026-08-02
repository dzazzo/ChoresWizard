using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Zazzo.ChoresWizard2000.Data;
using Zazzo.ChoresWizard2000.Models;
using Zazzo.ChoresWizard2000.Services;

namespace Zazzo.ChoresWizard2000.Tests;

/// <summary>
/// Builds an isolated <see cref="ChoresDbContext"/> backed by a SQLite in-memory
/// database. The relational SQLite provider (rather than the EF InMemory provider)
/// is used so foreign keys, indexes and column constraints are actually enforced.
///
/// The connection must be kept open for the lifetime of the database, so the
/// context owns and disposes the underlying <see cref="SqliteConnection"/>.
/// </summary>
public sealed class TestDbContext : ChoresDbContext
{
    private readonly SqliteConnection _connection;

    private TestDbContext(DbContextOptions<ChoresDbContext> options, SqliteConnection connection)
        : base(options)
    {
        _connection = connection;
    }

    public static TestDbContext Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ChoresDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new TestDbContext(options, connection);
        context.Database.EnsureCreated();
        return context;
    }

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }
}

/// <summary>
/// Shared helpers for arranging fixtures and building a deterministic service.
/// </summary>
public static class TestData
{
    /// <summary>The household time zone under test (Pacific: UTC-8 / UTC-7 in DST).</summary>
    public static readonly TimeZoneInfo Pacific =
        TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    /// <summary>
    /// Builds a <see cref="SortingHatService"/> against the supplied context with a
    /// seeded <see cref="Random"/>, guaranteeing deterministic distribution. Optionally
    /// pin the clock and time zone so month determination is fully controlled.
    /// </summary>
    public static SortingHatService CreateService(
        ChoresDbContext context,
        int seed = 12345,
        TimeProvider? timeProvider = null,
        TimeZoneInfo? timeZone = null)
        => new(context, NullLogger<SortingHatService>.Instance, new Random(seed), timeProvider, timeZone);

    public static FamilyMember AddMember(
        ChoresDbContext ctx,
        string name,
        bool isParent = false,
        bool isTeen = false,
        bool isActive = true)
    {
        var member = new FamilyMember
        {
            Name = name,
            IsParent = isParent,
            IsTeen = isTeen,
            IsActive = isActive
        };
        ctx.FamilyMembers.Add(member);
        ctx.SaveChanges();
        return member;
    }

    public static Chore AddChore(
        ChoresDbContext ctx,
        string name,
        ChoreFrequency frequency = ChoreFrequency.Daily,
        AgeRestriction ageRestriction = AgeRestriction.Everyone,
        AssignToGroup assignToGroup = AssignToGroup.OnePersonOnly,
        string? category = null,
        int? pinnedToFamilyMemberId = null,
        bool isActive = true)
    {
        var chore = new Chore
        {
            Name = name,
            Frequency = frequency,
            AgeRestriction = ageRestriction,
            AssignToGroup = assignToGroup,
            Category = category,
            PinnedToFamilyMemberId = pinnedToFamilyMemberId,
            IsActive = isActive
        };
        ctx.Chores.Add(chore);
        ctx.SaveChanges();
        return chore;
    }

    public static ChoreAssignment AddPreviousAssignment(
        ChoresDbContext ctx,
        int familyMemberId,
        int choreId,
        int year,
        int month)
    {
        var assignment = new ChoreAssignment
        {
            FamilyMemberId = familyMemberId,
            ChoreId = choreId,
            AssignedDate = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
            Month = month,
            Year = year
        };
        ctx.ChoreAssignments.Add(assignment);
        ctx.SaveChanges();
        return assignment;
    }
}
