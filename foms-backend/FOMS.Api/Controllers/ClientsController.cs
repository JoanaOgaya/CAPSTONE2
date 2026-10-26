using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

[Route("api/clients")]
[Route("api/v1/clients")]
[Route("clients")]
[Route("client-accounts")]
[Route("api/client-accounts")]
[Route("api/v1/client-accounts")]
[Authorize]
public class ClientsController : ApiControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public ClientsController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    private (string role, string? clientId, string username) GetCaller()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? string.Empty;
        var clientId = User.FindFirst("client_id")?.Value;
        var user = User.FindFirst("name")?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "User";
        return (role, clientId, user);
    }

    [HttpGet("lookup/{clientCode}")]
    public async Task<IActionResult> LookupClient(string clientCode)
    {
        var (role, clientIdClaim, _) = GetCaller();

        var client = await _fomsDb.Clients
            .FirstOrDefaultAsync(c => c.ClientCode == clientCode || c.Id == clientCode);

        if (client == null)
        {
            return NotFound(new { message = "Client account not found. Please verify your Client Code." });
        }

        // IAM-06: Client isolation
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim) ||
                (!string.Equals(client.Id, clientIdClaim, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(client.ClientCode, clientIdClaim, StringComparison.OrdinalIgnoreCase)))
            {
                return Forbid();
            }
        }

        var clientIntIdStr = client.ClientId?.ToString();
        var invoices = await _fomsDb.Invoices
            .Where(i => i.ClientIdStr == client.Id || i.ClientIdStr == client.ClientCode || (clientIntIdStr != null && i.ClientIdStr == clientIntIdStr))
            .ToListAsync();

        var payments = await _fomsDb.Payments
            .Where(p => p.ClientIdStr == client.Id || p.ClientIdStr == client.ClientCode || (clientIntIdStr != null && p.ClientIdStr == clientIntIdStr))
            .ToListAsync();

        return Ok(new
        {
            client.Id,
            client.ClientCode,
            client.Name,
            client.BusinessName,
            client.ContactPerson,
            client.ContactNumber,
            client.Email,
            client.Address,
            client.Tin,
            client.CreditLimit,
            client.CurrentBalance,
            client.TotalBilled,
            client.TotalPaid,
            client.Status,
            client.DateRegistered,
            client.LastTransaction,
            client.Archived,
            Invoices = invoices.Select(i => new
            {
                i.Id,
                i.InvoiceNo,
                ClientId = i.ClientIdStr ?? (i.ClientId.HasValue ? i.ClientId.Value.ToString() : client.Id),
                i.BillingDate,
                i.DueDate,
                i.Subtotal,
                i.VatRate,
                i.VatAmount,
                i.FreightCharges,
                i.OtherCharges,
                i.TotalAmount,
                i.AmountPaid,
                i.PaymentStatus,
                i.AgingBucket,
                i.DaysOverdue,
                i.Archived
            }).ToList(),
            Payments = payments.Select(p => new
            {
                p.Id,
                InvoiceId = p.InvoiceIdStr,
                p.InvoiceNo,
                ClientId = p.ClientIdStr,
                p.ClientName,
                p.PaymentDate,
                p.Amount,
                p.PaymentMethod,
                p.ReferenceNumber,
                p.ProofImageUrl,
                p.Remarks,
                p.RecordedBy,
                p.DateRecorded
            }).ToList()
        });
    }

    [HttpGet("code/{clientCode}")]
    public async Task<IActionResult> GetClientCodeInfo(string clientCode)
    {
        return await LookupClient(clientCode);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetClients()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
        var clientIdClaim = User.FindFirst("client_id")?.Value;

        var query = _fomsDb.Clients.AsNoTracking().AsQueryable();

        // IAM-06: Client isolation
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim)) return Forbid("No client association found in token.");
            query = query.Where(c => c.Id == clientIdClaim || c.ClientCode == clientIdClaim || (c.ClientId.HasValue && c.ClientId.ToString() == clientIdClaim));
        }

        var clients = await query.ToListAsync();
        var result = clients.Select(c =>
        {
            var code = !string.IsNullOrEmpty(c.ClientCode) ? c.ClientCode : c.Id;
            return new
            {
                id = code,
                clientCode = code,
                clientId = c.Id,
                name = c.Name,
                businessName = c.BusinessName,
                contactPerson = c.ContactPerson,
                contactNumber = c.ContactNumber,
                phone = c.ContactNumber,
                email = c.Email,
                address = c.Address,
                tin = c.Tin,
                region = "Metro Manila",
                serviceArea = "NCR",
                creditLimit = c.CreditLimit,
                currentBalance = c.CurrentBalance,
                totalBilled = c.TotalBilled,
                totalPaid = c.TotalPaid,
                status = c.Status ?? "Active",
                vatStatus = "VATable",
                vatRate = 0.12m,
                billingSchedule = (code == "SHP-001" || code == "CA-002" || c.Name.Contains("Shopee") || code == "TKT-001" || code == "CA-003" || c.Name.Contains("TikTok")) ? "Weekly" : "Monthly",
                createdAt = (c.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
                dateRegistered = c.DateRegistered,
                lastTransaction = c.LastTransaction,
                archived = c.Archived
            };
        }).ToList();
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetClientById(string id)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
        var clientIdClaim = User.FindFirst("client_id")?.Value;

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim) || (!id.Equals(clientIdClaim, StringComparison.OrdinalIgnoreCase) && !clientIdClaim.Contains(id)))
            {
                return Forbid("Access denied to another client's details.");
            }
        }

        var c = await _fomsDb.Clients
            .FirstOrDefaultAsync(cl => cl.Id == id || cl.ClientCode == id || (cl.ClientId.HasValue && cl.ClientId.ToString() == id));

        if (c == null) return NotFound(new { message = $"Client {id} not found" });

        var code = !string.IsNullOrEmpty(c.ClientCode) ? c.ClientCode : c.Id;
        return Ok(new
        {
            id = code,
            clientCode = code,
            clientId = c.Id,
            name = c.Name,
            businessName = c.BusinessName,
            contactPerson = c.ContactPerson,
            contactNumber = c.ContactNumber,
            phone = c.ContactNumber,
            email = c.Email,
            address = c.Address,
            tin = c.Tin,
            region = "Metro Manila",
            serviceArea = "NCR",
            creditLimit = c.CreditLimit,
            currentBalance = c.CurrentBalance,
            totalBilled = c.TotalBilled,
            totalPaid = c.TotalPaid,
            status = c.Status ?? "Active",
            vatStatus = "VATable",
            vatRate = 0.12m,
            billingSchedule = (code == "CA-002" || c.Name.Contains("Shopee") || code == "CA-003" || c.Name.Contains("TikTok")) ? "Weekly" : "Monthly",
            createdAt = (c.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
            dateRegistered = c.DateRegistered,
            lastTransaction = c.LastTransaction,
            archived = c.Archived
        });
    }

    public class ClientInputDto
    {
        public string? Id { get; set; }
        public string? ClientCode { get; set; }
        public string? Name { get; set; }
        public string? BusinessName { get; set; }
        public string? ContactPerson { get; set; }
        public string? ContactNumber { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Tin { get; set; }
        public decimal? CreditLimit { get; set; }
        public string? Status { get; set; }
        public string? BillingSchedule { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateClient([FromBody] ClientInputDto dto)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase))
            return Forbid("Clients cannot create client accounts.");

        var name = !string.IsNullOrWhiteSpace(dto.Name) ? dto.Name : "New Client";
        var code = dto.ClientCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            var cleanLetters = new string(name.Where(char.IsLetter).ToArray());
            var prefix = cleanLetters.Length >= 3 ? cleanLetters[..3].ToUpperInvariant() : "CLI";
            var count = await _fomsDb.Clients.CountAsync(c => c.ClientCode != null && c.ClientCode.StartsWith(prefix));
            code = $"{prefix}-{(count + 1):D3}";
        }

        var contactPhone = !string.IsNullOrWhiteSpace(dto.ContactNumber) ? dto.ContactNumber : (!string.IsNullOrWhiteSpace(dto.Phone) ? dto.Phone : "09000000000");
        var busName = !string.IsNullOrWhiteSpace(dto.BusinessName) ? dto.BusinessName : name;
        var email = !string.IsNullOrWhiteSpace(dto.Email) ? dto.Email : $"{name.ToLower().Replace(" ", "")}@client.com";

        var client = new Client
        {
            Id = code,
            ClientCode = code,
            Name = name,
            CompanyName = busName,
            BusinessName = busName,
            ContactPerson = !string.IsNullOrWhiteSpace(dto.ContactPerson) ? dto.ContactPerson : "Operations Manager",
            ContactNumber = contactPhone,
            Email = email,
            Address = dto.Address ?? "Metro Manila, Philippines",
            Tin = dto.Tin,
            CreditLimit = dto.CreditLimit ?? 100000m,
            CurrentBalance = 0,
            TotalBilled = 0,
            TotalPaid = 0,
            Status = dto.Status ?? "Active",
            DateRegistered = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            LastTransaction = "—",
            Archived = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _fomsDb.Clients.Add(client);
        await _fomsDb.SaveChangesAsync();

        return Ok(new
        {
            id = client.Id,
            clientCode = client.ClientCode,
            name = client.Name,
            businessName = client.BusinessName,
            contactPerson = client.ContactPerson,
            contactNumber = client.ContactNumber,
            phone = client.ContactNumber,
            email = client.Email,
            address = client.Address,
            tin = client.Tin,
            region = "Metro Manila",
            serviceArea = "NCR",
            creditLimit = client.CreditLimit,
            currentBalance = client.CurrentBalance,
            totalBilled = client.TotalBilled,
            totalPaid = client.TotalPaid,
            status = client.Status,
            vatStatus = "VATable",
            vatRate = 0.12m,
            billingSchedule = dto.BillingSchedule ?? "Monthly",
            createdAt = (client.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss")
        });
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteClient(string id)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase))
            return Forbid("Clients cannot delete client accounts.");

        var client = await _fomsDb.Clients.FirstOrDefaultAsync(c => c.Id == id || c.ClientCode == id);
        if (client == null) return NotFound();
        client.Archived = true;
        await _fomsDb.SaveChangesAsync();
        return Ok(new { success = true });
    }

    public record ClientArchiveRequest(bool Archived);

    [HttpPut("{id}/archive")]
    [Authorize]
    public async Task<IActionResult> ArchiveClient(string id, [FromBody] ClientArchiveRequest request)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase))
            return Forbid("Clients cannot archive client accounts.");

        var client = await _fomsDb.Clients.FirstOrDefaultAsync(c => c.Id == id || c.ClientCode == id);
        if (client == null) return NotFound();
        client.Archived = request.Archived;
        await _fomsDb.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> UpdateClient(string id, [FromBody] ClientInputDto dto)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase))
            return Forbid("Clients cannot modify client accounts.");

        var client = await _fomsDb.Clients.FirstOrDefaultAsync(c => c.Id == id || c.ClientCode == id);
        if (client == null) return NotFound(new { message = $"Client {id} not found" });

        if (!string.IsNullOrWhiteSpace(dto.Name)) client.Name = dto.Name;
        if (!string.IsNullOrWhiteSpace(dto.BusinessName)) client.BusinessName = dto.BusinessName;
        else if (!string.IsNullOrWhiteSpace(dto.Name)) client.BusinessName = dto.Name;
        if (!string.IsNullOrWhiteSpace(dto.ContactPerson)) client.ContactPerson = dto.ContactPerson;
        var phone = !string.IsNullOrWhiteSpace(dto.ContactNumber) ? dto.ContactNumber : dto.Phone;
        if (!string.IsNullOrWhiteSpace(phone)) client.ContactNumber = phone;
        if (!string.IsNullOrWhiteSpace(dto.Email)) client.Email = dto.Email;
        if (!string.IsNullOrWhiteSpace(dto.Address)) client.Address = dto.Address;
        if (dto.Tin != null) client.Tin = dto.Tin;
        if (dto.CreditLimit.HasValue) client.CreditLimit = dto.CreditLimit.Value;
        if (!string.IsNullOrWhiteSpace(dto.Status)) client.Status = dto.Status;
        client.UpdatedAt = DateTime.UtcNow;

        await _fomsDb.SaveChangesAsync();

        return Ok(new
        {
            id = client.Id,
            clientCode = client.ClientCode,
            name = client.Name,
            businessName = client.BusinessName,
            contactPerson = client.ContactPerson,
            contactNumber = client.ContactNumber,
            phone = client.ContactNumber,
            email = client.Email,
            address = client.Address,
            tin = client.Tin,
            region = "Metro Manila",
            serviceArea = "NCR",
            creditLimit = client.CreditLimit,
            currentBalance = client.CurrentBalance,
            totalBilled = client.TotalBilled,
            totalPaid = client.TotalPaid,
            status = client.Status,
            vatStatus = "VATable",
            vatRate = 0.12m,
            billingSchedule = dto.BillingSchedule ?? "Monthly",
            createdAt = (client.CreatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss")
        });
    }
}
