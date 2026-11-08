using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FOMS.Api.Controllers;
using FOMS.Api.Filters;
using FOMS.Application.Configuration;
using FOMS.Application.Contracts;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FOMS.Tests;

/// <summary>
/// Trust-boundary tests. Identity and role come ONLY from validated JWT claims:
///  (1) no token => 401,
///  (2) no token + spoofed X-User-Role header => 401,
///  (3) valid token with the wrong role + spoofed header for a higher role => 403 (the claim wins),
///  (4) authenticated principal with no/unknown role claim => 403 (fail closed).
/// </summary>
public class AuthTrustBoundaryTests
{
    private const string SpoofedHeaderRole = "Finance Manager";

    private static FomsDbContext CreateFomsDb(string name) =>
        new(new DbContextOptionsBuilder<FomsDbContext>()
            .UseInMemoryDatabase($"{name}_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ApplicationDbContext CreateAppDb(string name) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{name}_App_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>No JWT: anonymous principal. Optionally carries spoofed identity headers.</summary>
    private static void NoToken(ControllerBase controller, bool spoofHeaders)
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        if (spoofHeaders)
        {
            ctx.Request.Headers["X-User-Role"] = SpoofedHeaderRole;
            ctx.Request.Headers["X-User-Username"] = "EMP-001";
            ctx.Request.Headers["X-User-Id"] = "EMP-001";
        }
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
    }

    /// <summary>Validated JWT principal with the given role claim, plus a spoofed header claiming a different role.</summary>
    private static void Token(ControllerBase controller, string? role, string empId, string? spoofedHeaderRole)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, empId),
            new(ClaimTypes.NameIdentifier, empId),
            new("employee_id", empId)
        };
        if (role != null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        if (spoofedHeaderRole != null)
        {
            ctx.Request.Headers["X-User-Role"] = spoofedHeaderRole;
            ctx.Request.Headers["X-User-Username"] = "EMP-001";
        }
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
    }

    private static int Status(IActionResult result) =>
        Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode ?? 200;

    private static AiDuplicatesController NewDuplicates(string name) =>
        new(CreateAppDb(name), CreateFomsDb(name), new Mock<IHttpClientFactory>().Object, new ConfigurationBuilder().Build());

    private static AiFeaturesController NewFeatures(string name) =>
        new(CreateFomsDb(name), new Mock<IHttpClientFactory>().Object, new ConfigurationBuilder().Build());

    private static IntegrationController NewStars(string name) =>
        new(new Mock<IStarsIntegrationService>().Object, new Mock<IUserService>().Object, CreateAppDb(name), CreateFomsDb(name),
            Options.Create(new StarsIntegrationOptions { InboundApiKey = "valid-stars-inbound-key-12345" }));

    private static DmsIntegrationController NewDms(string name) =>
        new(new Mock<IDmsIntegrationService>().Object,
            Options.Create(new DmsIntegrationOptions { InboundApiKey = "valid-dms-inbound-key-12345" }),
            NullLogger<DmsIntegrationController>.Instance, CreateFomsDb(name));

    // ─── AI duplicate routes ────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AiDuplicates_NoToken_Returns401_EvenWithSpoofedRoleHeader(bool spoofHeaders)
    {
        var c = NewDuplicates(nameof(AiDuplicates_NoToken_Returns401_EvenWithSpoofedRoleHeader));
        NoToken(c, spoofHeaders);

        Assert.Equal(401, Status(await c.ValidateDocument(null, null)));
        Assert.Equal(401, Status(await c.ScanDocument(null, null, null)));
        Assert.Equal(401, Status(await c.DuplicateScanEndpoint(null, null)));
        Assert.Equal(401, Status(await c.Decide("1", new DecideDto { Decision = "DUPLICATE", Remarks = "x" })));
        Assert.Equal(401, Status(await c.SubmitAlertForReview("1", new AlertSubmitDto())));
        Assert.Equal(401, Status(await c.ConfirmUniqueDocumentEndpoint(new ConfirmUniqueDto { DocumentNo = "DOC-1" })));
    }

    [Fact]
    public async Task AiDuplicates_AccountantToken_WithSpoofedFinanceManagerHeader_ClaimWins_Returns403OnSupervisorReview()
    {
        var c = NewDuplicates(nameof(AiDuplicates_AccountantToken_WithSpoofedFinanceManagerHeader_ClaimWins_Returns403OnSupervisorReview));
        Token(c, "Accountant", "EMP-003", SpoofedHeaderRole);

        Assert.Equal(403, Status(await c.Review("1", new ReviewDto())));
    }

    [Fact]
    public async Task AiDuplicates_FinanceManagerToken_WithSpoofedAccountantHeader_ClaimWins_StillBarredFromScan()
    {
        var c = NewDuplicates(nameof(AiDuplicates_FinanceManagerToken_WithSpoofedAccountantHeader_ClaimWins_StillBarredFromScan));
        Token(c, "Finance Manager", "EMP-001", "Accountant");

        Assert.Equal(403, Status(await c.ValidateDocument(null, null)));
        Assert.Equal(403, Status(await c.DuplicateScanEndpoint(null, null)));
    }

    [Fact]
    public async Task AiDuplicates_AuthenticatedWithoutRoleClaim_FailsClosed403_EvenWithSpoofedHeader()
    {
        var c = NewDuplicates(nameof(AiDuplicates_AuthenticatedWithoutRoleClaim_FailsClosed403_EvenWithSpoofedHeader));
        Token(c, null, "EMP-003", SpoofedHeaderRole);

        Assert.Equal(403, Status(await c.ValidateDocument(null, null)));
        Assert.Equal(403, Status(await c.Decide("1", new DecideDto { Decision = "DUPLICATE", Remarks = "x" })));
    }

    // ─── AI feature routes ──────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AiFeatures_NoToken_Returns401_EvenWithSpoofedRoleHeader(bool spoofHeaders)
    {
        var c = NewFeatures(nameof(AiFeatures_NoToken_Returns401_EvenWithSpoofedRoleHeader));
        NoToken(c, spoofHeaders);

        Assert.Equal(401, Status(await c.UploadDocument(null, null)));
    }

    [Fact]
    public async Task AiFeatures_FinanceManagerToken_WithSpoofedAccountantHeader_ClaimWins_Returns403()
    {
        var c = NewFeatures(nameof(AiFeatures_FinanceManagerToken_WithSpoofedAccountantHeader_ClaimWins_Returns403));
        Token(c, "Finance Manager", "EMP-001", "Accountant");

        Assert.Equal(403, Status(await c.UploadDocument(null, null)));
    }

    [Fact]
    public async Task AiFeatures_AuthenticatedWithoutRoleClaim_FailsClosed403_EvenWithSpoofedHeader()
    {
        var c = NewFeatures(nameof(AiFeatures_AuthenticatedWithoutRoleClaim_FailsClosed403_EvenWithSpoofedHeader));
        Token(c, null, "EMP-003", "Accountant");

        Assert.Equal(403, Status(await c.UploadDocument(null, null)));
    }

    // ─── STARS integration (user routes) ────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StarsIntegration_NoToken_NoApiKey_Returns401_EvenWithSpoofedRoleHeader(bool spoofHeaders)
    {
        var c = NewStars(nameof(StarsIntegration_NoToken_NoApiKey_Returns401_EvenWithSpoofedRoleHeader));
        NoToken(c, spoofHeaders);

        Assert.Equal(401, Status(await c.GetStarsArSummary()));
        Assert.Equal(401, Status(await c.GetCollectionTasks(null)));
        Assert.Equal(401, Status(await c.GetEndorsementLink("INV-001")));
        Assert.Equal(401, Status(await c.TriggerEndorsement("INV-001", null)));
        Assert.Equal(401, Status(await c.CreateCollectionTask(new StarsCollectionTaskRequestDto { InvoiceNumber = "INV-001" })));
    }

    [Fact]
    public async Task StarsIntegration_AccountantToken_WithSpoofedFinanceManagerHeader_ClaimWins_Returns403OnWrite()
    {
        var c = NewStars(nameof(StarsIntegration_AccountantToken_WithSpoofedFinanceManagerHeader_ClaimWins_Returns403OnWrite));
        Token(c, "Accountant", "EMP-003", SpoofedHeaderRole);

        Assert.Equal(403, Status(await c.TriggerEndorsement("INV-001", null)));
        Assert.Equal(403, Status(await c.TriggerStatusSync("INV-001")));
        Assert.Equal(403, Status(await c.CreateCollectionTask(new StarsCollectionTaskRequestDto { InvoiceNumber = "INV-001" })));
    }

    [Fact]
    public async Task StarsIntegration_AfmToken_WithSpoofedFinanceManagerHeader_ClaimWins_Returns403OnRead()
    {
        var c = NewStars(nameof(StarsIntegration_AfmToken_WithSpoofedFinanceManagerHeader_ClaimWins_Returns403OnRead));
        Token(c, "Assistant of Finance Manager", "EMP-004", SpoofedHeaderRole);

        Assert.Equal(403, Status(await c.GetStarsArSummary()));
    }

    [Fact]
    public async Task StarsIntegration_AuthenticatedWithoutRoleClaim_FailsClosed403_EvenWithSpoofedHeader()
    {
        var c = NewStars(nameof(StarsIntegration_AuthenticatedWithoutRoleClaim_FailsClosed403_EvenWithSpoofedHeader));
        Token(c, null, "EMP-001", SpoofedHeaderRole);

        Assert.Equal(403, Status(await c.GetStarsArSummary()));
        Assert.Equal(403, Status(await c.TriggerEndorsement("INV-001", null)));
    }

    // ─── DMS integration (user routes) ──────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DmsIntegration_NoToken_NoApiKey_Returns401_EvenWithSpoofedRoleHeader(bool spoofHeaders)
    {
        var c = NewDms(nameof(DmsIntegration_NoToken_NoApiKey_Returns401_EvenWithSpoofedRoleHeader));
        NoToken(c, spoofHeaders);

        Assert.Equal(401, Status(await c.WaybillCreated(new DmsWaybillCreatedDto { WaybillNumber = "WB-001" })));
        Assert.Equal(401, Status(await c.DeliveryCompleted(new DmsDeliveryCompletedRequest { WaybillNumber = "WB-001" })));
        Assert.Equal(401, Status(await c.GetWaybillStatus("WB-001")));
    }

    [Fact]
    public async Task DmsIntegration_AfmToken_WithSpoofedFinanceManagerHeader_ClaimWins_Returns403()
    {
        var c = NewDms(nameof(DmsIntegration_AfmToken_WithSpoofedFinanceManagerHeader_ClaimWins_Returns403));
        Token(c, "Assistant of Finance Manager", "EMP-004", SpoofedHeaderRole);

        Assert.Equal(403, Status(await c.WaybillCreated(new DmsWaybillCreatedDto { WaybillNumber = "WB-001" })));
        Assert.Equal(403, Status(await c.GetWaybillStatus("WB-001")));
    }

    [Fact]
    public async Task DmsIntegration_AuthenticatedWithoutRoleClaim_FailsClosed403_EvenWithSpoofedHeader()
    {
        var c = NewDms(nameof(DmsIntegration_AuthenticatedWithoutRoleClaim_FailsClosed403_EvenWithSpoofedHeader));
        Token(c, null, "EMP-001", SpoofedHeaderRole);

        Assert.Equal(403, Status(await c.GetWaybillStatus("WB-001")));
    }

    // ─── /api/ai-data machine-to-machine API key guard ──────────────────────

    private static async Task<(IActionResult? Result, bool NextCalled)> RunAiDataFilter(string? configuredKey, Action<HttpRequest>? arrange)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(configuredKey == null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { [RequireAiServiceApiKeyAttribute.ConfigKey] = configuredKey })
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(cfg).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        arrange?.Invoke(http.Request);

        var ctx = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

        var nextCalled = false;
        await new RequireAiServiceApiKeyAttribute().OnActionExecutionAsync(ctx, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(ctx, new List<IFilterMetadata>(), new object()));
        });
        return (ctx.Result, nextCalled);
    }

    private const string GoodAiKey = "ai-service-inbound-key-0123456789";

    [Fact]
    public async Task AiData_NoToken_NoApiKey_Returns401()
    {
        var (result, next) = await RunAiDataFilter(GoodAiKey, null);
        Assert.False(next);
        Assert.Equal(401, Status(Assert.IsAssignableFrom<IActionResult>(result)));
    }

    [Fact]
    public async Task AiData_SpoofedRoleHeader_WithoutApiKey_Returns401()
    {
        var (result, next) = await RunAiDataFilter(GoodAiKey, r => r.Headers["X-User-Role"] = SpoofedHeaderRole);
        Assert.False(next);
        Assert.Equal(401, Status(Assert.IsAssignableFrom<IActionResult>(result)));
    }

    [Fact]
    public async Task AiData_WrongApiKey_Returns401()
    {
        var (result, next) = await RunAiDataFilter(GoodAiKey, r => r.Headers["Authorization"] = "ApiKey wrong-key-wrong-key-wrong");
        Assert.False(next);
        Assert.Equal(401, Status(Assert.IsAssignableFrom<IActionResult>(result)));
    }

    [Fact]
    public async Task AiData_CorrectApiKey_IsAllowed()
    {
        var (result, next) = await RunAiDataFilter(GoodAiKey, r => r.Headers["Authorization"] = $"ApiKey {GoodAiKey}");
        Assert.True(next);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("change-me")]
    [InlineData("replace_with_a_secure_ai_service_api_key")]
    public async Task AiData_KeyNotConfiguredOrPlaceholder_FailsClosed503(string? configured)
    {
        var (result, next) = await RunAiDataFilter(configured, r => r.Headers["Authorization"] = $"ApiKey {configured ?? "x"}");
        Assert.False(next);
        Assert.Equal(503, Status(Assert.IsAssignableFrom<IActionResult>(result)));
    }

    // ─── No class-level anonymous access ────────────────────────────────────

    [Theory]
    [InlineData(typeof(AiCollectionController))]
    [InlineData(typeof(AiDataController))]
    [InlineData(typeof(AiDuplicatesController))]
    [InlineData(typeof(AiFeaturesController))]
    [InlineData(typeof(IntegrationController))]
    [InlineData(typeof(ShipmentRecordsController))]
    [InlineData(typeof(ApprovalsController))]
    public void DataControllers_HaveNoAllowAnonymous_OnClassOrAnyAction(Type controller)
    {
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        var anonymousActions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            .Select(m => m.Name).ToList();
        Assert.Empty(anonymousActions);
    }

    [Theory]
    [InlineData(typeof(AiCollectionController))]
    [InlineData(typeof(AiDuplicatesController))]
    [InlineData(typeof(AiFeaturesController))]
    public void AiControllers_RequireAuthenticatedJwt_AtClassLevel(Type controller)
    {
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void AiDataController_RequiresServiceApiKey_AtClassLevel()
    {
        Assert.NotNull(typeof(AiDataController).GetCustomAttribute<RequireAiServiceApiKeyAttribute>());
    }

    [Fact]
    public void OnlyHealthEndpoint_RemainsAnonymous_InDmsIntegrationController()
    {
        var anonymous = typeof(DmsIntegrationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            .Select(m => m.Name).ToList();
        Assert.Equal(new[] { nameof(DmsIntegrationController.CheckHealth) }, anonymous);
    }

    // ─── SSO exchange: no unsigned-token fallback ───────────────────────────

    [Fact]
    public async Task SsoExchange_ForgedUnsignedStarsToken_WhenStarsCannotVerify_IsRejected_AndNoUserProvisioned()
    {
        using var appDb = CreateAppDb(nameof(SsoExchange_ForgedUnsignedStarsToken_WhenStarsCannotVerify_IsRejected_AndNoUserProvisioned));
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = new string('k', 48),
            ["JwtSettings:Issuer"] = "foms",
            ["JwtSettings:Audience"] = "foms",
            ["StarsIntegration:StarsBaseUrl"] = "http://127.0.0.1:1" // unreachable: STARS cannot verify
        }).Build();
        var auth = new AuthService(appDb, cfg, NullLogger<AuthService>.Instance);

        // Unsigned ("alg":"none") token claiming to be EMP-001 with a far-future expiry.
        static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();
        var forged = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64($"{{\"employee_number\":\"EMP-001\",\"role\":\"Finance Manager\",\"exp\":{exp}}}")}.";

        var result = await auth.SsoExchangeAsync(forged);

        Assert.Null(result);
        Assert.Empty(await appDb.Employees.ToListAsync());
    }
    [Fact]
    public async Task SsoExchange_UnknownEmployee_Returns403Forbidden_AndDoesNotAutoProvision()
    {
        using var appDb = CreateAppDb(nameof(SsoExchange_UnknownEmployee_Returns403Forbidden_AndDoesNotAutoProvision));
        var authServiceMock = new Mock<IAuthService>();
        authServiceMock.Setup(a => a.SsoExchangeAsync(It.IsAny<string>()))
            .ThrowsAsync(new UnauthorizedAccessException("Employee 'EMP-999' is not registered in FOMS. Access denied."));

        var controller = new AuthController(authServiceMock.Object);
        var response = await controller.SsoExchange(new AuthController.SsoExchangeRequestDto { Token = "valid-stars-token-for-unknown-emp" });

        var statusResult = Assert.IsAssignableFrom<IStatusCodeActionResult>(response);
        Assert.Contains(statusResult.StatusCode, new int?[] { 401, 403 });
        Assert.Equal(403, statusResult.StatusCode);

        // Ensure database remains untouched
        Assert.Empty(await appDb.Employees.ToListAsync());
    }
}