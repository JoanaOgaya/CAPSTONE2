using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Interfaces;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;

namespace FOMS.Application.Services;

public static class ThresholdConfigHelper
{
    public static async Task<decimal> GetAssistantFmThresholdAsync(IApplicationDbContext context, string transactionType = "CashOutflow")
    {
        return await GetAssistantFmThresholdAsync(context.ApprovalThresholdSettings, transactionType);
    }

    public static async Task<decimal> GetAssistantFmThresholdAsync(IQueryable<ApprovalThresholdSetting> settings, string transactionType = "CashOutflow")
    {
        var setting = await settings
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
}
