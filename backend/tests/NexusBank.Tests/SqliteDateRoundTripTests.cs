using Microsoft.EntityFrameworkCore;
using NexusBank.Domain.Entities;
using NexusBank.Domain.Enums;
using NexusBank.Tests.Support;

namespace NexusBank.Tests;

public sealed class SqliteDateRoundTripTests
{
    [Fact]
    public async Task DateTimeOffset_RoundTripsThroughSqlite()
    {
        using var database = new TestDatabase();
        var when = new DateTimeOffset(2026, 11, 13, 10, 30, 0, TimeSpan.Zero);

        database.Db.Customers.Add(new Customer
        {
            CustomerNumber = "C-RT-1",
            FirstName = "Round",
            LastName = "Trip",
            Email = "rt@example.test",
            CreatedAt = when
        });
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var reloaded = await database.Db.Customers.AsNoTracking().SingleAsync();
        Assert.Equal(when, reloaded.CreatedAt);

        database.Db.Accounts.Add(new Account
        {
            CustomerId = reloaded.Id,
            AccountNumber = "00-0000-0000000-00",
            Iban = "NZ00NEXU000000000000000",
            OpenedAt = when,
            Status = AccountStatus.Active
        });
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var account = await database.Db.Accounts.AsNoTracking().SingleAsync();
        Assert.Equal(when, account.OpenedAt);

        var recent = await database.Db.Accounts.CountAsync(a => a.OpenedAt > when.AddDays(-1));
        Assert.Equal(1, recent);
    }
}
