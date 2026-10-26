using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FOMS.Domain.Entities.SqlServer;
using FOMS.Infrastructure.Persistence;

namespace FOMS.Api.Controllers;

[Authorize]
[Route("api/notifications")]
[Route("api/v1/notifications")]
[ApiController]
public class NotificationsController : ControllerBase
{
    private readonly FomsDbContext _fomsDb;

    public NotificationsController(FomsDbContext fomsDb)
    {
        _fomsDb = fomsDb;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _fomsDb.Notifications.AsNoTracking().ToListAsync());

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkRead(string id)
    {
        var notification = await _fomsDb.Notifications.FirstOrDefaultAsync(n => n.Id == id);
        if (notification == null) return NotFound();

        notification.Read = true;
        await _fomsDb.SaveChangesAsync();
        return Ok(notification);
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var unread = await _fomsDb.Notifications.Where(n => !n.Read).ToListAsync();
        foreach (var n in unread)
        {
            n.Read = true;
        }
        await _fomsDb.SaveChangesAsync();
        return Ok(new { count = unread.Count });
    }
}
