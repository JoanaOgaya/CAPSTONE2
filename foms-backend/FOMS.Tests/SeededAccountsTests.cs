using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FOMS.Tests;

public class SeededAccountsTests
{
    [Fact]
    public async Task SeedSampleData_ShouldCreateTheDemoAdminAccount()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        await ApplicationDbContextSeed.SeedSampleDataAsync(context, isDevelopment: true);

        var employee = await context.Employees.SingleOrDefaultAsync(e => e.Id == "EMP-001");

        Assert.NotNull(employee);
        Assert.True(FOMS.Application.Services.AuthService.VerifyPassword("Password@123", employee!.PasswordHash));
        Assert.Equal("Finance Manager", employee.Role);
        Assert.Equal("Crystalyn Joyce C. Fajardo", employee.Name);
    }

    [Fact]
    public async Task UpdateDatabase_SeededEmployees_UsesDisposableInMemoryDatabase()
    {
        var hash = FOMS.Application.Services.AuthService.HashPassword("Password@123");
        var options = new DbContextOptionsBuilder<FOMS.Infrastructure.Persistence.FomsDbContext>()
            .UseInMemoryDatabase(databaseName: $"SeededUsers_{Guid.NewGuid()}")
            .Options;

        await using var context = new FOMS.Infrastructure.Persistence.FomsDbContext(options);
        context.Users.Add(new FOMS.Domain.Entities.SqlServer.User
        {
            UserId = 1,
            EmployeeNumber = "EMP-001",
            FullName = "Crystalyn Joyce C. Fajardo",
            PasswordHash = "old",
            IsActive = false
        });
        await context.SaveChangesAsync();

        var users = await context.Users.ToListAsync();
        foreach (var u in users)
        {
            u.PasswordHash = hash;
            u.IsActive = true;
        }
        await context.SaveChangesAsync();

        var updated = await context.Users.SingleAsync();
        Assert.True(updated.IsActive);
        Assert.True(FOMS.Application.Services.AuthService.VerifyPassword("Password@123", updated.PasswordHash));
    }

    [Fact]
    public async Task SeedSampleDataAsync_WhenNotDevelopment_ThrowsInvalidOperationException()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        // Default isDevelopment is false; should fail closed
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ApplicationDbContextSeed.SeedSampleDataAsync(context));
    }
}
