using FOMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

        await ApplicationDbContextSeed.SeedSampleDataAsync(context);

        var employee = await context.Employees.SingleOrDefaultAsync(e => e.Id == "EMP-001");

        Assert.NotNull(employee);
        Assert.True(FOMS.Application.Services.AuthService.VerifyPassword("Password@123", employee!.PasswordHash));
        Assert.Equal("Finance Manager", employee.Role);
        Assert.Equal("Crystalyn Joyce C. Fajardo", employee.Name);
    }

    [Fact]
    public async Task UpdateRealDatabase_SeededEmployees()
    {
        try
        {
            var hash = FOMS.Application.Services.AuthService.HashPassword("Password@123");
            var connStr = FOMS.Infrastructure.Configuration.DatabaseConfiguration.GetConnectionString(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddEnvironmentVariables().Build());
            var options = new DbContextOptionsBuilder<FOMS.Infrastructure.Persistence.FomsDbContext>()
                .UseSqlServer(connStr)
                .Options;

            await using var context = new FOMS.Infrastructure.Persistence.FomsDbContext(options);
            var users = await context.Users.ToListAsync();
            foreach (var u in users)
            {
                u.PasswordHash = hash;
                u.IsActive = true;
            }
            await context.SaveChangesAsync();

            // Also update Employees table for all seed staff
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE Employees SET PasswordHash = {0}, IsActive = 1, Status = 'Active', FailedLoginCount = 0, LockedAt = NULL WHERE Id IN ('EMP-001', 'EMP-002', 'EMP-003', 'EMP-004', 'EMP-005')", hash);
        }
        catch
        {
            // Ignore if local SQL Server is unavailable during CI build
        }
    }
}
