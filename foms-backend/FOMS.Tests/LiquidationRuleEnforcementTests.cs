using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Api.Controllers;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Tests;

public class LiquidationRuleEnforcementTests
{
    private static (FomsDbContext Db, LiquidationsController Controller) Create(string name)
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>().UseInMemoryDatabase(name).Options;
        var db = new FomsDbContext(options);
        var controller = new LiquidationsController(db);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "EMP-004"), new Claim(ClaimTypes.Name, "Preparer"),
            new Claim(ClaimTypes.Role, FomsRoles.AssistantFinanceManager), new Claim("role", FomsRoles.AssistantFinanceManager),
            new Claim("employee_id", "EMP-004"), new Claim("user_id", "4")
        };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } };
        return (db, controller);
    }

    [Fact]
    public async Task Create_RejectsMissingCashAdvance()
    {
        var (db, controller) = Create(nameof(Create_RejectsMissingCashAdvance));
        await using (db)
        {
            var result = await controller.Create(new LiquidationDto { CashAdvanceReference = "missing" });
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Empty(await db.Liquidations.ToListAsync());
        }
    }

    [Fact]
    public async Task Create_RejectsAdvanceNotReleased()
    {
        var (db, controller) = Create(nameof(Create_RejectsAdvanceNotReleased));
        await using (db)
        {
            db.CashAdvances.Add(new CashAdvance { AdvanceId = "CA-1", AmountGiven = 100m, Status = "Pending Finance Review" });
            await db.SaveChangesAsync();
            var result = await controller.Create(new LiquidationDto { CashAdvanceReference = "CA-1" });
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Empty(await db.Liquidations.ToListAsync());
        }
    }

    [Fact]
    public async Task Create_RejectsReportWithoutItemizedLines()
    {
        var (db, controller) = Create(nameof(Create_RejectsReportWithoutItemizedLines));
        await using (db)
        {
            db.CashAdvances.Add(new CashAdvance { AdvanceId = "CA-2", AmountGiven = 100m, Status = "Released" });
            await db.SaveChangesAsync();
            var result = await controller.Create(new LiquidationDto { CashAdvanceReference = "CA-2" });
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Empty(await db.Liquidations.ToListAsync());
        }
    }
}
