using System.Threading.Tasks;

namespace FOMS.Application.Interfaces;

public interface IThresholdConfigurationService
{
    Task<decimal> GetAssistantFmThresholdAsync(string transactionType = "CashOutflow");
    bool ExceedsThreshold(decimal amount, decimal threshold);
    bool IsWithinThreshold(decimal amount, decimal threshold);
}
