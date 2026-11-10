using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using FOMS.Application.DTOs;
using FOMS.Application.Services;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Xunit;

namespace FOMS.Tests;

public class Phase13EmployeeRoleSecurityTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private AuthService CreateAuthService(ApplicationDbContext context)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = new string('k', 48),
            ["JwtSettings:Issuer"] = "FOMS",
            ["JwtSettings:Audience"] = "FOMS_Audience",
            ["JwtSettings:AccessTokenExpirationMinutes"] = "60",
            ["JwtSettings:RefreshTokenExpirationDays"] = "7"
        }).Build();

        return new AuthService(context, cfg, NullLogger<AuthService>.Instance);
    }

    private ActionExecutingContext CreateActionExecutingContext(string path, ClaimsPrincipal user, ApplicationDbContext? db = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.User = user;

        var services = new ServiceCollection();
        if (db != null)
        {
            services.AddSingleton(db);
        }
        httpContext.RequestServices = services.BuildServiceProvider();

        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
    }

    [Fact]
    public async Task CreateAsync_WithoutPassword_GeneratesSecureTemporaryPassword_AndSetsMustChange()
    {
        using var context = CreateInMemoryContext();
        var userService = new UserService(context);

        var dto = new UserCreateDto
        {
            EmployeeId = "EMP-010",
            Username = "EMP-010",
            FullName = "Audit Tester",
            Email = "audittester@foms.local",
            Position = "Finance Manager"
        };

        var result = await userService.CreateAsync(dto);

        Assert.NotNull(result);
        Assert.Equal("EMP-010", result.Id);
        Assert.Equal("Finance Manager", result.Position);
        Assert.Equal("Finance Manager", result.Role); // Auto-derived
        Assert.True(result.MustChangePassword);
        Assert.True(result.IsTemporaryPassword);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
        Assert.True(result.TemporaryPassword!.Length >= 12);

        var dbUser = await context.Employees.FindAsync("EMP-010");
        Assert.NotNull(dbUser);
        Assert.True(dbUser!.MustChangePassword);
        Assert.True(dbUser.IsTemporaryPassword);
        Assert.NotNull(dbUser.TemporaryPasswordExpiresAt);
        Assert.True(dbUser.TemporaryPasswordExpiresAt > DateTime.UtcNow);
        Assert.True(AuthService.VerifyPassword(result.TemporaryPassword, dbUser.PasswordHash));
    }

    [Fact]
    public async Task CreateAsync_WithForbiddenDefaultPassword_ThrowsInvalidOperationException()
    {
        using var context = CreateInMemoryContext();
        var userService = new UserService(context);

        var dto = new UserCreateDto
        {
            EmployeeId = "EMP-011",
            Username = "EMP-011",
            FullName = "Insecure Tester",
            Email = "insecure@foms.local",
            Position = "Accountant",
            Password = "Password@123"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => userService.CreateAsync(dto));
        Assert.Contains("Password@123", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_WithMismatchedRoleAndPosition_ThrowsInvalidOperationException()
    {
        using var context = CreateInMemoryContext();
        var userService = new UserService(context);

        var dto = new UserCreateDto
        {
            EmployeeId = "EMP-012",
            Username = "EMP-012",
            FullName = "Mismatch Tester",
            Email = "mismatch@foms.local",
            Position = "Finance Manager",
            Role = "Accountant" // Mismatch
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => userService.CreateAsync(dto));
        Assert.Contains("does not match", ex.Message);
    }

    [Theory]
    [InlineData("CEO", "CEO")]
    [InlineData("President", "President")]
    [InlineData("Executive", "CEO")]
    public async Task CreateAsync_WithCeoOrPresident_ThrowsInvalidOperationException_IAM07(string role, string position)
    {
        using var context = CreateInMemoryContext();
        var userService = new UserService(context);

        var dto = new UserCreateDto
        {
            EmployeeId = "EMP-099",
            Username = "EMP-099",
            FullName = "Corporate Executive",
            Email = "exec@foms.local",
            Role = role,
            Position = position
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => userService.CreateAsync(dto));
        Assert.Contains("CEO/President is not a FOMS user", ex.Message);
        Assert.Contains("IAM-07", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_WithCeoUsernameOrEmployeeId_ThrowsInvalidOperationException()
    {
        using var context = CreateInMemoryContext();
        var userService = new UserService(context);

        var dto = new UserCreateDto
        {
            EmployeeId = "EMP-CEO",
            Username = "EMP-CEO",
            FullName = "Executive User",
            Email = "ceo@foms.local",
            Position = "Accountant"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => userService.CreateAsync(dto));
        Assert.Contains("CEO/President is not a FOMS user", ex.Message);
    }

    [Fact]
    public async Task ChangePasswordAsync_ClearsMustChangePassword_AndTemporaryPasswordFlags()
    {
        using var context = CreateInMemoryContext();
        var authService = CreateAuthService(context);

        var initialPassword = "TempPassword!123";
        var user = new Employee
        {
            Id = "EMP-015",
            Username = "EMP-015",
            Name = "Change Password Tester",
            Email = "change@foms.local",
            Role = FomsRoles.Accountant,
            Position = FomsRoles.Accountant,
            PasswordHash = AuthService.HashPassword(initialPassword),
            MustChangePassword = true,
            IsTemporaryPassword = true,
            TemporaryPasswordExpiresAt = DateTime.UtcNow.AddHours(72)
        };
        await context.Employees.AddAsync(user);
        await context.SaveChangesAsync();

        var newPassword = "NewSecurePassword#2026";
        var result = await authService.ChangePasswordAsync("EMP-015", initialPassword, newPassword);

        Assert.True(result);
        var updated = await context.Employees.FindAsync("EMP-015");
        Assert.NotNull(updated);
        Assert.False(updated!.MustChangePassword);
        Assert.False(updated.IsTemporaryPassword);
        Assert.Null(updated.TemporaryPasswordExpiresAt);
        Assert.True(AuthService.VerifyPassword(newPassword, updated.PasswordHash));
    }

    [Fact]
    public async Task LoginAsync_WithExpiredTemporaryPassword_ThrowsUnauthorizedAccessException()
    {
        using var context = CreateInMemoryContext();
        var authService = CreateAuthService(context);

        var password = "TempPassword!123";
        var user = new Employee
        {
            Id = "EMP-016",
            Username = "EMP-016",
            Name = "Expired Temp User",
            Email = "expired@foms.local",
            Role = FomsRoles.Accountant,
            Position = FomsRoles.Accountant,
            PasswordHash = AuthService.HashPassword(password),
            MustChangePassword = true,
            IsTemporaryPassword = true,
            TemporaryPasswordExpiresAt = DateTime.UtcNow.AddMinutes(-10) // Expired 10 min ago
        };
        await context.Employees.AddAsync(user);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authService.LoginAsync("EMP-016", password));
        Assert.Contains("Temporary password has expired", ex.Message);
    }

    [Fact]
    public async Task StartupCheck_ThrowsInNonDevelopment_IfDemoAccountsExist()
    {
        using var context = CreateInMemoryContext();
        var user = new Employee
        {
            Id = "EMP-001",
            Username = "EMP-001",
            Name = "Crystalyn Joyce C. Fajardo",
            Email = "crystalyn@foms.local",
            Role = "Finance Manager",
            PasswordHash = "PBKDF2$150000$KPxlVQdFsQ14PmZeoVkx/A==$R9tqlRYtrayoyb6c6gXWVjrqdUimmWyvhsoMA6reN50="
        };
        await context.Employees.AddAsync(user);
        await context.SaveChangesAsync();

        // When isDevelopment is false (Staging or Production), must fail fast
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ApplicationDbContextSeed.ValidateNoDemoAccountsInNonDevelopmentAsync(context, isDevelopment: false));

        Assert.Contains("FATAL SECURITY ERROR", ex.Message);
        Assert.Contains("demo accounts detected in non-development environment", ex.Message);
    }

    [Fact]
    public async Task MustChangePasswordFilter_WhenMustChangePassword_BlocksBusinessEndpoint()
    {
        var filter = new FOMS.Api.Filters.MustChangePasswordFilter();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP-010"),
            new Claim("must_change_password", "true")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = CreateActionExecutingContext("/api/invoices", principal);
        bool nextCalled = false;

        await filter.OnActionExecutionAsync(context, () => { nextCalled = true; return Task.FromResult<ActionExecutedContext>(null!); });

        Assert.False(nextCalled);
        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, objectResult.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/change-password")]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/auth/refresh")]
    public async Task MustChangePasswordFilter_WhenMustChangePassword_AllowsAuthEndpoints(string path)
    {
        var filter = new FOMS.Api.Filters.MustChangePasswordFilter();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP-010"),
            new Claim("must_change_password", "true")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = CreateActionExecutingContext(path, principal);
        bool nextCalled = false;

        await filter.OnActionExecutionAsync(context, () => { nextCalled = true; return Task.FromResult<ActionExecutedContext>(null!); });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task MustChangePasswordFilter_WhenTemporaryPasswordExpired_BlocksAccess()
    {
        var filter = new FOMS.Api.Filters.MustChangePasswordFilter();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP-010"),
            new Claim("must_change_password", "true"),
            new Claim("temp_pw_expires_at", DateTime.UtcNow.AddMinutes(-5).ToString("o"))
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = CreateActionExecutingContext("/api/payments", principal);
        bool nextCalled = false;

        await filter.OnActionExecutionAsync(context, () => { nextCalled = true; return Task.FromResult<ActionExecutedContext>(null!); });

        Assert.False(nextCalled);
        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, objectResult.StatusCode);
    }
}
