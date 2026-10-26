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

[Authorize]
[Route("api/official-receipts")]
[Route("official-receipts")]
[ApiController]
public class OfficialReceiptsController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public OfficialReceiptsController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    private (string role, string? clientId) GetCallerInfo()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? string.Empty;
        var clientId = User.FindFirst("client_id")?.Value;
        return (role, clientId);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (role, clientIdClaim) = GetCallerInfo();

        var query = from r in _fomsDb.OfficialReceipts.AsNoTracking()
                    join p in _fomsDb.Payments.AsNoTracking() on r.PaymentId equals p.PaymentId into pr
                    from p in pr.DefaultIfEmpty()
                    join i in _fomsDb.Invoices.AsNoTracking() on r.InvoiceId equals i.InvoiceId into ir
                    from i in ir.DefaultIfEmpty()
                    join c in _fomsDb.Clients.AsNoTracking() on r.ClientId equals c.ClientId into cr
                    from c in cr.DefaultIfEmpty()
                    select new
                    {
                        r,
                        p,
                        i,
                        c
                    };

        // IAM-06: Client isolation
        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim))
            {
                return Forbid("No client association found in token.");
            }

            query = query.Where(x => x.c != null && (x.c.ClientCode == clientIdClaim || x.c.ClientId.ToString() == clientIdClaim || x.c.DmsClientId == clientIdClaim));
        }

        var results = await query.Select(x => new
        {
            id = x.r.OrNumber ?? ("OR-" + x.r.OrId),
            receiptNumber = x.r.OrNumber ?? ("OR-2026-" + x.r.OrId.ToString("D4")),
            referenceNumber = x.p != null ? x.p.ReferenceNumber : ("REF-" + x.r.OrId),
            paymentId = x.p != null ? (x.p.ReferenceNumber ?? "PAY-" + x.p.PaymentId) : ("PAY-" + x.r.PaymentId),
            clientId = x.c != null ? (x.c.ClientCode ?? "CA-" + x.c.ClientId) : ("CA-" + x.r.ClientId),
            clientName = x.c != null ? x.c.CompanyName : "Client",
            invoiceId = x.i != null ? (x.i.InvoiceNumber ?? "INV-" + x.i.InvoiceId) : ("INV-" + x.r.InvoiceId),
            invoiceNumber = x.i != null ? x.i.InvoiceNumber : ("INV-" + x.r.InvoiceId),
            amount = x.r.IssuedAmount > 0 ? x.r.IssuedAmount : (x.p != null ? x.p.Amount : 0m),
            issuedAt = (x.r.IssuedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
            issuedBy = x.r.IssuedBy ?? "Crystalyn Joyce C. Fajardo",
            paymentMethod = x.p != null ? (x.p.PaymentMethod ?? "Bank Transfer") : "Bank Transfer",
            status = x.r.Status ?? "Issued OR"
        }).ToListAsync();

        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var (role, clientIdClaim) = GetCallerInfo();

        var query = from r in _fomsDb.OfficialReceipts.AsNoTracking()
                    join p in _fomsDb.Payments.AsNoTracking() on r.PaymentId equals p.PaymentId into pr
                    from p in pr.DefaultIfEmpty()
                    join i in _fomsDb.Invoices.AsNoTracking() on r.InvoiceId equals i.InvoiceId into ir
                    from i in ir.DefaultIfEmpty()
                    join c in _fomsDb.Clients.AsNoTracking() on r.ClientId equals c.ClientId into cr
                    from c in cr.DefaultIfEmpty()
                    where r.OrNumber == id || r.OrId.ToString() == id
                    select new
                    {
                        r,
                        p,
                        i,
                        c
                    };

        if (string.Equals(role, "Client", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "CLIENT", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(clientIdClaim))
            {
                return Forbid("No client association found in token.");
            }

            query = query.Where(x => x.c != null && (x.c.ClientCode == clientIdClaim || x.c.ClientId.ToString() == clientIdClaim || x.c.DmsClientId == clientIdClaim));
        }

        var dbReceipt = await query.Select(x => new
        {
            id = x.r.OrNumber ?? ("OR-" + x.r.OrId),
            receiptNumber = x.r.OrNumber ?? ("OR-2026-" + x.r.OrId.ToString("D4")),
            referenceNumber = x.p != null ? x.p.ReferenceNumber : ("REF-" + x.r.OrId),
            paymentId = x.p != null ? (x.p.ReferenceNumber ?? "PAY-" + x.p.PaymentId) : ("PAY-" + x.r.PaymentId),
            clientId = x.c != null ? (x.c.ClientCode ?? "CA-" + x.c.ClientId) : ("CA-" + x.r.ClientId),
            clientName = x.c != null ? x.c.CompanyName : "Client",
            invoiceId = x.i != null ? (x.i.InvoiceNumber ?? "INV-" + x.i.InvoiceId) : ("INV-" + x.r.InvoiceId),
            invoiceNumber = x.i != null ? x.i.InvoiceNumber : ("INV-" + x.r.InvoiceId),
            amount = x.r.IssuedAmount > 0 ? x.r.IssuedAmount : (x.p != null ? x.p.Amount : 0m),
            issuedAt = (x.r.IssuedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss"),
            issuedBy = x.r.IssuedBy ?? "Crystalyn Joyce C. Fajardo",
            paymentMethod = x.p != null ? (x.p.PaymentMethod ?? "Bank Transfer") : "Bank Transfer",
            status = x.r.Status ?? "Issued OR"
        }).FirstOrDefaultAsync();

        return dbReceipt == null ? NotFound(new { message = "Official receipt not found." }) : Ok(dbReceipt);
    }
}
