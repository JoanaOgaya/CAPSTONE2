using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Interfaces;
using FOMS.Domain.Constants;

namespace FOMS.Application.Services;

public class ThresholdConfigurationService : IThresholdConfigurationService
{
    private readonly IApplicationDbContext _context;

    public ThresholdConfigurationService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> GetAssistantFmThresholdAsync(string transactionType = "CashOutflow")
    {
        var setting = await _context.ApprovalThresholdSettings
            .AsNoTracking()
            .Where(t => t.IsActive && (t.TransactionType == transactionType || t.TransactionType == "CashOutflow" || t.TransactionType == "All"))
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync();

        if (setting != null && setting.MaximumAmount > 0)
        {
            return setting.MaximumAmount;
        }

        return ThresholdConstants.DefaultAssistantFmThreshold;
    }

    public bool ExceedsThreshold(decimal amount, decimal threshold) =>
        ThresholdConstants.ExceedsThreshold(amount, threshold);

    public bool IsWithinThreshold(decimal amount, decimal threshold) =>
        ThresholdConstants.IsWithinThreshold(amount, threshold);
}
