using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FOMS.Application.DTOs;
using FOMS.Application.Interfaces;
using FOMS.Domain.Constants;
using FOMS.Domain.Entities.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace FOMS.Application.Services;

public class CeoApprovalGateService : ICeoApprovalGateService
{
    private readonly IApplicationDbContext _context;
    private readonly string _attachmentStoragePath;

    public CeoApprovalGateService(IApplicationDbContext context)
    {
        _context = context;
        // Secure path outside wwwroot
        _attachmentStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "SecureAttachments", "CeoApprovals");
        if (!Directory.Exists(_attachmentStoragePath))
        {
            Directory.CreateDirectory(_attachmentStoragePath);
        }
    }

    private static DateTime GetManilaToday()
    {
        var utcNow = DateTime.UtcNow;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");
        return TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz).Date;
    }

    private static bool IsFinanceManager(string role)
    {
        return !string.IsNullOrWhiteSpace(role) &&
               role.Equals("Finance Manager", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHeadAccountant(string role)
    {
        return !string.IsNullOrWhiteSpace(role) &&
               role.Equals("Head Accountant", StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Verify Item Exists & Matches Type
    // ─────────────────────────────────────────────────────────────────────────
    private async Task VerifyItemExistsAsync(string itemType, string itemId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("ItemId is strictly required.", nameof(itemId));

        bool exists = false;
        switch (itemType)
        {
            case CeoApprovalItemTypes.AnnualBudget:
                if (int.TryParse(itemId, out int fy))
                {
                    exists = await _context.AnnualBudgets.AnyAsync(b => b.FiscalYear == fy, ct);
                }
                break;

            case CeoApprovalItemTypes.BudgetTransfer:
                if (int.TryParse(itemId, out int transferId))
                {
                    exists = await _context.BudgetTransfers.AnyAsync(t => t.TransferId == transferId, ct);
                }
                break;

            case CeoApprovalItemTypes.BudgetCarryOver:
                // Carry-over references fiscal year / month or request id
                exists = !string.IsNullOrWhiteSpace(itemId);
                break;

            case CeoApprovalItemTypes.BudgetOverride:
                if (int.TryParse(itemId, out int overrideId))
                {
                    exists = await _context.BudgetOverrides.AnyAsync(o => o.OverrideId == overrideId, ct);
                }
                break;

            case CeoApprovalItemTypes.Reimbursement:
                if (int.TryParse(itemId, out int reimId))
                {
                    exists = await _context.Reimbursements.AnyAsync(r => r.ReimbursementId == reimId, ct);
                }
                break;

            case CeoApprovalItemTypes.CashOutflow:
                bool caExists = await _context.CashAdvances.AnyAsync(a => a.AdvanceId == itemId, ct);
                bool coExists = int.TryParse(itemId, out int coId) && await _context.CashOutflows.AnyAsync(c => c.OutflowId == coId, ct);
                bool ddExists = int.TryParse(itemId, out int ddId) && await _context.DirectDisbursements.AnyAsync(d => d.DisbursementId == ddId, ct);
                bool reimExists = await _context.Reimbursements.AnyAsync(r => r.ReferenceNumber == itemId || r.ReimbursementId.ToString() == itemId, ct);
                exists = caExists || coExists || ddExists || reimExists;
                break;

            case CeoApprovalItemTypes.InvoiceDiscount:
            case CeoApprovalItemTypes.BadDebtWriteOff:
                exists = await _context.Waybills.AnyAsync(w => w.InvoiceNo == itemId, ct) ||
                         await _context.Invoices.AnyAsync(i => i.InvoiceNo == itemId, ct) ||
                         !string.IsNullOrWhiteSpace(itemId);
                break;

            case CeoApprovalItemTypes.AnnualFinancialStatement:
                if (int.TryParse(itemId, out int afsYear))
                {
                    exists = afsYear >= 2020 && afsYear <= 2100;
                }
                break;

            default:
                throw new ArgumentException($"Invalid item type '{itemType}'. Allowed types: {string.Join(", ", CeoApprovalItemTypes.AllowedTypes)}.");
        }

        if (!exists)
        {
            throw new KeyNotFoundException($"Item with ID '{itemId}' not found for type '{itemType}'.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Resolve Thresholds (Setting A vs Setting B)
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<decimal> GetLimitAAsync(CancellationToken ct)
    {
        var setting = await _context.ApprovalThresholdSettings
            .Where(s => s.TransactionType == "CashOutflow" && s.IsActive)
            .OrderByDescending(s => s.MaximumAmount)
            .FirstOrDefaultAsync(ct);

        return setting?.MaximumAmount ?? ApprovalThresholdConfig.DefaultAssistantFmThreshold;
    }

    public async Task<CeoApprovalThresholdDto> GetCurrentThresholdAsync(DateTime? effectiveDate = null, CancellationToken ct = default)
    {
        var targetDate = effectiveDate ?? DateTime.UtcNow;
        var limitA = await GetLimitAAsync(ct);

        var setting = await _context.CeoApprovalThresholdSettings
            .Where(s => s.SettingKey == "CSH-09_CEO_THRESHOLD" &&
                        s.EffectiveFrom <= targetDate &&
                        (s.EffectiveTo == null ? s.IsActive : s.EffectiveTo > targetDate))
            .OrderByDescending(s => s.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (setting == null)
        {
            return new CeoApprovalThresholdDto
            {
                SettingId = 0,
                SettingKey = "CSH-09_CEO_THRESHOLD",
                ThresholdAmount = null,
                EffectiveCeoLimit = limitA,
                Currency = "PHP",
                EffectiveFrom = DateTime.UtcNow,
                EffectiveTo = null,
                IsActive = true,
                UpdatedBy = "System (Fail-safe)",
                UpdatedAt = DateTime.UtcNow
            };
        }

        return new CeoApprovalThresholdDto
        {
            SettingId = setting.SettingId,
            SettingKey = setting.SettingKey,
            ThresholdAmount = setting.ThresholdAmount,
            EffectiveCeoLimit = setting.ThresholdAmount ?? limitA,
            Currency = setting.Currency,
            EffectiveFrom = setting.EffectiveFrom,
            EffectiveTo = setting.EffectiveTo,
            IsActive = setting.IsActive,
            UpdatedBy = setting.UpdatedBy,
            UpdatedAt = setting.UpdatedAt
        };
    }

    public async Task<CeoApprovalThresholdDto> UpdateThresholdAsync(
        UpdateCeoThresholdRequest request, string updatedByEmployeeId, string userRole, CancellationToken ct = default)
    {
        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only the Finance Manager can update Setting (B) (CSH-09 CEO threshold).");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("A reason is strictly required when changing the CEO threshold.");
        }

        decimal limitA = await GetLimitAAsync(ct);

        if (request.ThresholdAmount.HasValue)
        {
            if (request.ThresholdAmount.Value <= 0)
            {
                throw new ArgumentException("Threshold amount must be strictly greater than 0.");
            }

            if (request.ThresholdAmount.Value < limitA)
            {
                throw new InvalidOperationException(
                    $"Setting (B) (₱{request.ThresholdAmount.Value:N2}) cannot be set lower than Limit (A) Assistant FM limit (₱{limitA:N2}).");
            }
        }

        var now = DateTime.UtcNow;
        var effectiveFrom = request.EffectiveFrom ?? now;

        // Fetch current active setting
        var activeSetting = await _context.CeoApprovalThresholdSettings
            .Where(s => s.SettingKey == "CSH-09_CEO_THRESHOLD" && s.IsActive)
            .FirstOrDefaultAsync(ct);

        decimal? oldAmount = activeSetting?.ThresholdAmount;

        if (activeSetting != null)
        {
            activeSetting.IsActive = false;
            activeSetting.EffectiveTo = effectiveFrom;
            activeSetting.UpdatedAt = now;
        }

        var newSetting = new CeoApprovalThresholdSetting
        {
            SettingKey = "CSH-09_CEO_THRESHOLD",
            ThresholdAmount = request.ThresholdAmount,
            Currency = "PHP",
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            IsActive = true,
            UpdatedBy = updatedByEmployeeId,
            UpdatedAt = now
        };

        _context.CeoApprovalThresholdSettings.Add(newSetting);
        await _context.SaveChangesAsync(ct);

        var log = new CeoApprovalThresholdChangeLog
        {
            SettingId = newSetting.SettingId,
            OldAmount = oldAmount,
            NewAmount = request.ThresholdAmount,
            ChangedBy = updatedByEmployeeId,
            ChangedAt = now,
            Reason = request.Reason.Trim()
        };

        _context.CeoApprovalThresholdChangeLogs.Add(log);
        await _context.SaveChangesAsync(ct);

        return await GetCurrentThresholdAsync(now, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Gate Check and Enforcement
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<CeoApprovalGateCheckResult> CheckGateAsync(
        string itemType, string itemId, decimal? amount = null, DateTime? requestDate = null, CancellationToken ct = default)
    {
        if (!CeoApprovalItemTypes.AllowedTypes.Contains(itemType))
        {
            throw new ArgumentException($"Invalid item type '{itemType}'.", nameof(itemType));
        }

        var targetDate = requestDate ?? DateTime.UtcNow;
        var thresholdDto = await GetCurrentThresholdAsync(targetDate, ct);
        decimal limitA = await GetLimitAAsync(ct);

        bool isRequired = false;
        bool usedFailSafe = thresholdDto.IsUnsetFailSafe;
        decimal effectiveCeoLimit = thresholdDto.EffectiveCeoLimit;

        if (CeoApprovalItemTypes.AlwaysRequiredTypes.Contains(itemType))
        {
            isRequired = true;
        }
        else
        {
            // Threshold-dependent items: BudgetOverride, Reimbursement, CashOutflow
            if (amount.HasValue)
            {
                isRequired = amount.Value > effectiveCeoLimit;
            }
            else
            {
                isRequired = true;
            }
        }

        if (!isRequired)
        {
            return new CeoApprovalGateCheckResult
            {
                IsRequired = false,
                IsSatisfied = true,
                ItemType = itemType,
                ItemId = itemId,
                EvaluatedAmount = amount,
                ThresholdInForce = effectiveCeoLimit,
                UsedFailSafeLimit = usedFailSafe,
                Message = "No CEO/President approval record required for this transaction."
            };
        }

        // Fetch latest record for ItemType + ItemId
        var latestRecord = await _context.CeoApprovalRecords
            .Where(r => r.ItemType == itemType && r.ItemId == itemId && r.IsActive)
            .OrderByDescending(r => r.EnteredAt)
            .ThenByDescending(r => r.RecordId)
            .FirstOrDefaultAsync(ct);

        if (latestRecord == null)
        {
            return new CeoApprovalGateCheckResult
            {
                IsRequired = true,
                IsSatisfied = false,
                ItemType = itemType,
                ItemId = itemId,
                EvaluatedAmount = amount,
                ThresholdInForce = effectiveCeoLimit,
                UsedFailSafeLimit = usedFailSafe,
                Message = $"Gate blocked: Transaction strictly requires CEO/President approval record for {itemType} '{itemId}'."
            };
        }

        // Fetch confirmations for this record
        var confirmations = await _context.CeoApprovalConfirmations
            .Where(c => c.RecordId == latestRecord.RecordId)
            .ToListAsync(ct);

        bool isEffective = false;
        string detailMessage = string.Empty;

        if (latestRecord.Status == "Rejected")
        {
            isEffective = false;
            detailMessage = $"Gate blocked: CEO/President rejected {itemType} '{itemId}'. Reason: {latestRecord.RejectionReason}";
        }
        else if (latestRecord.Status == "Approved")
        {
            if (latestRecord.IsCorrection)
            {
                // Correction requires confirmation by a second person
                var correctionConfirmation = confirmations.FirstOrDefault(c => c.Kind == "Correction");
                if (correctionConfirmation == null)
                {
                    isEffective = false;
                    detailMessage = $"Gate blocked: Correction record {latestRecord.RecordId} requires confirmation by a second authorized employee.";
                }
                else
                {
                    isEffective = true;
                }
            }
            else if (CeoApprovalItemTypes.MakerCheckerConfirmationRequiredTypes.Contains(itemType))
            {
                // Items 2 & 3 require Head Accountant recording confirmation
                var haConfirmation = confirmations.FirstOrDefault(c => c.Kind == "Recording");
                if (haConfirmation == null)
                {
                    isEffective = false;
                    detailMessage = $"Gate blocked: {itemType} '{itemId}' requires Head Accountant maker-checker recording confirmation.";
                }
                else
                {
                    isEffective = true;
                }
            }
            else
            {
                isEffective = true;
            }
        }

        var dto = MapToDto(latestRecord, confirmations, isEffective);

        return new CeoApprovalGateCheckResult
        {
            IsRequired = true,
            IsSatisfied = isEffective,
            ItemType = itemType,
            ItemId = itemId,
            EvaluatedAmount = amount,
            ThresholdInForce = effectiveCeoLimit,
            UsedFailSafeLimit = usedFailSafe,
            Message = isEffective ? "CEO/President approval record is satisfied." : detailMessage,
            EffectiveRecord = dto
        };
    }

    public async Task EnsureGateSatisfiedAsync(
        string itemType, string itemId, decimal? amount = null, DateTime? requestDate = null, CancellationToken ct = default)
    {
        var result = await CheckGateAsync(itemType, itemId, amount, requestDate, ct);
        if (!result.IsSatisfied)
        {
            throw new UnauthorizedAccessException(result.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Record Approval / Rejection
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<CeoApprovalRecordDto> RecordApprovalAsync(
        CreateCeoApprovalRecordRequest request, string enteredByEmployeeId, string userRole, CancellationToken ct = default)
    {
        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only the Finance Manager can record a CEO/President approval or rejection.");
        }

        if (!CeoApprovalItemTypes.AllowedTypes.Contains(request.ItemType))
        {
            throw new ArgumentException($"Invalid item type '{request.ItemType}'. Allowed types: {string.Join(", ", CeoApprovalItemTypes.AllowedTypes)}.");
        }

        if (string.IsNullOrWhiteSpace(request.ItemId))
        {
            throw new ArgumentException("ItemId is required.");
        }

        if (request.Status != "Approved" && request.Status != "Rejected")
        {
            throw new ArgumentException("Status must be strictly 'Approved' or 'Rejected'. Pending is derived and not stored.");
        }

        if (request.Status == "Rejected" && string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            throw new ArgumentException("RejectionReason is strictly mandatory when Status is 'Rejected'.");
        }

        var manilaToday = GetManilaToday();
        if (request.ApprovalDate.Date > manilaToday)
        {
            throw new ArgumentException($"ApprovalDate ({request.ApprovalDate:yyyy-MM-dd}) cannot be in the future (Manila date is {manilaToday:yyyy-MM-dd}).");
        }

        await VerifyItemExistsAsync(request.ItemType, request.ItemId, ct);

        // Check if an original non-correction record already exists
        var existingRecord = await _context.CeoApprovalRecords
            .Where(r => r.ItemType == request.ItemType && r.ItemId == request.ItemId && !r.IsCorrection)
            .FirstOrDefaultAsync(ct);

        if (existingRecord != null)
        {
            throw new InvalidOperationException(
                $"A CEO approval record already exists for {request.ItemType} '{request.ItemId}'. Modifications must be recorded via a correction record.");
        }

        // Validate and handle attachment
        string? attachmentPath = null;
        string? attachmentFileName = null;
        bool hasAttachment = false;

        if (request.AttachmentBytes != null && request.AttachmentBytes.Length > 0)
        {
            if (!CeoApprovalItemTypes.AttachmentAllowedTypes.Contains(request.ItemType))
            {
                throw new ArgumentException($"Attachments are permitted only on items 1, 6, and 8 ({string.Join(", ", CeoApprovalItemTypes.AttachmentAllowedTypes)}).");
            }

            var saved = await ValidateAndSaveAttachmentAsync(request.AttachmentBytes, request.AttachmentFileName ?? "attachment", ct);
            attachmentPath = saved.Path;
            attachmentFileName = saved.FileName;
            hasAttachment = true;
        }

        var record = new CeoApprovalRecord
        {
            ItemType = request.ItemType,
            ItemId = request.ItemId,
            Status = request.Status,
            ApproverLabel = "CEO/President (in person)",
            EnteredBy = enteredByEmployeeId,
            EnteredAt = DateTime.UtcNow,
            ApprovalDate = request.ApprovalDate.Date,
            Notes = request.Notes?.Trim(),
            RejectionReason = request.Status == "Rejected" ? request.RejectionReason?.Trim() : null,
            AttachmentPath = attachmentPath,
            AttachmentFileName = attachmentFileName,
            HasAttachment = hasAttachment,
            IsCorrection = false,
            OriginalRecordId = null,
            CorrectionReason = null,
            IsActive = true
        };

        _context.CeoApprovalRecords.Add(record);
        await _context.SaveChangesAsync(ct);

        // If rejected, handle release of reservations for CashAdvance and Reimbursement
        if (request.Status == "Rejected")
        {
            await HandleRejectionCleanupAsync(request.ItemType, request.ItemId, request.RejectionReason!, ct);
        }

        return MapToDto(record, new List<CeoApprovalConfirmation>(), record.Status == "Approved" && !CeoApprovalItemTypes.MakerCheckerConfirmationRequiredTypes.Contains(record.ItemType));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Record Correction
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<CeoApprovalRecordDto> RecordCorrectionAsync(
        CreateCeoApprovalCorrectionRequest request, string enteredByEmployeeId, string userRole, CancellationToken ct = default)
    {
        if (!IsFinanceManager(userRole))
        {
            throw new UnauthorizedAccessException("Only the Finance Manager can record a correction.");
        }

        if (string.IsNullOrWhiteSpace(request.CorrectionReason))
        {
            throw new ArgumentException("CorrectionReason is strictly required for correction records.");
        }

        var originalRecord = await _context.CeoApprovalRecords
            .FirstOrDefaultAsync(r => r.RecordId == request.OriginalRecordId, ct);

        if (originalRecord == null)
        {
            throw new KeyNotFoundException($"Original record with ID {request.OriginalRecordId} not found.");
        }

        if (originalRecord.ItemType != request.ItemType || originalRecord.ItemId != request.ItemId)
        {
            throw new ArgumentException("Correction record ItemType and ItemId must match the original record.");
        }

        var manilaToday = GetManilaToday();
        if (request.ApprovalDate.Date > manilaToday)
        {
            throw new ArgumentException($"ApprovalDate ({request.ApprovalDate:yyyy-MM-dd}) cannot be in the future.");
        }

        string? attachmentPath = null;
        string? attachmentFileName = null;
        bool hasAttachment = false;

        if (request.AttachmentBytes != null && request.AttachmentBytes.Length > 0)
        {
            if (!CeoApprovalItemTypes.AttachmentAllowedTypes.Contains(request.ItemType))
            {
                throw new ArgumentException($"Attachments are permitted only on items 1, 6, and 8.");
            }

            var saved = await ValidateAndSaveAttachmentAsync(request.AttachmentBytes, request.AttachmentFileName ?? "attachment", ct);
            attachmentPath = saved.Path;
            attachmentFileName = saved.FileName;
            hasAttachment = true;
        }

        var correctionRecord = new CeoApprovalRecord
        {
            ItemType = request.ItemType,
            ItemId = request.ItemId,
            Status = request.Status,
            ApproverLabel = "CEO/President (in person)",
            EnteredBy = enteredByEmployeeId,
            EnteredAt = DateTime.UtcNow,
            ApprovalDate = request.ApprovalDate.Date,
            Notes = request.Notes?.Trim(),
            RejectionReason = request.Status == "Rejected" ? request.RejectionReason?.Trim() : null,
            AttachmentPath = attachmentPath,
            AttachmentFileName = attachmentFileName,
            HasAttachment = hasAttachment,
            IsCorrection = true,
            OriginalRecordId = originalRecord.RecordId,
            CorrectionReason = request.CorrectionReason.Trim(),
            IsActive = true
        };

        _context.CeoApprovalRecords.Add(correctionRecord);
        await _context.SaveChangesAsync(ct);

        return MapToDto(correctionRecord, new List<CeoApprovalConfirmation>(), false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Confirm Record (Head Accountant or Second Person)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<CeoApprovalConfirmationDto> ConfirmRecordAsync(
        int recordId, string kind, string confirmedByEmployeeId, string userRole, CancellationToken ct = default)
    {
        var record = await _context.CeoApprovalRecords
            .FirstOrDefaultAsync(r => r.RecordId == recordId, ct);

        if (record == null)
        {
            throw new KeyNotFoundException($"CEO approval record with ID {recordId} not found.");
        }

        if (string.Equals(confirmedByEmployeeId, record.EnteredBy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The confirmer must strictly differ from the person who entered the approval record (EnteredBy).");
        }

        if (kind.Equals("Recording", StringComparison.OrdinalIgnoreCase))
        {
            if (!CeoApprovalItemTypes.MakerCheckerConfirmationRequiredTypes.Contains(record.ItemType))
            {
                throw new InvalidOperationException($"Recording confirmation is applicable only for items 2 and 3 ({string.Join(", ", CeoApprovalItemTypes.MakerCheckerConfirmationRequiredTypes)}).");
            }

            if (!IsHeadAccountant(userRole))
            {
                throw new UnauthorizedAccessException("Recording confirmation for items 2 and 3 strictly requires the Head Accountant role.");
            }

            // Verify Head Accountant is not the requester of the transfer/carryover
            if (record.ItemType == CeoApprovalItemTypes.BudgetTransfer)
            {
                if (int.TryParse(record.ItemId, out int transferId))
                {
                    var transfer = await _context.BudgetTransfers.FirstOrDefaultAsync(t => t.TransferId == transferId, ct);
                    if (transfer != null && string.Equals(transfer.RequestedBy, confirmedByEmployeeId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("The Head Accountant confirming the transfer cannot be the employee who requested it.");
                    }
                }
            }
        }
        else if (kind.Equals("Correction", StringComparison.OrdinalIgnoreCase))
        {
            if (!record.IsCorrection)
            {
                throw new InvalidOperationException("Correction confirmation applies only to correction records.");
            }
        }
        else
        {
            throw new ArgumentException("Kind must be strictly 'Recording' or 'Correction'.");
        }

        var confirmation = new CeoApprovalConfirmation
        {
            RecordId = recordId,
            Kind = kind.Equals("Recording", StringComparison.OrdinalIgnoreCase) ? "Recording" : "Correction",
            ConfirmedBy = confirmedByEmployeeId,
            ConfirmedAt = DateTime.UtcNow
        };

        _context.CeoApprovalConfirmations.Add(confirmation);
        await _context.SaveChangesAsync(ct);

        return new CeoApprovalConfirmationDto
        {
            ConfirmationId = confirmation.ConfirmationId,
            RecordId = confirmation.RecordId,
            Kind = confirmation.Kind,
            ConfirmedBy = confirmation.ConfirmedBy,
            ConfirmedAt = confirmation.ConfirmedAt
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. Rejection Cleanup
    // ─────────────────────────────────────────────────────────────────────────
    private async Task HandleRejectionCleanupAsync(string itemType, string itemId, string reason, CancellationToken ct)
    {
        if (itemType == CeoApprovalItemTypes.CashOutflow)
        {
            var advance = await _context.CashAdvances.FirstOrDefaultAsync(a => a.AdvanceId == itemId, ct);
            if (advance != null)
            {
                advance.Status = "Rejected";
                advance.RejectionReason = reason;

                // Release budget committed amount
                var line = await _context.MonthlyBudgetLines.FirstOrDefaultAsync(l =>
                    l.FiscalYear == advance.FiscalYear &&
                    l.DepartmentCode == advance.DepartmentCode &&
                    l.Category == advance.Category &&
                    l.Month == advance.Month, ct);

                if (line != null && line.CommittedAmount >= advance.AmountGiven)
                {
                    line.CommittedAmount -= advance.AmountGiven;
                    line.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync(ct);
            }
        }
        else if (itemType == CeoApprovalItemTypes.Reimbursement)
        {
            int.TryParse(itemId, out int rId);
            var reimbursement = await _context.Reimbursements.FirstOrDefaultAsync(r => r.ReimbursementId == rId, ct);
            if (reimbursement != null)
            {
                reimbursement.Status = "Rejected";
                reimbursement.RejectionReason = reason;
                await _context.SaveChangesAsync(ct);
            }
        }
        else if (itemType == CeoApprovalItemTypes.BudgetOverride)
        {
            if (int.TryParse(itemId, out int overrideId))
            {
                var budgetOverride = await _context.BudgetOverrides.FirstOrDefaultAsync(o => o.OverrideId == overrideId, ct);
                if (budgetOverride != null)
                {
                    budgetOverride.Status = "Rejected";
                    await _context.SaveChangesAsync(ct);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Attachment Validation & Storage
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<(string Path, string FileName)> ValidateAndSaveAttachmentAsync(byte[] bytes, string originalFileName, CancellationToken ct)
    {
        if (bytes == null || bytes.Length <= 0)
        {
            throw new ArgumentException("Uploaded file cannot be empty.");
        }

        const long maxBytes = 10 * 1024 * 1024; // 10 MB
        if (bytes.Length > maxBytes)
        {
            throw new ArgumentException($"File size ({bytes.Length / (1024.0 * 1024.0):N2} MB) exceeds the strict 10 MB limit.");
        }

        string ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        bool validMagicBytes = false;

        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
        {
            validMagicBytes = true; // PDF %PDF
            ext = ".pdf";
        }
        else if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            validMagicBytes = true; // JPEG
            ext = ".jpg";
        }
        else if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                 bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            validMagicBytes = true; // PNG
            ext = ".png";
        }

        if (!validMagicBytes)
        {
            throw new ArgumentException("Invalid file content. Real file format must be genuine PDF, JPG, or PNG.");
        }

        string safeFileName = $"{Guid.NewGuid():N}{ext}";
        string fullPath = Path.Combine(_attachmentStoragePath, safeFileName);

        await File.WriteAllBytesAsync(fullPath, bytes, ct);
        return (fullPath, originalFileName);
    }

    public async Task<(byte[] Bytes, string ContentType, string FileName)> DownloadAttachmentAsync(
        int recordId, string employeeId, string userRole, CancellationToken ct = default)
    {
        if (!IsFinanceManager(userRole) && !IsHeadAccountant(userRole))
        {
            throw new UnauthorizedAccessException("Attachments can be downloaded strictly by the Finance Manager and Head Accountant.");
        }

        var record = await _context.CeoApprovalRecords.FirstOrDefaultAsync(r => r.RecordId == recordId, ct);
        if (record == null || string.IsNullOrWhiteSpace(record.AttachmentPath) || !File.Exists(record.AttachmentPath))
        {
            throw new FileNotFoundException($"Attachment for approval record {recordId} not found.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(record.AttachmentPath, ct);
        string ext = Path.GetExtension(record.AttachmentPath).ToLowerInvariant();
        string contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };

        return (bytes, contentType, record.AttachmentFileName ?? Path.GetFileName(record.AttachmentPath));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. Query Records & Excel Export
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<List<CeoApprovalRecordDto>> GetRecordsAsync(
        string? itemType = null, string? status = null, int? fiscalYear = null, CancellationToken ct = default)
    {
        var query = _context.CeoApprovalRecords.AsQueryable();

        if (!string.IsNullOrWhiteSpace(itemType))
            query = query.Where(r => r.ItemType == itemType);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        if (fiscalYear.HasValue)
            query = query.Where(r => r.ApprovalDate.Year == fiscalYear.Value);

        var records = await query.OrderByDescending(r => r.EnteredAt).ToListAsync(ct);
        var recordIds = records.Select(r => r.RecordId).ToList();

        var confirmations = await _context.CeoApprovalConfirmations
            .Where(c => recordIds.Contains(c.RecordId))
            .ToListAsync(ct);

        var dtoList = new List<CeoApprovalRecordDto>();
        foreach (var r in records)
        {
            var confs = confirmations.Where(c => c.RecordId == r.RecordId).ToList();
            bool effective = r.Status == "Approved" &&
                (!r.IsCorrection || confs.Any(c => c.Kind == "Correction")) &&
                (!CeoApprovalItemTypes.MakerCheckerConfirmationRequiredTypes.Contains(r.ItemType) || confs.Any(c => c.Kind == "Recording"));

            dtoList.Add(MapToDto(r, confs, effective));
        }

        return dtoList;
    }

    public async Task<CeoApprovalRecordDto?> GetRecordByIdAsync(int recordId, CancellationToken ct = default)
    {
        var record = await _context.CeoApprovalRecords.FirstOrDefaultAsync(r => r.RecordId == recordId, ct);
        if (record == null) return null;

        var confirmations = await _context.CeoApprovalConfirmations
            .Where(c => c.RecordId == recordId)
            .ToListAsync(ct);

        bool effective = record.Status == "Approved" &&
            (!record.IsCorrection || confirmations.Any(c => c.Kind == "Correction")) &&
            (!CeoApprovalItemTypes.MakerCheckerConfirmationRequiredTypes.Contains(record.ItemType) || confirmations.Any(c => c.Kind == "Recording"));

        return MapToDto(record, confirmations, effective);
    }

    public async Task<List<CeoApprovalThresholdChangeLogDto>> GetThresholdHistoryAsync(CancellationToken ct = default)
    {
        return await _context.CeoApprovalThresholdChangeLogs
            .OrderByDescending(l => l.ChangedAt)
            .Select(l => new CeoApprovalThresholdChangeLogDto
            {
                LogId = l.LogId,
                SettingId = l.SettingId,
                OldAmount = l.OldAmount,
                NewAmount = l.NewAmount,
                ChangedBy = l.ChangedBy,
                ChangedAt = l.ChangedAt,
                Reason = l.Reason
            })
            .ToListAsync(ct);
    }

    public async Task<byte[]> ExportRecordsToExcelAsync(string? itemType = null, string? status = null, CancellationToken ct = default)
    {
        var records = await GetRecordsAsync(itemType, status, null, ct);
        var sb = new StringBuilder();

        // CSV formatted for Excel
        sb.AppendLine("Record ID,Item Type,Item ID,Status,Approver Label,Entered By,Entered At (UTC),Approval Date,Attachment Flag,File Name,Is Correction,Original Record ID,Effective");

        foreach (var r in records)
        {
            string attachmentFlag = r.HasAttachment ? "Attached" : (CeoApprovalItemTypes.AttachmentAllowedTypes.Contains(r.ItemType) ? "No attachment" : "N/A");
            sb.AppendLine($"{r.RecordId},\"{r.ItemType}\",\"{r.ItemId}\",\"{r.Status}\",\"{r.ApproverLabel}\",\"{r.EnteredBy}\",\"{r.EnteredAt:yyyy-MM-dd HH:mm:ss}\",\"{r.ApprovalDate:yyyy-MM-dd}\",\"{attachmentFlag}\",\"{r.AttachmentFileName ?? ""}\",{r.IsCorrection},\"{r.OriginalRecordId}\",{r.IsEffective}");
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static CeoApprovalRecordDto MapToDto(CeoApprovalRecord r, List<CeoApprovalConfirmation> confirmations, bool isEffective)
    {
        return new CeoApprovalRecordDto
        {
            RecordId = r.RecordId,
            ItemType = r.ItemType,
            ItemId = r.ItemId,
            Status = r.Status,
            ApproverLabel = r.ApproverLabel,
            EnteredBy = r.EnteredBy,
            EnteredAt = r.EnteredAt,
            ApprovalDate = r.ApprovalDate,
            Notes = r.Notes,
            RejectionReason = r.RejectionReason,
            HasAttachment = r.HasAttachment,
            AttachmentFileName = r.AttachmentFileName,
            IsCorrection = r.IsCorrection,
            OriginalRecordId = r.OriginalRecordId,
            CorrectionReason = r.CorrectionReason,
            IsActive = r.IsActive,
            IsEffective = isEffective,
            Confirmations = confirmations.Select(c => new CeoApprovalConfirmationDto
            {
                ConfirmationId = c.ConfirmationId,
                RecordId = c.RecordId,
                Kind = c.Kind,
                ConfirmedBy = c.ConfirmedBy,
                ConfirmedAt = c.ConfirmedAt
            }).ToList()
        };
    }
}
