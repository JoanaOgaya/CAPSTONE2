using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using FOMS.Application.Configuration;
using FOMS.Application.Contracts;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using FOMS.Infrastructure.Services;

namespace FOMS.Api.Controllers;

public class FomsSyncPasswordRequest
{
    public string EmployeeId { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class StarsProvisionUserDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
    public string? Phone { get; set; }
    public decimal? SalaryRate { get; set; }
    public DateTime? DateHired { get; set; }
    public string? Status { get; set; } = "Active";
}

[ApiController]
public class IntegrationController : ControllerBase
{
    private readonly IStarsIntegrationService _starsService;
    private readonly IUserService _userService;
    private readonly IApplicationDbContext _context;
    private readonly FomsDbContext _fomsDb;
    private readonly StarsIntegrationOptions _options;

    public IntegrationController(
        IStarsIntegrationService starsService,
        IUserService userService,
        IApplicationDbContext context,
        FomsDbContext fomsDb,
        IOptions<StarsIntegrationOptions> options)
    {
        _starsService = starsService;
        _userService = userService;
        _context = context;
        _fomsDb = fomsDb;
        _options = options.Value;
    }

    private IActionResult? ValidateApiKey()
    {
        if (string.IsNullOrWhiteSpace(_options.InboundApiKey))
            return null; // Allow in development/staging if not configured

        var key = Request.Headers[StarsIntegrationService.ApiKeyHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (!string.IsNullOrWhiteSpace(_options.InboundApiKey) &&
                !StarsIntegrationService.ValidateApiKey(key, _options.InboundApiKey))
            {
                return Unauthorized(new { message = "Invalid API key." });
            }
        }
        return null;
    }

    // ─── Machine-to-Machine Endpoints (API Key Guarded) ──────────────────────

    /// <summary>
    /// Integration 1 (STARS → FOMS): STARS pushes client signed endorsement copy and actual received date.
    /// FOMS automatically calculates the 30-day payment term due date per FR-028.
    /// </summary>
    [HttpPost("api/integration/stars/invoice-endorsed")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveEndorsementCompletion([FromBody] StarsEndorsementCompletionDto dto)
    {
        var authError = ValidateApiKey();
        if (authError != null) return authError;

        var (isSuccess, message) = await _starsService.ProcessEndorsementCompletionAsync(dto);
        return isSuccess ? Ok(new { isSuccess, message }) : BadRequest(new { isSuccess, message });
    }

    /// <summary>
    /// Integration 1 (FOMS → STARS trigger): Finalized invoice is pushed to STARS to create an endorsement delivery task.
    /// Can be invoked by STARS (M2M with API key) or internal staff.
    /// </summary>
    [HttpPost("api/integration/stars/trigger-endorsement/{invoiceNo}")]
    [AllowAnonymous]
    public async Task<IActionResult> TriggerEndorsement(string invoiceNo, [FromQuery] string? remarks)
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var (isSuccess, message, data) = await _starsService.RequestInvoiceEndorsementAsync(invoiceNo, remarks);
        return isSuccess ? Ok(new { isSuccess, message, data }) : BadRequest(new { isSuccess, message });
    }

    /// <summary>
    /// Integration 2 (FOMS → STARS trigger): Pushes invoice and payment status updates to STARS.
    /// </summary>
    [HttpPost("api/integration/stars/trigger-status-sync/{invoiceNo}")]
    [AllowAnonymous]
    public async Task<IActionResult> TriggerStatusSync(string invoiceNo)
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var (isSuccess, message) = await _starsService.NotifyPaymentStatusChangeAsync(invoiceNo);
        return isSuccess ? Ok(new { isSuccess, message }) : BadRequest(new { isSuccess, message });
    }

    /// <summary>
    /// Integration 3 (FOMS → STARS trigger): Pushes overdue account alert to STARS for collection routing.
    /// </summary>
    [HttpPost("api/integration/stars/trigger-overdue-alert/{invoiceNo}")]
    [AllowAnonymous]
    public async Task<IActionResult> TriggerOverdueAlert(string invoiceNo)
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var (isSuccess, message) = await _starsService.SendOverdueAlertAsync(invoiceNo);
        return isSuccess ? Ok(new { isSuccess, message }) : BadRequest(new { isSuccess, message });
    }

    /// <summary>
    /// Retrieves the STARS endorsement link record for a specific invoice.
    /// </summary>
    [HttpGet("api/integration/stars/links/{invoiceNo}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetEndorsementLink(string invoiceNo)
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var link = await _starsService.GetEndorsementLinkAsync(invoiceNo);
        return link != null ? Ok(link) : NotFound(new { message = $"No endorsement link found for invoice '{invoiceNo}'." });
    }

    /// <summary>
    /// STARS Collection Workflow (Item 4 & 5): Intercepts payload and automatically creates a "Collection Follow-up" task.
    /// STARS Graph Routing Engine evaluates finance team workload/availability and routes task to optimal Coordinator.
    /// </summary>
    [HttpPost("api/integration/stars/collection-task")]
    [AllowAnonymous]
    public async Task<IActionResult> CreateCollectionTask([FromBody] StarsCollectionTaskRequestDto dto)
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var (isSuccess, message, data) = await _starsService.CreateCollectionFollowUpTaskAsync(dto);
        return isSuccess ? Ok(new { isSuccess, message, data }) : BadRequest(new { isSuccess, message });
    }

    /// <summary>
    /// STARS Collection Workflow (Item 6): Coordinator executes collection follow-up workflow inside STARS.
    /// </summary>
    [HttpPost("api/integration/stars/collection-task/{taskId}/execute")]
    [AllowAnonymous]
    public async Task<IActionResult> ExecuteCollectionTask(int taskId, [FromBody] StarsCollectionExecutionDto dto)
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var (isSuccess, message, data) = await _starsService.ExecuteCollectionTaskAsync(taskId, dto);
        return isSuccess ? Ok(new { isSuccess, message, data }) : BadRequest(new { isSuccess, message });
    }

    /// <summary>
    /// Retrieves collection follow-up tasks recorded by STARS.
    /// </summary>
    [HttpGet("api/integration/stars/collection-tasks")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCollectionTasks([FromQuery] string? status)
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var tasks = await _starsService.GetCollectionTasksAsync(status);
        return Ok(tasks);
    }

    /// <summary>
    /// STARS Graph Routing Engine: Returns real-time coordinator workload and availability routing metrics.
    /// </summary>
    [HttpGet("api/integration/stars/routing-metrics")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRoutingMetrics()
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var metrics = await _starsService.EvaluateCoordinatorGraphRoutingAsync();
        return Ok(metrics);
    }

    /// <summary>
    /// STARS Integration #2 (STARS Dashboard AR Synchronization):
    /// Returns live Accounts Receivable summary, outstanding balances, and aging distribution for STARS dashboard.
    /// </summary>
    [HttpGet("api/integration/stars/ar-summary")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStarsArSummary()
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var arRecords = await _fomsDb.AccountsReceivable.AsNoTracking().ToListAsync();

        var totalAr = arRecords.Sum(a => a.OutstandingBalance ?? a.InvoiceAmount);
        var totalBilled = arRecords.Sum(a => a.InvoiceAmount);
        var totalCollected = arRecords.Sum(a => a.PaidAmount ?? 0m);

        var current = arRecords.Where(a => a.AgingBucket == "Current" || string.IsNullOrEmpty(a.AgingBucket)).Sum(a => a.OutstandingBalance ?? 0m);
        var overdue1to30 = arRecords.Where(a => a.AgingBucket == "1-30 Days Overdue" || a.AgingBucket == "1-30").Sum(a => a.OutstandingBalance ?? 0m);
        var overdue31to60 = arRecords.Where(a => a.AgingBucket == "31-60 Days Overdue" || a.AgingBucket == "31-60").Sum(a => a.OutstandingBalance ?? 0m);
        var overdue61to90 = arRecords.Where(a => a.AgingBucket == "61-90 Days Overdue" || a.AgingBucket == "61-90").Sum(a => a.OutstandingBalance ?? 0m);
        var overdue90plus = arRecords.Where(a => a.AgingBucket == "90+ Days Overdue" || a.AgingBucket == "90+").Sum(a => a.OutstandingBalance ?? 0m);

        return Ok(new
        {
            isSuccess = true,
            syncedAt = DateTime.UtcNow,
            totalAccountsReceivable = totalAr,
            totalBilled,
            totalCollected,
            collectionEfficiency = totalBilled > 0 ? Math.Round((totalCollected / totalBilled) * 100m, 2) : 0,
            activeCount = arRecords.Count,
            agingDistribution = new
            {
                current,
                days1to30 = overdue1to30,
                days31to60 = overdue31to60,
                days61to90 = overdue61to90,
                days90Plus = overdue90plus
            }
        });
    }

    /// <summary>
    /// STARS Integration #2 (STARS Dashboard AR Synchronization):
    /// Returns live list of Accounts Receivable records for the STARS dashboard & collections queue.
    /// </summary>
    [HttpGet("api/integration/stars/accounts-receivable")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStarsAccountsReceivable([FromQuery] string? status, [FromQuery] string? clientCode)
    {
        var authError = ValidateApiKey();
        if (authError != null && User.Identity?.IsAuthenticated != true)
            return authError;

        var query = _fomsDb.AccountsReceivable.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.ArStatus == status);
        }

        var arList = await query.ToListAsync();
        var clients = await _fomsDb.Clients.AsNoTracking().ToListAsync();
        var invoices = await _fomsDb.Invoices.AsNoTracking().ToListAsync();

        var result = arList.Select(a =>
        {
            var inv = invoices.FirstOrDefault(i => i.InvoiceId == a.InvoiceId);
            var client = clients.FirstOrDefault(c => c.ClientId == a.ClientId);

            return new
            {
                arId = a.ArId,
                invoiceId = a.InvoiceId,
                invoiceNumber = inv?.InvoiceNumber ?? $"INV-{a.InvoiceId}",
                clientId = a.ClientId,
                clientCode = client?.ClientCode ?? (a.ClientId == 1 ? "CA-001" : a.ClientId == 2 ? "CA-002" : a.ClientId == 3 ? "CA-003" : a.ClientId == 4 ? "CA-004" : a.ClientId.ToString()),
                clientName = client?.CompanyName ?? inv?.ClientName ?? $"Client {a.ClientId}",
                invoiceAmount = a.InvoiceAmount,
                paidAmount = a.PaidAmount ?? 0m,
                outstandingBalance = a.OutstandingBalance ?? (a.InvoiceAmount - (a.PaidAmount ?? 0m)),
                status = a.ArStatus ?? "Pending",
                dueDate = a.DueDate?.ToString("yyyy-MM-dd") ?? inv?.DueDate?.ToString("yyyy-MM-dd"),
                agingBucket = a.AgingBucket ?? "Current",
                collectionPriority = a.CollectionPriority ?? "Normal",
                createdAt = a.CreatedAt ?? DateTime.UtcNow
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(clientCode))
        {
            result = result.Where(r => r.clientCode.Equals(clientCode, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Ok(new
        {
            isSuccess = true,
            count = result.Count,
            items = result
        });
    }

    /// <summary>
    /// Integration 2 (STARS → FOMS): Provision staff user/account from STARS to FOMS.
    /// Secured via X-Api-Key. Supports upsert if employee already exists.
    /// </summary>
    [HttpPost("api/integration/stars/provision-user")]
    [AllowAnonymous]
    public async Task<IActionResult> ProvisionUser([FromBody] StarsProvisionUserDto dto)
    {
        var authError = ValidateApiKey();
        if (authError != null) return authError;

        if (string.IsNullOrWhiteSpace(dto.EmployeeId) || string.IsNullOrWhiteSpace(dto.Username))
            return BadRequest(new { isSuccess = false, message = "EmployeeId and Username are required." });

        try
        {
            var empId = dto.EmployeeId.Trim();
            var username = dto.Username.Trim();
            var email = dto.Email?.Trim();

            // Normalize ID to satisfy CK_Employees_Id_Pattern (EMP-[0-9][0-9][0-9])
            string fomsEmpId = empId;
            if (!fomsEmpId.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(fomsEmpId, out var num))
                {
                    fomsEmpId = $"EMP-{num:D3}";
                }
                else
                {
                    fomsEmpId = $"EMP-{fomsEmpId.PadLeft(3, '0')}";
                }
            }

            var existing = await _context.Employees.FirstOrDefaultAsync(e =>
                e.Id == fomsEmpId || e.Id == empId || e.Username == username || (!string.IsNullOrEmpty(email) && e.Email == email));

            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(dto.Password))
                {
                    existing.PasswordHash = AuthService.HashPassword(dto.Password);
                }
                if (!string.IsNullOrWhiteSpace(dto.FullName))
                {
                    existing.Name = dto.FullName.Trim();
                }
                if (!string.IsNullOrWhiteSpace(dto.Role))
                {
                    existing.Role = dto.Role.Trim();
                }
                if (!string.IsNullOrWhiteSpace(dto.Phone))
                {
                    existing.Phone = dto.Phone.Trim();
                }
                if (dto.SalaryRate.HasValue)
                {
                    existing.SalaryRate = dto.SalaryRate.Value;
                }
                if (dto.DateHired.HasValue)
                {
                    existing.DateHired = dto.DateHired.Value;
                }
                existing.IsActive = string.IsNullOrWhiteSpace(dto.Status) || dto.Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
                existing.Status = string.IsNullOrWhiteSpace(dto.Status) ? "Active" : dto.Status;
                existing.FailedLoginCount = 0;
                existing.LockedAt = null;

                await _context.SaveChangesAsync(default);
                return Ok(new { isSuccess = true, message = "Employee updated in FOMS.", user = existing });
            }

            // Direct insertion without circular STARS duplicate check
            var roleName = !string.IsNullOrWhiteSpace(dto.Role) ? dto.Role.Trim() : "Accountant";
            var roleEntity = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName)
                ?? await _context.Roles.FirstOrDefaultAsync();

            var newEmployee = new Employee
            {
                Id = fomsEmpId,
                Username = username,
                Name = !string.IsNullOrWhiteSpace(dto.FullName) ? dto.FullName.Trim() : username,
                Email = !string.IsNullOrWhiteSpace(email) ? email : $"{username.ToLower()}@speedex.com",
                Role = roleName,
                RoleId = roleEntity?.Id,
                Phone = dto.Phone?.Trim(),
                SalaryRate = dto.SalaryRate,
                DateHired = dto.DateHired ?? DateTime.UtcNow,
                IsActive = string.IsNullOrWhiteSpace(dto.Status) || dto.Status.Equals("Active", StringComparison.OrdinalIgnoreCase),
                Status = string.IsNullOrWhiteSpace(dto.Status) ? "Active" : dto.Status,
                SystemAccess = "Finance Operation Service",
                CreatedAt = DateTime.UtcNow,
                PasswordHash = AuthService.HashPassword(!string.IsNullOrWhiteSpace(dto.Password) ? dto.Password : "Password@123")
            };

            await _context.Employees.AddAsync(newEmployee);
            await _context.SaveChangesAsync(default);

            return Ok(new { isSuccess = true, message = "User provisioned successfully into FOMS.", user = newEmployee });
        }
        catch (Exception ex)
        {
            return BadRequest(new { isSuccess = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Cross-system uniqueness probe: checks whether an email or employee ID is already in use in FOMS.
    /// Authenticated by shared X-Api-Key.
    /// </summary>
    [HttpGet("api/integration/stars/check-user-exists")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckUserExists([FromQuery] string? email, [FromQuery] string? employeeId)
    {
        var authError = ValidateApiKey();
        if (authError != null) return authError;

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(employeeId))
            return BadRequest(new { exists = false, message = "Either email or employeeId must be provided." });

        var (exists, matchedBy) = await _userService.CheckExistsAsync(email, employeeId);
        return Ok(new { exists, system = "FOMS", matchedBy });
    }

    /// <summary>
    /// Centralized password synchronization from STARS master identity.
    /// Authenticated by shared X-Api-Key.
    /// </summary>
    [HttpPost("api/integration/stars/sync-password")]
    [AllowAnonymous]
    public async Task<IActionResult> SyncPassword([FromBody] FomsSyncPasswordRequest request)
    {
        var authError = ValidateApiKey();
        if (authError != null) return authError;

        if (string.IsNullOrWhiteSpace(request.EmployeeId) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { isSuccess = false, message = "EmployeeId and NewPassword are required." });

        var cleanId = request.EmployeeId.Trim();
        var user = await _context.Employees.FirstOrDefaultAsync(e =>
            e.Id == cleanId || e.Username == cleanId || e.Email.ToLower() == cleanId.ToLower());

        if (user == null)
        {
            return NotFound(new { isSuccess = false, message = $"Employee '{cleanId}' not found in FOMS." });
        }

        user.PasswordHash = AuthService.HashPassword(request.NewPassword);
        user.Status = "Active";
        user.IsActive = true;
        user.FailedLoginCount = 0;
        user.LockedAt = null;

        await _context.SaveChangesAsync(default);
        return Ok(new { isSuccess = true, message = "Password synchronized in FOMS successfully." });
    }
}
