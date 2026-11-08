using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Api.Controllers;
using FOMS.Infrastructure.Persistence;
using Xunit;

namespace FOMS.Tests;

public class DatabaseHealthControllerTests
{
    private static JsonElement ToJson(IActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        var json = JsonSerializer.Serialize(objectResult.Value);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task CheckDatabaseHealth_WhenHealthy_ReturnsConnected()
    {
        var legacyOptions = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var appOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var legacyDb = new FomsDbContext(legacyOptions);
        using var appDb = new ApplicationDbContext(appOptions);

        var controller = new DatabaseHealthController(legacyDb, appDb);
        var result = await controller.CheckDatabaseHealth();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var root = ToJson(okResult);

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Connected", root.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CheckDatabaseHealth_DoesNotExposeRawException_OnFailure()
    {
        // Dispose db immediately so calling health check throws ObjectDisposedException
        var legacyOptions = new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var appOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var legacyDb = new FomsDbContext(legacyOptions);
        var appDb = new ApplicationDbContext(appOptions);
        legacyDb.Dispose();
        appDb.Dispose();

        var controller = new DatabaseHealthController(legacyDb, appDb);
        var result = await controller.CheckDatabaseHealth();

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, statusResult.StatusCode);

        var root = ToJson(statusResult);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("Unhealthy", root.GetProperty("status").GetString());
        // Generic message without leaking raw exception details (e.g. ObjectDisposedException)
        Assert.Equal("Database health check failed.", root.GetProperty("message").GetString());
        Assert.False(root.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public async Task ApplyMigrationsAsync_WhenRelationalDatabaseFails_ThrowsInvalidOperationException()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=invalid-unreachable-host;Database=NonExistentDb;Timeout=1;ConnectRetryCount=0;")
            .Options;

        using var appDb = new ApplicationDbContext(options);
        var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseMigrationRunner.ApplyMigrationsAsync(appDb, mockLogger.Object));

        Assert.Contains("Fatal Startup Error: Database migration failed", ex.Message);
    }

    [Fact]
    public async Task ApplyMigrationsAsync_WhenNonRelationalProvider_SkipsWithoutThrowing()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var appDb = new ApplicationDbContext(options);
        var mockLogger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger>();

        await DatabaseMigrationRunner.ApplyMigrationsAsync(appDb, mockLogger.Object);
    }
}
