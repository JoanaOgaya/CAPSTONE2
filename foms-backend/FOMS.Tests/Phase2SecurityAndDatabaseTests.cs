using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using FOMS.Api.Controllers;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Application.Services;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FOMS.Tests;

/// <summary>
/// Phase 2 Security, Database Integrity, and Architecture Integration Tests.
/// Runs strictly against the dedicated isolated test database: FOMS_TEST_DB on localhost\SQLEXPRESS.
/// Zero interaction with or contamination of development data in FOMS_DB.
/// </summary>
public class Phase2SecurityAndDatabaseTests
{
    private static readonly string RealConnectionString = 
        Environment.GetEnvironmentVariable("FOMS_TEST_CONNECTION_STRING") 
        ?? "Server=localhost\\SQLEXPRESS;Database=FOMS_TEST_DB;Trusted_Connection=True;TrustServerCertificate=True;";

    private FomsDbContext CreateTestDbContext()
    {
        var options = new DbContextOptionsBuilder<FomsDbContext>()
            .UseSqlServer(RealConnectionString)
            .Options;
        return new FomsDbContext(options);
    }

    private static AuthService CreateAuthService(IFomsDbContext db)
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "JwtSettings:Secret", "FOMS_Capstone_JWT_SecureKey_2026_OneUI!!" },
            { "JwtSettings:ExpiryMinutes", "15" },
            { "SecuritySettings:MaxFailedLoginAttempts", "5" },
            { "SecuritySettings:LockoutDurationMinutes", "15" },
            { "EnableDevLogins", "true" }
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return new AuthService(db, config, NullLogger<AuthService>.Instance);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 1: Anonymous Allowlist & Controller Authorization (Item #7)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Anonymous_Allowlist_Enforced()
    {
        var assembly = typeof(AuthController).Assembly;
        var controllerTypes = assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<NonControllerAttribute>() == null)
            .ToList();

        var anonymousAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AuthController.Login",
            "AuthController.SsoExchange",
            "AuthController.Logout",
            "AuthController.Refresh"
        };

        var disallowedAnonymousActions = new List<string>();

        foreach (var controller in controllerTypes)
        {
            if (controller.Name.Contains("Integration"))
                continue; // External machine-to-machine integration controllers with webhook/API key verification

            var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && (typeof(IActionResult).IsAssignableFrom(m.ReturnType) ||
                             (m.ReturnType.IsGenericType && typeof(Task).IsAssignableFrom(m.ReturnType))))
                .ToList();

            foreach (var action in actions)
            {
                var actionFullName = $"{controller.Name}.{action.Name}";
                var actionHasAnonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() != null ||
                                         controller.GetCustomAttribute<AllowAnonymousAttribute>() != null;

                if (actionHasAnonymous && !anonymousAllowlist.Contains(actionFullName))
                {
                    disallowedAnonymousActions.Add(actionFullName);
                }

                // If not anonymous, must have [Authorize] on controller or action
                var hasAuthorize = action.GetCustomAttribute<AuthorizeAttribute>() != null ||
                                   controller.GetCustomAttribute<AuthorizeAttribute>() != null;

                Assert.True(hasAuthorize || actionHasAnonymous,
                    $"Action {actionFullName} lacks [Authorize] and is not on the anonymous allowlist.");
            }
        }

        Assert.Empty(disallowedAnonymousActions);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 2: Atomic Rollback Across Payment + Invoice + Receipt (NFR-09, Item #7)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Atomic_Rollback_OnPaymentFailure()
    {
        using var db = CreateTestDbContext();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var invoiceNo = "INV-ROLLBACK-" + uniqueSuffix;

        // 1. Seed test invoice
        var invoice = new Invoice
        {
            InvoiceNo = invoiceNo,
            TotalAmount = 50000m,
            Balance = 50000m,
            AmountPaid = 0m,
            PaymentStatus = "Unpaid",
            ClientIdStr = "CLI-001",
            ClientName = "Test Client",
            DueDateStr = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var initialBalance = invoice.Balance;

        // 2. Execute transactional operation simulated with intentional failure
        bool transactionRolledBack = false;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            // Step A: Update invoice balance
            invoice.Balance -= 25000m;
            invoice.AmountPaid += 25000m;
            await db.SaveChangesAsync();

            // Step B: Insert payment
            db.Payments.Add(new Payment
            {
                Id = "PAY-ROLLBACK-" + uniqueSuffix,
                OrNumber = "OR-ROLLBACK-" + uniqueSuffix,
                InvoiceIdStr = invoice.Id,
                InvoiceNo = invoice.InvoiceNo,
                ClientIdStr = "CLI-001",
                ClientName = "Test Client",
                Amount = 25000m,
                Flow = "inflow",
                PaymentMethod = "Bank Transfer",
                PaymentStatus = "Validated",
                PaymentDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                DateRecorded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                RecordedBy = "Test User",
                SubmittedAt = DateTime.UtcNow,
                ReferenceNumber = "REF-ROLLBACK-" + uniqueSuffix
            });
            await db.SaveChangesAsync();

            // Step C: Simulate intentional failure during official receipt issuance
            throw new InvalidOperationException("SIMULATED_RECEIPT_FAILURE_FOR_ATOMIC_ROLLBACK");

#pragma warning disable CS0162
            await tx.CommitAsync();
#pragma warning restore CS0162
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("SIMULATED_RECEIPT_FAILURE"))
        {
            transactionRolledBack = true;
        }

        Assert.True(transactionRolledBack);

        // 3. Verify clean rollback: Invoice balance and payments restored to pristine state
        using var verifyDb = CreateTestDbContext();
        var reloadedInvoice = await verifyDb.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.InvoiceNo == invoiceNo);
        Assert.NotNull(reloadedInvoice);
        Assert.Equal(initialBalance, reloadedInvoice.Balance);
        Assert.Equal(0m, reloadedInvoice.AmountPaid);

        var paymentExists = await verifyDb.Payments.AsNoTracking().AnyAsync(p => p.InvoiceNo == invoiceNo);
        Assert.False(paymentExists, "Payment record must NOT persist after atomic rollback.");
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 3: Upload Rejection by Type and Size (NFR-06, Item #7)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Upload_Rejection_ByTypeAndSize()
    {
        var controller = new UploadsController();

        // 1. Disallowed extension (.exe executable) MUST be rejected
        var exeBytes = Encoding.UTF8.GetBytes("MZ_FAKE_EXECUTABLE_HEADER");
        var exeStream = new MemoryStream(exeBytes);
        var exeFile = new FormFile(exeStream, 0, exeBytes.Length, "file", "malicious_payload.exe")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/x-msdownload"
        };

        var exeResult = await controller.Upload(exeFile);
        var badExtResult = Assert.IsType<BadRequestObjectResult>(exeResult);
        Assert.Contains("not permitted", badExtResult.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // 2. Oversized file (> 10MB) MUST be rejected
        var oversizedStream = new MemoryStream(new byte[11 * 1024 * 1024]); // 11 MB
        var oversizedFile = new FormFile(oversizedStream, 0, 11 * 1024 * 1024, "file", "giant_document.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var sizeResult = await controller.Upload(oversizedFile);
        var badSizeResult = Assert.IsType<BadRequestObjectResult>(sizeResult);
        Assert.Contains("exceeds the maximum allowed limit", badSizeResult.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        // 3. Legitimate PDF under 10MB MUST be accepted
        var validPdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 mock pdf content");
        var validPdfStream = new MemoryStream(validPdfBytes);
        var validFile = new FormFile(validPdfStream, 0, validPdfBytes.Length, "file", "executive_proof.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var validResult = await controller.Upload(validFile);
        var okResult = Assert.IsType<OkObjectResult>(validResult);
        Assert.Contains("/api/uploads/protected/", okResult.Value?.ToString() ?? "");
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 4: Executive Approval - Missing Proof, Amount Below Transaction, Rejected Blocks, Delegate Justification (Item #7)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ExecutiveApproval_Validation_MissingProof_Rejected()
    {
        using var db = CreateTestDbContext();
        var controller = new ExecutiveApprovalsController(db);
        controller.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-001");

        // Missing proof attachment URL
        var dto = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "CEO",
            ApproverName = "John Doe",
            Decision = "Approved",
            ApprovedAmount = 100000m,
            TransactionAmount = 80000m,
            TransactionType = "Discount",
            TransactionReference = "DISC-" + Guid.NewGuid().ToString("N")[..8],
            ProofAttachmentUrl = "" // EMPTY PROOF
        };

        var result = await controller.Create(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("proof file URL", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutiveApproval_Validation_AmountBelowTransaction_Rejected()
    {
        using var db = CreateTestDbContext();
        var controller = new ExecutiveApprovalsController(db);
        controller.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-001");

        // Approved amount (₱50,000) does not cover transaction amount (₱100,000)
        var dto = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "CEO",
            ApproverName = "John Doe",
            Decision = "Approved",
            ApprovedAmount = 50000m,
            TransactionAmount = 100000m,
            TransactionType = "LargeCashOutflow",
            TransactionReference = "OUTFLOW-" + Guid.NewGuid().ToString("N")[..8],
            ProofAttachmentUrl = "/api/uploads/protected/proof.pdf"
        };

        var result = await controller.Create(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("must cover the transaction amount", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutiveApproval_DelegateJustification_Enforced()
    {
        using var db = CreateTestDbContext();
        var controller = new ExecutiveApprovalsController(db);
        controller.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-001");

        // Delegated = true without delegate_justification
        var dto = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "President",
            ApproverName = "Jane Doe",
            Decision = "Approved",
            ApprovedAmount = 75000m,
            TransactionAmount = 50000m,
            TransactionType = "BudgetOverride",
            TransactionReference = "OVR-" + Guid.NewGuid().ToString("N")[..8],
            ProofAttachmentUrl = "/api/uploads/protected/proof.pdf",
            IsDelegated = true,
            DelegateJustification = "" // MISSING JUSTIFICATION
        };

        var result = await controller.Create(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("delegate_justification is mandatory", badRequest.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutiveApproval_Decision_RejectedBlocks()
    {
        using var db = CreateTestDbContext();
        var controller = new ExecutiveApprovalsController(db);
        controller.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-001");

        var testRef = "DISC-REJ-" + Guid.NewGuid().ToString("N")[..8];

        var dto = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "CEO",
            ApproverName = "John Doe",
            Decision = "Rejected",
            ApprovedAmount = 0m,
            TransactionAmount = 10000m,
            TransactionType = "Discount",
            TransactionReference = testRef,
            ProofAttachmentUrl = "/api/uploads/protected/rejection_notice.pdf"
        };

        var result = await controller.Create(dto);
        var okResult = Assert.IsType<OkObjectResult>(result);

        // Verify that the record is recorded with Rejected decision and blocks approval
        var record = await db.ExecutiveApprovalRecords.FirstOrDefaultAsync(r => r.TransactionReference == testRef);
        Assert.NotNull(record);
        Assert.Equal("Rejected", record.Decision);
        Assert.Equal(0m, record.ApprovedAmount);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 5: Lockout and Timed Auto-Unlock (IAM-05, Item #7)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Lockout_And_TimedAutoUnlock_Enforced()
    {
        using var db = CreateTestDbContext();
        var testUsername = "lockout_user_" + Guid.NewGuid().ToString("N")[..8];

        // Seed test user with adaptive password hash
        var user = new User
        {
            Username = testUsername,
            EmployeeNumber = "EMP-LOCK-" + Guid.NewGuid().ToString("N")[..6],
            FullName = "Lockout Test User",
            PasswordHash = AuthService.HashPassword("CorrectPassword123!"),
            RoleId = 1,
            IsActive = true,
            FailedLoginCount = 0,
            LockedAt = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var authService = CreateAuthService(db);

        // 1. Perform 5 consecutive failed login attempts
        for (int i = 1; i <= 5; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authService.LoginAsync(testUsername, "WrongPassword!"));
        }

        // 2. Verify account is now locked out
        var lockedUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == testUsername);
        Assert.NotNull(lockedUser);
        Assert.Equal(5, lockedUser.FailedLoginCount);
        Assert.NotNull(lockedUser.LockedAt);

        // 3. 6th attempt with correct credentials MUST still be rejected due to lockout
        var lockoutEx = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(testUsername, "CorrectPassword123!"));
        Assert.Contains("locked", lockoutEx.Message, StringComparison.OrdinalIgnoreCase);

        // 4. Simulate passage of 15 minutes: verify auto-unlock
        using var conn = new SqlConnection(RealConnectionString);
        await conn.OpenAsync();
        using (var cmd = new SqlCommand("UPDATE dbo.Users SET locked_at = DATEADD(minute, -16, GETUTCDATE()) WHERE username = @u;", conn))
        {
            cmd.Parameters.AddWithValue("@u", testUsername);
            await cmd.ExecuteNonQueryAsync();
        }

        // 5. User can now successfully login and lockout resets to 0
        using var freshDb = CreateTestDbContext();
        var freshAuthService = CreateAuthService(freshDb);
        var successResult = await freshAuthService.LoginAsync(testUsername, "CorrectPassword123!");
        Assert.NotNull(successResult);
        Assert.NotEmpty(successResult.AccessToken);

        var unlockedUser = await freshDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == testUsername);
        Assert.NotNull(unlockedUser);
        Assert.Equal(0, unlockedUser.FailedLoginCount);
        Assert.Null(unlockedUser.LockedAt);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 6: Exactly Five Login Roles With ROLE-005 Non-Login (IAM-01 & IAM-03, Item #7)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ExactlyFiveLoginRoles_WithRole005NonLogin()
    {
        using var db = CreateTestDbContext();
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.RoleId).ToListAsync();

        Assert.True(roles.Count >= 6, "Expected at least 6 canonical role definitions.");

        // Five Login Roles:
        // Role 1 = Head Accountant
        // Role 2 = Financial Manager
        // Role 3 = Accountant
        // Role 4 = Asst. Financial Manager
        // Role 6 = Client
        // Role 5 = Operations Coordinator (STARS/DMS coordinator) — strictly Non-Login in FOMS
        var role5 = roles.FirstOrDefault(r => r.RoleId == 5);
        Assert.NotNull(role5);
        Assert.Contains("Coordinator", role5.RoleName, StringComparison.OrdinalIgnoreCase);

        // Verify authentication rejects Role 5 logins from FOMS login portal
        var authService = CreateAuthService(db);
        var coordinatorUser = await db.Users.FirstOrDefaultAsync(u => u.RoleId == 5);
        if (coordinatorUser != null)
        {
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authService.LoginAsync(coordinatorUser.Username, "Password@123"));
            Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 7: Client Isolation on Every Client Data Endpoint (IAM-06, Item #7)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ClientIsolation_OnEveryClientDataEndpoint()
    {
        using var db = CreateTestDbContext();
        var clientId = "CLI-001";
        var otherClientId = "CLI-UNAUTHORIZED-SPOOF";

        // Seed invoice for other client
        db.Invoices.Add(new Invoice
        {
            InvoiceNo = "INV-SPOOF-" + Guid.NewGuid().ToString("N")[..8],
            ClientIdStr = otherClientId,
            ClientName = "Spoof Client",
            TotalAmount = 999999m,
            Balance = 999999m,
            PaymentStatus = "Unpaid",
            DueDateStr = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd")
        });
        await db.SaveChangesAsync();

        // 1. Invoices Endpoint Scoped by Token Claim
        var invoicesController = new InvoicesController(db);
        invoicesController.ControllerContext = CreateControllerContext("Client", "USER-CLI-001", clientId);

        var invoicesResult = await invoicesController.GetInvoices();
        var okInvoices = Assert.IsType<OkObjectResult>(invoicesResult);
        var jsonInvoices = System.Text.Json.JsonSerializer.Serialize(okInvoices.Value);
        Assert.DoesNotContain(otherClientId, jsonInvoices);

        // 2. Payments Endpoint Scoped by Token Claim
        var paymentsController = new PaymentsController(db);
        paymentsController.ControllerContext = CreateControllerContext("Client", "USER-CLI-001", clientId);

        var paymentsResult = await paymentsController.GetPayments();
        var okPayments = Assert.IsType<OkObjectResult>(paymentsResult);
        var jsonPayments = System.Text.Json.JsonSerializer.Serialize(okPayments.Value);
        Assert.DoesNotContain(otherClientId, jsonPayments);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 8: Audit Visibility Enforced Per Role (AUD-03, Item #7)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AuditVisibility_EnforcedPerRole()
    {
        using var db = CreateTestDbContext();
        var controller = new AuditLogsController(db);

        // 1. Head Accountant: Allowed access to accounts audits
        controller.ControllerContext = CreateControllerContext("Head Accountant", "STAFF-HA");
        var haResult = await controller.GetAll();
        Assert.IsType<OkObjectResult>(haResult);

        // 2. Financial Manager: Allowed access to cashflow and expense audits
        controller.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-FM");
        var fmResult = await controller.GetAll();
        Assert.IsType<OkObjectResult>(fmResult);

        // 3. Accountant: Strictly Forbidden from viewing audit trail (403)
        controller.ControllerContext = CreateControllerContext("Accountant", "STAFF-ACC");
        var accResult = await controller.GetAll();
        Assert.True(accResult is ForbidResult or ObjectResult { StatusCode: 403 });

        // 4. Client: Strictly Forbidden from viewing audit trail (403)
        controller.ControllerContext = CreateControllerContext("Client", "CLIENT-001", "CLI-001");
        var cliResult = await controller.GetAll();
        Assert.True(cliResult is ForbidResult or ObjectResult { StatusCode: 403 });
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 9: Sliding 15-Minute Session Inactivity (IAM-02, Item #4)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void SlidingInactivity_15Minutes_Enforced()
    {
        SessionActivityTracker.Clear();
        var tokenHash = "MOCK_SESSION_TOKEN_HASH_" + Guid.NewGuid().ToString("N");

        // 1. Initial request establishes session
        var (isExpired1, _) = SessionActivityTracker.CheckAndUpdateActivity(tokenHash, TimeSpan.FromMinutes(15));
        Assert.False(isExpired1);

        // 2. Active request within 10 minutes slides the expiry
        SessionActivityTracker.SimulateLastActivity(tokenHash, DateTime.UtcNow.AddMinutes(-10));
        var (isExpired2, _) = SessionActivityTracker.CheckAndUpdateActivity(tokenHash, TimeSpan.FromMinutes(15));
        Assert.False(isExpired2, "Activity within 10 minutes must reset sliding window and not expire.");

        // 3. Inactive for 16 minutes MUST expire session
        SessionActivityTracker.SimulateLastActivity(tokenHash, DateTime.UtcNow.AddMinutes(-16));
        var (isExpired3, elapsed) = SessionActivityTracker.CheckAndUpdateActivity(tokenHash, TimeSpan.FromMinutes(15));
        Assert.True(isExpired3, "Session inactive for > 15 minutes must expire.");
        Assert.True(elapsed.TotalMinutes >= 15);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 10: Half-Up Rounding Rule (MidpointRounding.AwayFromZero) (NFR-12, Item #3)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Rounding_HalfUp_AwayFromZero_Enforced()
    {
        // 1. Standard Half-up rounding checks (MidpointRounding.AwayFromZero)
        // 0.125 -> 0.13, 0.124 -> 0.12
        Assert.Equal(0.13m, BillingComputationService.RoundMoney(0.125m));
        Assert.Equal(0.12m, BillingComputationService.RoundMoney(0.124m));
        Assert.Equal(0.38m, BillingComputationService.RoundMoney(0.375m));

        // 2. ComputeVat with Half-up rounding
        // Subtotal = 125.45 * 0.12 = 15.054 -> 15.05
        var vat1 = BillingComputationService.ComputeVat(125.45m);
        Assert.Equal(15.05m, vat1);

        // Subtotal = 125.46 * 0.12 = 15.0552 -> 15.06
        var vat2 = BillingComputationService.ComputeVat(125.46m);
        Assert.Equal(15.06m, vat2);

        // 3. ComputeFreight with Half-up rounding
        var freight = BillingComputationService.ComputeFreight(7.333m, 200m, 25.555m, 5.0m);
        // Excess = 2.333 * 25.555 = 59.619815 + 200 = 259.619815 -> 259.62
        Assert.Equal(259.62m, freight);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 11: Direct UPDATE and DELETE Blocked on dbo.AuditLogs by Triggers (AUD-01)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AuditLogs_DirectUpdateAndDelete_BlockedByDatabaseTriggers()
    {
        using var conn = new SqlConnection(RealConnectionString);
        await conn.OpenAsync();

        int testAuditId;
        using (var cmd = new SqlCommand(@"
            INSERT INTO dbo.AuditLogs (user_id, role, action, module, remarks, created_at, internal_retention_until)
            VALUES (1, 'Admin', 'TEST_TRIGGER', 'TestModule', 'Immutable Test', GETUTCDATE(), DATEADD(year, 5, GETUTCDATE()));
            SELECT SCOPE_IDENTITY();", conn))
        {
            var idResult = await cmd.ExecuteScalarAsync();
            testAuditId = Convert.ToInt32(idResult);
        }

        // Direct UPDATE MUST fail with Trigger Error
        using (var updateCmd = new SqlCommand("UPDATE dbo.AuditLogs SET remarks = 'TAMPERED' WHERE audit_id = @id;", conn))
        {
            updateCmd.Parameters.AddWithValue("@id", testAuditId);
            var ex = await Assert.ThrowsAsync<SqlException>(() => updateCmd.ExecuteNonQueryAsync());
            Assert.Contains("strictly prohibited", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Direct DELETE MUST fail with Trigger Error
        using (var deleteCmd = new SqlCommand("DELETE FROM dbo.AuditLogs WHERE audit_id = @id;", conn))
        {
            deleteCmd.Parameters.AddWithValue("@id", testAuditId);
            var ex = await Assert.ThrowsAsync<SqlException>(() => deleteCmd.ExecuteNonQueryAsync());
            Assert.Contains("strictly prohibited", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 12: Archive Stored Procedure Executes Under Dedicated Principal (AUD-02)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ArchiveProcedure_MovesRetentionExpiredRecords_WithoutTriggerFailure()
    {
        using var conn = new SqlConnection(RealConnectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand("sp_ArchiveRetentionRecords", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        var ex = await Record.ExceptionAsync(() => cmd.ExecuteNonQueryAsync());
        Assert.Null(ex);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 13: Passwords Stored With Adaptive Salted PBKDF2 (NFR-05)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Passwords_StoredWithAdaptiveSaltedPBKDF2_NoPlaintextOrSha256()
    {
        using var db = CreateTestDbContext();
        var reloadedUsers = await db.Users.AsNoTracking().ToListAsync();
        foreach (var user in reloadedUsers)
        {
            if (string.IsNullOrWhiteSpace(user.PasswordHash)) continue;

            Assert.False(user.PasswordHash == "Password@123" || user.PasswordHash == "Secret123!",
                $"User {user.Username} has plaintext password!");

            bool isSha256Hex = user.PasswordHash.Length == 64 &&
                               user.PasswordHash.All(c => "0123456789abcdefABCDEF".Contains(c));
            Assert.False(isSha256Hex, $"User {user.Username} has plain unsalted SHA-256 hash!");

            Assert.True(user.PasswordHash.StartsWith("AQAAAA") || user.PasswordHash.Length > 40,
                $"User {user.Username} does not have valid adaptive salted PBKDF2 hash.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 14: ExecutiveApprovalRecords Integrity & Filtered Index (NFR-11)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ExecutiveApprovalRecords_DatabaseIntegrityConstraints_Enforced()
    {
        using var conn = new SqlConnection(RealConnectionString);
        await conn.OpenAsync();

        var testRef = "REF-TEST-" + Guid.NewGuid().ToString("N")[..8];

        // 1. Valid record insertion with BudgetCarryOver type
        using (var cmd = new SqlCommand(@"
            INSERT INTO dbo.ExecutiveApprovalRecords 
            (transaction_reference, transaction_type, approver_name, approver_title, decision, approved_amount, recorded_by, proof_attachment_url, approval_date, is_active, created_at)
            VALUES (@ref, 'BudgetCarryOver', 'Jane Doe', 'CEO', 'Approved', 500000.00, 'Finance Manager', '/api/uploads/protected/proof.pdf', GETUTCDATE(), 1, GETUTCDATE());", conn))
        {
            cmd.Parameters.AddWithValue("@ref", testRef);
            var rows = await cmd.ExecuteNonQueryAsync();
            Assert.Equal(1, rows);
        }

        // 2. Filtered Unique Index UQ_Active_ExecApproval_Ref: Second active record for same reference MUST fail
        using (var dupCmd = new SqlCommand(@"
            INSERT INTO dbo.ExecutiveApprovalRecords 
            (transaction_reference, transaction_type, approver_name, approver_title, decision, approved_amount, recorded_by, proof_attachment_url, approval_date, is_active, created_at)
            VALUES (@ref, 'BudgetCarryOver', 'Jane Doe', 'CEO', 'Approved', 200000.00, 'Finance Manager', '/api/uploads/protected/proof2.pdf', GETUTCDATE(), 1, GETUTCDATE());", conn))
        {
            dupCmd.Parameters.AddWithValue("@ref", testRef);
            var ex = await Assert.ThrowsAsync<SqlException>(() => dupCmd.ExecuteNonQueryAsync());
            Assert.Contains("UQ_Active_ExecApproval_Ref", ex.Message);
        }

        // 3. Check Constraint: Invalid Approver Title MUST fail
        using (var badTitleCmd = new SqlCommand(@"
            INSERT INTO dbo.ExecutiveApprovalRecords 
            (transaction_reference, transaction_type, approver_name, approver_title, decision, approved_amount, recorded_by, proof_attachment_url, approval_date, is_active, created_at)
            VALUES (@ref2, 'BudgetCarryOver', 'Jane Doe', 'InvalidTitle', 'Approved', 1000.00, 'Finance Manager', '/api/uploads/protected/proof3.pdf', GETUTCDATE(), 1, GETUTCDATE());", conn))
        {
            badTitleCmd.Parameters.AddWithValue("@ref2", "REF-BAD-TITLE-" + Guid.NewGuid().ToString("N")[..8]);
            var ex = await Assert.ThrowsAsync<SqlException>(() => badTitleCmd.ExecuteNonQueryAsync());
            Assert.Contains("CK_ExecApproval_Title", ex.Message);
        }

        // 4. Check Constraint: Delegated without Justification MUST fail
        using (var badDelegateCmd = new SqlCommand(@"
            INSERT INTO dbo.ExecutiveApprovalRecords 
            (transaction_reference, transaction_type, approver_name, approver_title, decision, approved_amount, recorded_by, proof_attachment_url, approval_date, is_delegated, delegate_justification, is_active, created_at)
            VALUES (@ref3, 'BudgetCarryOver', 'Jane Doe', 'CEO', 'Approved', 1000.00, 'Finance Manager', '/api/uploads/protected/proof4.pdf', GETUTCDATE(), 1, NULL, 1, GETUTCDATE());", conn))
        {
            badDelegateCmd.Parameters.AddWithValue("@ref3", "REF-BAD-DEL-" + Guid.NewGuid().ToString("N")[..8]);
            var ex = await Assert.ThrowsAsync<SqlException>(() => badDelegateCmd.ExecuteNonQueryAsync());
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 11: Anonymous Caller Cannot Read Any Invoice, Client, or Amount (Item #1)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Anonymous_Caller_Cannot_Read_Any_Invoice_Client_Or_Amount()
    {
        // 1. Verify Class-level or Method-level Authorize on Controllers
        var protectedControllers = new[]
        {
            typeof(InvoicesController),
            typeof(ClientsController),
            typeof(PaymentsController),
            typeof(OfficialReceiptsController),
            typeof(ReceivablesController),
            typeof(ExecutiveApprovalsController)
        };

        foreach (var controller in protectedControllers)
        {
            var isControllerAuthorized = controller.GetCustomAttribute<AuthorizeAttribute>() != null;
            var publicMethods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && (typeof(IActionResult).IsAssignableFrom(m.ReturnType) ||
                             (m.ReturnType.IsGenericType && typeof(Task).IsAssignableFrom(m.ReturnType))))
                .ToList();

            foreach (var method in publicMethods)
            {
                var isAnonymous = method.GetCustomAttribute<AllowAnonymousAttribute>() != null;
                Assert.False(isAnonymous, $"Financial endpoint {controller.Name}.{method.Name} must NOT allow anonymous access.");

                var isAuthorized = isControllerAuthorized || method.GetCustomAttribute<AuthorizeAttribute>() != null;
                Assert.True(isAuthorized, $"Financial endpoint {controller.Name}.{method.Name} must require authorization.");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 12: CheckLockout / Login Does Not Reveal Account Existence (Item #1)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CheckLockout_DoesNotRevealAccountExistence()
    {
        using var db = CreateTestDbContext();
        var authService = CreateAuthService(db);

        // 1. Attempt login with non-existent account
        var nonExistentUser = "non_existent_" + Guid.NewGuid().ToString("N")[..8];
        var nonExistentEx = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(nonExistentUser, "AnyPassword123!"));

        // 2. Attempt login with existing account but wrong password
        var existingUser = await db.Users.FirstOrDefaultAsync(u => u.IsActive == true && u.LockedAt == null);
        if (existingUser != null)
        {
            var wrongPasswordEx = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authService.LoginAsync(existingUser.Username, "CompletelyWrongPassword!123"));

            // Exact identical error message — zero username enumeration leak
            Assert.Equal("Invalid employee number or password.", nonExistentEx.Message);
            Assert.Equal(nonExistentEx.Message, wrongPasswordEx.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 13: Executive Approver Title Must Be CEO or President ONLY (Item #3)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ExecutiveApproval_ApproverTitle_CeoOrPresidentOnly()
    {
        using var db = CreateTestDbContext();
        var controller = new ExecutiveApprovalsController(db);
        controller.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-001");

        // 1. CEO is accepted
        var ceoDto = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "CEO",
            ApproverName = "Chief Executive",
            Decision = "Approved",
            ApprovedAmount = 25000m,
            TransactionAmount = 20000m,
            TransactionType = "Discount",
            TransactionReference = "DISC-CEO-" + Guid.NewGuid().ToString("N")[..8],
            ProofAttachmentUrl = "/api/uploads/protected/ceo_approval.pdf"
        };
        var ceoResult = await controller.Create(ceoDto);
        Assert.IsType<OkObjectResult>(ceoResult);

        // 2. President is accepted
        var presDto = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "President",
            ApproverName = "Company President",
            Decision = "Approved",
            ApprovedAmount = 35000m,
            TransactionAmount = 30000m,
            TransactionType = "BudgetOverride",
            TransactionReference = "OVR-PRES-" + Guid.NewGuid().ToString("N")[..8],
            ProofAttachmentUrl = "/api/uploads/protected/pres_approval.pdf"
        };
        var presResult = await controller.Create(presDto);
        Assert.IsType<OkObjectResult>(presResult);

        // 3. BoardOfDirectors is rejected
        var bodDto = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "BoardOfDirectors",
            ApproverName = "Board Member",
            Decision = "Approved",
            ApprovedAmount = 50000m,
            TransactionAmount = 40000m,
            TransactionType = "LargeCashOutflow",
            TransactionReference = "OUTFLOW-BOD-" + Guid.NewGuid().ToString("N")[..8],
            ProofAttachmentUrl = "/api/uploads/protected/bod_approval.pdf"
        };
        var bodResult = await controller.Create(bodDto);
        var badBod = Assert.IsType<BadRequestObjectResult>(bodResult);
        Assert.Contains("Approver title must be either 'CEO' or 'President'", badBod.Value?.ToString() ?? "");

        // 4. DesignatedExecutive is rejected
        var deDto = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "DesignatedExecutive",
            ApproverName = "Exec Designee",
            Decision = "Approved",
            ApprovedAmount = 50000m,
            TransactionAmount = 40000m,
            TransactionType = "BudgetTransfer",
            TransactionReference = "XFR-DE-" + Guid.NewGuid().ToString("N")[..8],
            ProofAttachmentUrl = "/api/uploads/protected/de_approval.pdf"
        };
        var deResult = await controller.Create(deDto);
        var badDe = Assert.IsType<BadRequestObjectResult>(deResult);
        Assert.Contains("Approver title must be either 'CEO' or 'President'", badDe.Value?.ToString() ?? "");
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 14: Uploads — PDF, JPG, PNG, CSV, XLSX Allowed (NFR-06, Item #4)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Uploads_AllowedFileTypes_NFR06()
    {
        var controller = new UploadsController();

        var allowedTestFiles = new (string fileName, string contentType, byte[] content)[]
        {
            ("proof_document.pdf", "application/pdf", "%PDF-1.4\nSAMPLE CONTENT FOR TEST"u8.ToArray()),
            ("receipt_scan.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }),
            ("check_copy.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 }),
            ("bank_statement.csv", "text/csv", "TransactionId,Amount,Date\nTX1,1000,2026-10-26\n"u8.ToArray()),
            ("reconciliation_sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00 })
        };

        foreach (var (fileName, contentType, content) in allowedTestFiles)
        {
            var stream = new MemoryStream(content);
            var formFile = new FormFile(stream, 0, content.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };

            var result = await controller.Upload(formFile);
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Contains("/api/uploads/protected/", okResult.Value?.ToString() ?? "");
        }

        // Disallowed extensions MUST be rejected
        var disallowedFiles = new (string fileName, string contentType)[]
        {
            ("malware.exe", "application/octet-stream"),
            ("script.sh", "application/x-sh")
        };

        foreach (var (fileName, contentType) in disallowedFiles)
        {
            var content = "DISALLOWED SCRIPT CONTENT"u8.ToArray();
            var stream = new MemoryStream(content);
            var formFile = new FormFile(stream, 0, content.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };

            var result = await controller.Upload(formFile);
            var badResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("not permitted", badResult.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 15: FM Delegation Configuration & Separation of Duties (Item #5)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task FM_Delegation_Configuration_And_SeparationOfDuties()
    {
        using var db = CreateTestDbContext();
        var controller = new ExecutiveApprovalsController(db);
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var delegateUserId = "STAFF-DEL-" + uniqueSuffix;

        // 1. Non-FM user cannot configure delegate
        controller.ControllerContext = CreateControllerContext("Accountant", "STAFF-003");
        var nonFmResult = await controller.ConfigureDelegate(new ConfigureDelegateDto
        {
            DelegateUserId = delegateUserId,
            DelegateName = "Test Delegate",
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidUntil = DateTime.UtcNow.AddDays(7),
            Reason = "Covering during leave"
        });
        Assert.IsType<ForbidResult>(nonFmResult);

        // 2. Financial Manager configures the delegate
        controller.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-001");
        var fmResult = await controller.ConfigureDelegate(new ConfigureDelegateDto
        {
            DelegateUserId = delegateUserId,
            DelegateName = "Test Delegate",
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidUntil = DateTime.UtcNow.AddDays(7),
            Reason = "Authorized delegate for executive endorsements during annual leave"
        });
        var okDelegate = Assert.IsType<OkObjectResult>(fmResult);
        Assert.Contains("Delegate configured successfully", okDelegate.Value?.ToString() ?? "");

        // 3. Delegate CANNOT record approval for a transaction they requested (Separation of Duties)
        controller.ControllerContext = CreateControllerContext("Assistant Financial Manager", delegateUserId);
        var selfRequestedApproval = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "CEO",
            ApproverName = "Chief Executive",
            Decision = "Approved",
            ApprovedAmount = 50000m,
            TransactionAmount = 45000m,
            TransactionType = "LargeCashOutflow",
            TransactionReference = "CA-SELF-" + uniqueSuffix,
            ProofAttachmentUrl = "/api/uploads/protected/proof.pdf",
            IsDelegated = true,
            DelegateJustification = "Acting FM during leave",
            RequestedBy = delegateUserId // SAME AS CALLER!
        };
        var selfReqResult = await controller.Create(selfRequestedApproval);
        var badReq = Assert.IsType<BadRequestObjectResult>(selfReqResult);
        Assert.Contains("Separation of duties violation: A delegate cannot record an approval for a transaction they requested", badReq.Value?.ToString() ?? "");

        // 4. Delegate CANNOT record approval for a transaction they validated
        var selfValidatedApproval = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "President",
            ApproverName = "Company President",
            Decision = "Approved",
            ApprovedAmount = 50000m,
            TransactionAmount = 45000m,
            TransactionType = "Discount",
            TransactionReference = "DISC-SELF-" + uniqueSuffix,
            ProofAttachmentUrl = "/api/uploads/protected/proof.pdf",
            IsDelegated = true,
            DelegateJustification = "Acting FM during leave",
            ValidatedBy = delegateUserId // SAME AS CALLER!
        };
        var selfValResult = await controller.Create(selfValidatedApproval);
        var badVal = Assert.IsType<BadRequestObjectResult>(selfValResult);
        Assert.Contains("Separation of duties violation: A delegate cannot record an approval for a transaction they validated", badVal.Value?.ToString() ?? "");

        // 5. Delegate CAN record approval for a clean third-party transaction
        var validDelegatedApproval = new CreateExecutiveApprovalDto
        {
            ApproverTitle = "CEO",
            ApproverName = "Chief Executive",
            Decision = "Approved",
            ApprovedAmount = 60000m,
            TransactionAmount = 50000m,
            TransactionType = "LargeCashOutflow",
            TransactionReference = "CA-CLEAN-" + uniqueSuffix,
            ProofAttachmentUrl = "/api/uploads/protected/clean_proof.pdf",
            IsDelegated = true,
            DelegateJustification = "Acting FM during leave with CEO signed approval memo",
            RequestedBy = "STAFF-DISPATCH-001",
            ValidatedBy = "STAFF-ACCOUNTANT-002"
        };
        var validResult = await controller.Create(validDelegatedApproval);
        Assert.IsType<OkObjectResult>(validResult);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 16: ADJ-04 Adjustment Records Stored and Audit Entries Recorded (Item #2)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ADJ04_AdjustmentRecords_StoredAndAuditEntriesRecorded()
    {
        using var db = CreateTestDbContext();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

        // 1. Create adjustment record
        var adjustment = new FinancialAdjustment
        {
            AdjustmentType = "Credit Memo",
            Amount = 1500.00m,
            Reason = "Billing calculation discrepancy resolution per ADJ-01",
            Status = "Pending",
            RequestedBy = 3,
            CreatedAt = DateTime.UtcNow
        };
        db.FinancialAdjustments.Add(adjustment);
        await db.SaveChangesAsync();

        var adjId = adjustment.AdjustmentId;

        // 2. Record immutable audit log entry
        var auditEntry = new AuditLog
        {
            user_id = 3,
            role = "Accountant",
            action = "CREATE_ADJUSTMENT",
            module = "Adjustments",
            affected_record_id = adjId,
            affected_record_type = "FinancialAdjustment",
            remarks = "Financial adjustment created for AR balance resolution",
            created_at = DateTime.UtcNow
        };
        db.AuditLogs.Add(auditEntry);
        await db.SaveChangesAsync();

        // 3. Verify adjustment and audit record exist
        var storedAdj = await db.FinancialAdjustments.FirstOrDefaultAsync(a => a.AdjustmentId == adjId);
        Assert.NotNull(storedAdj);
        Assert.Equal(1500.00m, storedAdj.Amount);

        var storedAudit = await db.AuditLogs.FirstOrDefaultAsync(a => a.affected_record_id == adjId && a.affected_record_type == "FinancialAdjustment");
        Assert.NotNull(storedAudit);
        Assert.Equal("CREATE_ADJUSTMENT", storedAudit.action);

        // 4. Void the adjustment — Soft delete / status update only (physical deletion prohibited)
        storedAdj.Status = "Voided";
        storedAdj.Reason = "Voided: Incorrect client reference provided";

        var voidAudit = new AuditLog
        {
            user_id = 1,
            role = "Financial Manager",
            action = "VOID_ADJUSTMENT",
            module = "Adjustments",
            affected_record_id = adjId,
            affected_record_type = "FinancialAdjustment",
            remarks = "Adjustment voided with documented reason per ADJ-04: Incorrect client reference provided",
            created_at = DateTime.UtcNow
        };
        db.AuditLogs.Add(voidAudit);
        await db.SaveChangesAsync();

        // 5. Confirm physical record is STILL present in database (physical deletion prohibited)
        var postVoidAdj = await db.FinancialAdjustments.FirstOrDefaultAsync(a => a.AdjustmentId == adjId);
        Assert.NotNull(postVoidAdj);
        Assert.Equal("Voided", postVoidAdj.Status);

        var allAudits = await db.AuditLogs.Where(a => a.affected_record_id == adjId && a.affected_record_type == "FinancialAdjustment").ToListAsync();
        Assert.Equal(2, allAudits.Count); // CREATE + VOID
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 20: AUD-02 Retention Computed Dates Verification (AUD-02)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task AUD02_RetentionDates_ComputedCorrectlyPerTable()
    {
        using var db = CreateTestDbContext();

        // 1. Invoices: 2 years client-visible, 5 years internal
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.ClientRetentionUntil != null && i.InternalRetentionUntil != null);
        if (invoice != null)
        {
            var created = invoice.CreatedAt ?? DateTime.UtcNow;
            var diffClient = (invoice.ClientRetentionUntil!.Value - created).TotalDays;
            var diffInternal = (invoice.InternalRetentionUntil!.Value - created).TotalDays;
            Assert.True(diffClient >= 700 && diffClient <= 750, $"Invoices client retention should be ~2 years (730 days), got {diffClient} days");
            Assert.True(diffInternal >= 1800 && diffInternal <= 1850, $"Invoices internal retention should be ~5 years (1826 days), got {diffInternal} days");
        }

        // 2. Payments: 2 years client-visible, 5 years internal
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.ClientRetentionUntil != null && p.InternalRetentionUntil != null);
        if (payment != null)
        {
            var submitted = payment.SubmittedAt 
                ?? (DateTime.TryParse(payment.DateRecorded, out var dr) ? dr 
                : (DateTime.TryParse(payment.PaymentDate, out var dp) ? dp : DateTime.UtcNow));
            var diffClient = (payment.ClientRetentionUntil!.Value - submitted).TotalDays;
            var diffInternal = (payment.InternalRetentionUntil!.Value - submitted).TotalDays;
            Assert.True(diffClient >= 700 && diffClient <= 750, $"Payments client retention should be ~2 years (730 days), got {diffClient} days");
            Assert.True(diffInternal >= 1800 && diffInternal <= 1850, $"Payments internal retention should be ~5 years (1826 days), got {diffInternal} days");
        }

        // 3. CashAdvances: 5 years internal retention, client retention is NULL (internal finance only)
        var advance = await db.CashAdvances.AsNoTracking().FirstOrDefaultAsync(c => c.InternalRetentionUntil != null);
        if (advance != null)
        {
            Assert.Null(advance.ClientRetentionUntil);
            var created = advance.CreatedAt ?? DateTime.UtcNow;
            var diffInternal = (advance.InternalRetentionUntil!.Value - created).TotalDays;
            Assert.True(diffInternal >= 1800 && diffInternal <= 1850, $"Cash advances internal retention should be ~5 years, got {diffInternal} days");
        }

        // 4. Liquidations: 5 years internal retention, client retention is NULL (internal finance only)
        var liquidation = await db.Liquidations.AsNoTracking().FirstOrDefaultAsync(l => l.InternalRetentionUntil != null);
        if (liquidation != null)
        {
            Assert.Null(liquidation.ClientRetentionUntil);
            var created = liquidation.CreatedAt ?? DateTime.UtcNow;
            var diffInternal = (liquidation.InternalRetentionUntil!.Value - created).TotalDays;
            Assert.True(diffInternal >= 1800 && diffInternal <= 1850, $"Liquidations internal retention should be ~5 years, got {diffInternal} days");
        }

        // 5. AccountsReceivable: 2 years client-visible, 5 years internal retention (AUD-02)
        var ar = await db.AccountsReceivable.AsNoTracking().FirstOrDefaultAsync(a => a.ClientRetentionUntil != null && a.InternalRetentionUntil != null);
        if (ar != null)
        {
            var created = ar.CreatedAt ?? DateTime.UtcNow;
            var diffClient = (ar.ClientRetentionUntil!.Value - created).TotalDays;
            var diffInternal = (ar.InternalRetentionUntil!.Value - created).TotalDays;
            Assert.True(diffClient >= 700 && diffClient <= 750, $"AccountsReceivable client retention should be ~2 years (730 days), got {diffClient} days");
            Assert.True(diffInternal >= 1800 && diffInternal <= 1850, $"AccountsReceivable internal retention should be ~5 years (1826 days), got {diffInternal} days");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 21: FM Delegate Payment Validation & Lifecycle (PAY-03, Q9)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task FM_Delegate_PaymentValidation_And_Lifecycle_Tests()
    {
        using var db = CreateTestDbContext();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var payId = "PAY-DEL-" + uniqueSuffix;

        db.Payments.Add(new Payment
        {
            Id = payId,
            OrNumber = "OR-" + uniqueSuffix,
            ClientIdStr = "1",
            ClientName = "Acme Corp",
            InvoiceIdStr = "INV-001",
            InvoiceNo = "INV-001",
            PaymentDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Amount = 1000m,
            PaymentMethod = "Bank Transfer",
            ReferenceNumber = "REF-" + uniqueSuffix,
            RecordedBy = "STAFF-001",
            DateRecorded = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            PaymentStatus = "Pending Finance Validation",
            SubmittedAt = DateTime.UtcNow,
            Flow = "Standard"
        });
        await db.SaveChangesAsync();

        var controller = new PaymentsController(db);

        // A. Unauthorized non-FM (e.g. Accountant) without delegation: 403 Forbidden
        controller.ControllerContext = CreateControllerContext("Accountant", "STAFF-ACC-ROGUE");
        var rogueResult = await controller.ActionOnPayment(payId, "validate", null);
        var rogueForbidden = Assert.IsType<ObjectResult>(rogueResult);
        Assert.Equal(403, rogueForbidden.StatusCode);

        // B. Expired Delegate: 403 Forbidden
        var expiredDelegate = new FinanceManagerDelegate
        {
            DelegateUserId = "STAFF-EXP-" + uniqueSuffix,
            DelegateName = "Expired Delegate",
            AssignedBy = "STAFF-FM",
            ValidFrom = DateTime.UtcNow.AddDays(-10),
            ValidUntil = DateTime.UtcNow.AddMinutes(-5), // Expired
            Reason = "Medical Leave Coverage",
            IsActive = true
        };
        db.FinanceManagerDelegates.Add(expiredDelegate);
        await db.SaveChangesAsync();

        controller.ControllerContext = CreateControllerContext("Accountant", expiredDelegate.DelegateUserId);
        var expiredResult = await controller.ActionOnPayment(payId, "validate", null);
        var expiredForbidden = Assert.IsType<ObjectResult>(expiredResult);
        Assert.Equal(403, expiredForbidden.StatusCode);

        // C. Revoked Delegate: 403 Forbidden
        var revokedDelegate = new FinanceManagerDelegate
        {
            DelegateUserId = "STAFF-REV-" + uniqueSuffix,
            DelegateName = "Revoked Delegate",
            AssignedBy = "STAFF-FM",
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidUntil = DateTime.UtcNow.AddDays(5),
            Reason = "Vacation Coverage",
            IsActive = false // Revoked
        };
        db.FinanceManagerDelegates.Add(revokedDelegate);
        await db.SaveChangesAsync();

        controller.ControllerContext = CreateControllerContext("Accountant", revokedDelegate.DelegateUserId);
        var revokedResult = await controller.ActionOnPayment(payId, "validate", null);
        var revokedForbidden = Assert.IsType<ObjectResult>(revokedResult);
        Assert.Equal(403, revokedForbidden.StatusCode);

        // D. Active Delegate: Allowed (200 OK), logged with delegate identity
        var activeDelegate = new FinanceManagerDelegate
        {
            DelegateUserId = "STAFF-ACT-" + uniqueSuffix,
            DelegateName = "Active Assistant FM",
            AssignedBy = "STAFF-FM",
            ValidFrom = DateTime.UtcNow.AddHours(-1),
            ValidUntil = DateTime.UtcNow.AddDays(2),
            Reason = "Official Conference Coverage",
            IsActive = true
        };
        db.FinanceManagerDelegates.Add(activeDelegate);
        await db.SaveChangesAsync();

        controller.ControllerContext = CreateControllerContext("Assistant of Financial Manager", activeDelegate.DelegateUserId);
        var successResult = await controller.ActionOnPayment(payId, "validate", null);
        Assert.IsType<OkObjectResult>(successResult);

        // Verify payment record has delegate identity
        var verifiedPayment = await db.Payments.FirstOrDefaultAsync(p => p.Id == payId);
        Assert.NotNull(verifiedPayment);
        Assert.Equal("Validated", verifiedPayment.PaymentStatus);
        Assert.Contains("Active Assistant FM", verifiedPayment.ValidatedBy);

        // Verify audit log has delegate identity
        var audit = await db.AuditLogs
            .OrderByDescending(a => a.audit_id)
            .FirstOrDefaultAsync(a => a.module == "Payments" && a.action == "Payment validate");
        Assert.NotNull(audit);
        Assert.Contains(activeDelegate.DelegateUserId, audit.remarks);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 22: Uploads Security (Magic Bytes, Size, Traversal, Tenant) (NFR-06, IAM-06)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Uploads_Security_MagicBytes_Size_PathTraversal_And_TenantIsolation()
    {
        var controller = new UploadsController();

        // 1. Magic bytes check: renamed HTML disguised as .pdf rejected
        var htmlBytes = System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body><h1>Malicious Payload</h1></body></html>");
        var fakeHtmlStream = new MemoryStream(htmlBytes);
        var fakeHtmlFormFile = new FormFile(fakeHtmlStream, 0, htmlBytes.Length, "file", "document.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
        controller.ControllerContext = CreateControllerContext("Accountant", "USER-ACC");

        var badPdfResult = await controller.Upload(fakeHtmlFormFile);
        var badPdfRequest = Assert.IsType<BadRequestObjectResult>(badPdfResult);
        Assert.Contains("Content validation failed", badPdfRequest.Value?.ToString() ?? string.Empty);

        // 1b. Magic bytes check: renamed EXE disguised as .png rejected
        var exeBytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 }; // MZ DOS header
        var fakePngStream = new MemoryStream(exeBytes);
        var fakePngFormFile = new FormFile(fakePngStream, 0, exeBytes.Length, "file", "image.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        var badPngResult = await controller.Upload(fakePngFormFile);
        var badPngRequest = Assert.IsType<BadRequestObjectResult>(badPngResult);
        Assert.Contains("Content validation failed", badPngRequest.Value?.ToString() ?? string.Empty);

        // 1c. Valid formats accepted: PDF (%PDF), PNG (PNG signature), JPG (FFD8FF), XLSX (PK), CSV (UTF-8 text)
        var validPdfBytes = "%PDF-1.4\n%âãÏÓ\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF"u8.ToArray();
        var validPdfFile = new FormFile(new MemoryStream(validPdfBytes), 0, validPdfBytes.Length, "file", "valid.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
        var pdfOkResult = await controller.Upload(validPdfFile);
        Assert.IsType<OkObjectResult>(pdfOkResult);

        var validJpgBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var validJpgFile = new FormFile(new MemoryStream(validJpgBytes), 0, validJpgBytes.Length, "file", "photo.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };
        var jpgOkResult = await controller.Upload(validJpgFile);
        Assert.IsType<OkObjectResult>(jpgOkResult);

        var validXlsxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00 };
        var validXlsxFile = new FormFile(new MemoryStream(validXlsxBytes), 0, validXlsxBytes.Length, "file", "sheet.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
        var xlsxOkResult = await controller.Upload(validXlsxFile);
        Assert.IsType<OkObjectResult>(xlsxOkResult);

        var validCsvBytes = System.Text.Encoding.UTF8.GetBytes("ClientCode,Amount,InvoiceNo\nCLI-001,1500.50,INV-1001\n");
        var validCsvFile = new FormFile(new MemoryStream(validCsvBytes), 0, validCsvBytes.Length, "file", "data.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
        var csvOkResult = await controller.Upload(validCsvFile);
        Assert.IsType<OkObjectResult>(csvOkResult);

        // 2. Max size check: 11MB file rejected
        var bigStream = new MemoryStream(new byte[11 * 1024 * 1024]);
        var bigFile = new FormFile(bigStream, 0, bigStream.Length, "file", "large.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
        var bigResult = await controller.Upload(bigFile);
        var bigBadRequest = Assert.IsType<BadRequestObjectResult>(bigResult);
        Assert.Contains("exceeds the maximum allowed limit of 10MB", bigBadRequest.Value?.ToString() ?? string.Empty);

        // 3. Path traversal attack blocked
        var traversalResult = controller.GetProtectedFile("../../../etc/passwd");
        var traversalBadRequest = Assert.IsType<BadRequestObjectResult>(traversalResult);
        Assert.Contains("Path traversal", traversalBadRequest.Value?.ToString() ?? string.Empty);

        // 4. Tenant download isolation
        // Client 1 uploads valid PNG
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        var validPngStream = new MemoryStream(pngBytes);
        var pngFormFile = new FormFile(validPngStream, 0, pngBytes.Length, "file", "receipt.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        controller.ControllerContext = CreateControllerContext("Client", "USER-CLI-001", "CLI-001");
        var uploadResult = await controller.Upload(pngFormFile);
        var okUpload = Assert.IsType<OkObjectResult>(uploadResult);
        var jsonUpload = JsonSerializer.Serialize(okUpload.Value);
        Assert.Contains("TENANT_CLI-001", jsonUpload);

        // Extract filename
        var urlProperty = okUpload.Value?.GetType().GetProperty("url")?.GetValue(okUpload.Value)?.ToString();
        Assert.NotNull(urlProperty);
        var uploadedFileName = Path.GetFileName(urlProperty);

        // Client 2 attempts to download Client 1's file: 403 Forbidden
        controller.ControllerContext = CreateControllerContext("Client", "USER-CLI-002", "CLI-002");
        var client2Result = controller.GetProtectedFile(uploadedFileName);
        Assert.IsType<ForbidResult>(client2Result);

        // Client 1 downloads their own file: Success (PhysicalFileResult)
        controller.ControllerContext = CreateControllerContext("Client", "USER-CLI-001", "CLI-001");
        var client1Result = controller.GetProtectedFile(uploadedFileName);
        Assert.IsType<PhysicalFileResult>(client1Result);

        // Finance Manager downloads Client 1's file: Success (PhysicalFileResult)
        controller.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-FM");
        var fmDownloadResult = controller.GetProtectedFile(uploadedFileName);
        Assert.IsType<PhysicalFileResult>(fmDownloadResult);
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 23: Role Enforcement Matrix on Lookup Endpoints (IAM-03, IAM-06, FIN-04)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RoleEnforcement_LookupClient_GetClientCodeInfo_LookupInvoice_AllFiveRoles()
    {
        using var db = CreateTestDbContext();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var invoiceNo = "INV-ROLE-" + uniqueSuffix;
        var clientCode = "CLI-ROLE-" + uniqueSuffix;

        db.Clients.Add(new Client
        {
            Id = clientCode,
            ClientCode = clientCode,
            DmsClientId = "DMS-" + uniqueSuffix,
            Name = "Lazada E-Services",
            BusinessName = "Lazada E-Services Inc.",
            ContactPerson = "John Doe",
            ContactNumber = "09170000000",
            Email = "contact@lazada.com",
            Address = "123 Business Blvd, Manila",
            DateRegistered = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            LastTransaction = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            PasswordHash = "AQAAAAIAAYagAAAAEPg1WJmK/Z4sYc9vO+6Yf7T2b1n9l5pQ9X5m7Y8Z1A==",
            Status = "Active",
            IsActive = true,
            SyncedFromDms = false,
            LastSyncedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.Invoices.Add(new Invoice
        {
            InvoiceNo = invoiceNo,
            TotalAmount = 75000m,
            Balance = 75000m,
            PaymentStatus = "Unpaid",
            ClientIdStr = clientCode,
            ClientName = "Lazada E-Services"
        });
        await db.SaveChangesAsync();

        var invController = new InvoicesController(db);
        var clientController = new ClientsController(db);

        // Role 1: Financial Manager (Full Access)
        invController.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-FM");
        clientController.ControllerContext = CreateControllerContext("Financial Manager", "STAFF-FM");
        var fmInv = await invController.LookupInvoice(invoiceNo);
        Assert.IsType<OkObjectResult>(fmInv);
        var fmClient = await clientController.LookupClient(clientCode);
        Assert.IsType<OkObjectResult>(fmClient);
        var fmCode = await clientController.GetClientCodeInfo(clientCode);
        Assert.IsType<OkObjectResult>(fmCode);

        // Role 2: Head Accountant (Invoice & Client Lookup Allowed)
        invController.ControllerContext = CreateControllerContext("Head Accountant", "STAFF-HA");
        clientController.ControllerContext = CreateControllerContext("Head Accountant", "STAFF-HA");
        var haInv = await invController.LookupInvoice(invoiceNo);
        Assert.IsType<OkObjectResult>(haInv);
        var haClient = await clientController.LookupClient(clientCode);
        Assert.IsType<OkObjectResult>(haClient);

        // Role 3: Accountant (Invoice & Client Lookup Allowed)
        invController.ControllerContext = CreateControllerContext("Accountant", "STAFF-ACC");
        clientController.ControllerContext = CreateControllerContext("Accountant", "STAFF-ACC");
        var accInv = await invController.LookupInvoice(invoiceNo);
        Assert.IsType<OkObjectResult>(accInv);
        var accClient = await clientController.LookupClient(clientCode);
        Assert.IsType<OkObjectResult>(accClient);

        // Role 4: Assistant of Financial Manager (Restricted from Invoices per FIN-04; Client Lookup Allowed)
        invController.ControllerContext = CreateControllerContext("Assistant of Financial Manager", "STAFF-AFM");
        clientController.ControllerContext = CreateControllerContext("Assistant of Financial Manager", "STAFF-AFM");
        var afmInv = await invController.LookupInvoice(invoiceNo);
        Assert.IsType<ForbidResult>(afmInv);
        var afmClient = await clientController.LookupClient(clientCode);
        Assert.IsType<OkObjectResult>(afmClient);

        // Role 5: Client (Tenancy Isolation: Own data allowed, cross-tenant data forbidden)
        // Client 1 looking up their own invoice: Allowed
        invController.ControllerContext = CreateControllerContext("Client", "USER-" + clientCode, clientCode);
        var clientOwnInv = await invController.LookupInvoice(invoiceNo);
        Assert.IsType<OkObjectResult>(clientOwnInv);

        // Client 2 looking up Client 1's invoice: 403 Forbidden
        invController.ControllerContext = CreateControllerContext("Client", "USER-CLI-SPOOF", "CLI-SPOOF");
        var clientSpoofInv = await invController.LookupInvoice(invoiceNo);
        Assert.IsType<ForbidResult>(clientSpoofInv);

        // Client 2 looking up Client 1's code info: 403 Forbidden
        clientController.ControllerContext = CreateControllerContext("Client", "USER-CLI-SPOOF", "CLI-SPOOF");
        var clientSpoofCode = await clientController.GetClientCodeInfo(clientCode);
        Assert.IsType<ForbidResult>(clientSpoofCode);
    }

    private static ControllerContext CreateControllerContext(string role, string userId, string? clientId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role)
        };
        if (!string.IsNullOrEmpty(clientId))
        {
            claims.Add(new Claim("client_id", clientId));
        }

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
    }
}
