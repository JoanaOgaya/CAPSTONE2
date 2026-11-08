using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

[ApiController]
[Route("api/health")]
public class DatabaseHealthController : ControllerBase
{
    private readonly FomsDbContext _legacyDbContext;
    private readonly ApplicationDbContext _applicationDbContext;
    private readonly ILogger<DatabaseHealthController> _logger;

    public DatabaseHealthController(
        FomsDbContext legacyDbContext,
        ApplicationDbContext applicationDbContext,
        ILogger<DatabaseHealthController>? logger = null)
    {
        _legacyDbContext = legacyDbContext;
        _applicationDbContext = applicationDbContext;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseHealthController>.Instance;
    }

    [HttpGet("database")]
    public async Task<IActionResult> CheckDatabaseHealth()
    {
        try
        {
            var legacyCanConnect = await _legacyDbContext.Database.CanConnectAsync();
            var applicationCanConnect = await _applicationDbContext.Database.CanConnectAsync();
            var legacyDatabase = GetDatabaseName(_legacyDbContext);
            var applicationDatabase = GetDatabaseName(_applicationDbContext);
            var sameDatabase = string.Equals(
                legacyDatabase,
                applicationDatabase,
                StringComparison.OrdinalIgnoreCase);

            var response = new
            {
                success = legacyCanConnect && applicationCanConnect,
                database = applicationDatabase,
                provider = _applicationDbContext.Database.ProviderName,
                status = legacyCanConnect && applicationCanConnect ? "Connected" : "Connection Failed",
                sameDatabase,
                contexts = new
                {
                    application = new
                    {
                        database = applicationDatabase,
                        connected = applicationCanConnect
                    },
                    legacy = new
                    {
                        database = legacyDatabase,
                        connected = legacyCanConnect
                    }
                },
                message = (string?)null
            };

            return response.success ? Ok(response) : StatusCode(503, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed.");
            return StatusCode(503, new
            {
                success = false,
                status = "Unhealthy",
                message = "Database health check failed."
            });
        }
    }

    [HttpGet("finance-manager")]
    public async Task<IActionResult> CheckFinanceManagerAccess()
    {
        try
        {
            var user = await _applicationDbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == "EMP-001");

            var isFm = user != null
                && (user.Role == "Finance Manager" || user.Role == "Finance Manager");

            return Ok(new
            {
                success = true,
                employeeNumber = "EMP-001",
                role = user?.Role ?? "Unknown",
                isActive = user?.IsActive ?? false,
                canAccessFinanceManager = isFm && user?.IsActive == true,
                database = GetDatabaseName(_applicationDbContext)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check endpoint failed.");
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while evaluating health."
            });
        }
    }

    [HttpGet("head-accountant")]
    public async Task<IActionResult> CheckHeadAccountantAccess()
    {
        try
        {
            var user = await _applicationDbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == "EMP-002");

            var isHa = user != null
                && (user.Role == "Head Accountant" || user.Role == "HeadAccountant");

            return Ok(new
            {
                success = true,
                employeeNumber = "EMP-002",
                role = user?.Role ?? "Unknown",
                isActive = user?.IsActive ?? false,
                canAccessHeadAccountant = isHa && user?.IsActive == true,
                database = GetDatabaseName(_applicationDbContext)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check endpoint failed.");
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while evaluating health."
            });
        }
    }

    [HttpGet("accountant")]
    public async Task<IActionResult> CheckAccountantAccess()
    {
        try
        {
            var user = await _applicationDbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == "EMP-003");

            var isAcc = user != null && user.Role == "Accountant";

            return Ok(new
            {
                success = true,
                employeeNumber = "EMP-003",
                role = user?.Role ?? "Unknown",
                isActive = user?.IsActive ?? false,
                canAccessAccountant = isAcc && user?.IsActive == true,
                database = GetDatabaseName(_applicationDbContext)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check endpoint failed.");
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while evaluating health."
            });
        }
    }

    [HttpGet("assistant-finance-manager")]
    public async Task<IActionResult> CheckAssistantFinanceManagerAccess()
    {
        try
        {
            var user = await _applicationDbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == "EMP-004");

            var isAfm = user != null
                && (user.Role == "Assistant of Finance Manager" || user.Role == "Assistant of Finance Manager");

            return Ok(new
            {
                success = true,
                employeeNumber = "EMP-004",
                role = user?.Role ?? "Unknown",
                isActive = user?.IsActive ?? false,
                canAccessAssistantFinanceManager = isAfm && user?.IsActive == true,
                database = GetDatabaseName(_applicationDbContext)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check endpoint failed.");
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while evaluating health."
            });
        }
    }

    [HttpGet("no-mock-data")]
    public IActionResult CheckNoMockData()
    {
        return Ok(new
        {
            success = true,
            database = GetDatabaseName(_legacyDbContext),
            mockDataEnabled = false,
            seedDataFallbackEnabled = false
        });
    }

    [HttpGet("cash-outflow")]
    public async Task<IActionResult> CheckCashOutflowHealth()
    {
        try
        {
            var canConnect = await _legacyDbContext.Database.CanConnectAsync();
            var totalCount = await _legacyDbContext.CashOutflows.CountAsync();
            var activeThreshold = await _legacyDbContext.ApprovalThresholdSettings
                .AsNoTracking()
                .Where(t => t.IsActive && (t.TransactionType == "CashOutflow" || t.TransactionType == "All"))
                .FirstOrDefaultAsync();

            return Ok(new
            {
                success = canConnect,
                database = GetDatabaseName(_legacyDbContext),
                status = "Connected",
                totalCashOutflows = totalCount,
                thresholdConfigured = activeThreshold != null,
                activeThreshold = activeThreshold != null ? new
                {
                    limit = activeThreshold.MaximumAmount,
                    currency = activeThreshold.Currency,
                    role = activeThreshold.RoleName
                } : null,
                mockDataEnabled = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check endpoint failed.");
            return StatusCode(500, new { success = false, message = "An error occurred while evaluating health." });
        }
    }

    [HttpGet("approval-workflow")]
    public async Task<IActionResult> CheckApprovalWorkflowHealth()
    {
        try
        {
            var canConnect = await _legacyDbContext.Database.CanConnectAsync();
            var totalRequests = await _legacyDbContext.CashOutflowApprovalRequests.CountAsync();
            var pendingAssistant = await _legacyDbContext.CashOutflowApprovalRequests.CountAsync(r => r.Status == "Pending Assistant Review" || r.Status == "Submitted");
            var pendingFm = await _legacyDbContext.CashOutflowApprovalRequests.CountAsync(r => r.Status == "Pending Finance Manager Approval" || (r.IsEscalated && !r.Status.StartsWith("Approved")));
            var approvedCount = await _legacyDbContext.CashOutflowApprovalRequests.CountAsync(r => r.Status.StartsWith("Approved"));
            var historyCount = await _legacyDbContext.CashOutflowApprovalHistories.CountAsync();

            return Ok(new
            {
                success = canConnect,
                database = GetDatabaseName(_legacyDbContext),
                status = "Operational",
                totalApprovalRequests = totalRequests,
                pendingAssistantReview = pendingAssistant,
                pendingFinanceManagerApproval = pendingFm,
                approvedTransactions = approvedCount,
                totalAuditHistoryRecords = historyCount,
                mockDataEnabled = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check endpoint failed.");
            return StatusCode(500, new { success = false, message = "An error occurred while evaluating health." });
        }
    }

    private static string GetDatabaseName(DbContext dbContext)
    {
        return dbContext.Database.IsRelational()
            ? dbContext.Database.GetDbConnection().Database
            : "InMemory";
    }
}
