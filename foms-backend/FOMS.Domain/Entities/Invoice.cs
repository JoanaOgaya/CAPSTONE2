using System;
using System.ComponentModel.DataAnnotations;
using FOMS.Domain.Constants;

namespace FOMS.Domain.Entities;

public static class InvoiceDocumentStatus
{
    public const string Draft = "Draft";
    public const string ForReview = "For Review";
    public const string Approved = "Approved";
    public const string Sent = "Sent";
    public const string Received = "Received";
    public const string Disputed = "Disputed";
    public const string Void = "Void";

    // Legacy statuses for backward compatibility during transition
    [Obsolete("Use ForReview instead")]
    public const string PendingHaVerification = "Pending HA Verification";
    [Obsolete("Use ForReview instead")]
    public const string PendingFmApproval = "Pending FM Approval";
    [Obsolete("Use Draft instead")]
    public const string ReturnedToCreator = "Returned to Creator";
    [Obsolete("Use ForReview instead")]
    public const string ReturnedToHa = "Returned to HA";

    public static readonly string[] AllCanonical = new[]
    {
        Draft,
        ForReview,
        Approved,
        Sent,
        Received,
        Disputed,
        Void
    };

    public static string Normalize(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return Draft;
        var trimmed = status.Trim();
        if (trimmed.Equals(PendingHaVerification, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(PendingFmApproval, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(ReturnedToHa, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Pending Approval", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            return ForReview;
        }
        if (trimmed.Equals(ReturnedToCreator, StringComparison.OrdinalIgnoreCase))
        {
            return Draft;
        }
        return trimmed;
    }
}

public static class InvoicePaymentStatus
{
    public const string Unpaid = "Unpaid";
    public const string PartiallyPaid = "Partially Paid";
    public const string Paid = "Paid";

    public static readonly string[] AllCanonical = new[]
    {
        Unpaid,
        PartiallyPaid,
        Paid
    };
}

public static class InvoiceRole
{
    public const string FinanceManager = FomsRoles.FinanceManager;
    public const string HeadAccountant = FomsRoles.HeadAccountant;
    public const string Staff = FomsRoles.Accountant;
    public const string AssistantFinanceManager = FomsRoles.AssistantFinanceManager;

    public static bool IsFinanceManager(string? role, string? empId) =>
        !string.IsNullOrWhiteSpace(role)
            ? FomsRoles.IsFinanceManager(role)
            : string.Equals(empId, FomsRoles.EmpFinanceManager, StringComparison.OrdinalIgnoreCase);

    public static bool IsHeadAccountant(string? role, string? empId) =>
        !string.IsNullOrWhiteSpace(role)
            ? FomsRoles.IsHeadAccountant(role)
            : string.Equals(empId, FomsRoles.EmpHeadAccountant, StringComparison.OrdinalIgnoreCase);

    public static bool IsStaff(string? role, string? empId) =>
        !string.IsNullOrWhiteSpace(role)
            ? FomsRoles.IsAccountant(role)
            : string.Equals(empId, FomsRoles.EmpAccountant, StringComparison.OrdinalIgnoreCase);

    public static bool IsAssistantFinanceManager(string? role, string? empId) =>
        string.Equals(empId, FomsRoles.EmpAssistantFinanceManager, StringComparison.OrdinalIgnoreCase) ||
        FomsRoles.IsAssistantFinanceManager(role);
}

public class Invoice
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InvoiceNo { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public Client? Client { get; set; }
    public string? ClientName { get; set; }
    public string? BillingDate { get; set; }
    public string? DueDate { get; set; }
    public decimal FreightCharges { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatRate { get; set; } = 0.12m;
    public decimal VatAmount { get; set; }
    public decimal Surcharge { get; set; }
    public decimal FuelSurchargeRate { get; set; } = 0.15m;
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal CreditMemoAmount { get; set; }
    public decimal CreditApplied { get; set; }
    public decimal Balance { get; set; }
    public string PaymentStatus { get; set; } = InvoicePaymentStatus.Unpaid;
    public string? PaymentValidationStatus { get; set; }
    public string? AgingBucket { get; set; } = "Current";
    public int DaysOverdue { get; set; }
    public string? Description { get; set; }
    public string? EncodedBy { get; set; }
    public string? DateEncoded { get; set; }
    public string? LastUpdated { get; set; }
    public string? UpdatedBy { get; set; }
    public bool Archived { get; set; } = false;
    public bool HasCtc { get; set; } = false;
    public string? CtcWaybills { get; set; }
    public string? WaybillIds { get; set; }
    public decimal DiscountAmount { get; set; } = 0m;
    public decimal DiscountRate { get; set; } = 0m;
    public string? DiscountApprovedBy { get; set; }
    public string? DiscountReason { get; set; }
    public bool IsSuspect { get; set; } = false;
    public string? SuspectReason { get; set; }
    public string? VoidReason { get; set; }
    public string DocumentStatus { get; set; } = InvoiceDocumentStatus.Draft;
    public DateTime? SentDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public string? ReviewRejectionReason { get; set; }
    public bool IsSampledForReview { get; set; } = false;
    public string? SampledBy { get; set; }
    public DateTime? SampledAt { get; set; }
    public string? DisputeReasonCode { get; set; }
    public string? DisputeRemarks { get; set; }
    public bool IsDisputed { get; set; } = false;
    public bool AccrualRevenueRecognized { get; set; } = false;
    public string? AccrualJournalEntryId { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    private void EnsureNotAssistantFm(string? actorRole, string? actorId)
    {
        if (InvoiceRole.IsAssistantFinanceManager(actorRole, actorId))
        {
            throw new UnauthorizedAccessException("Assistant Finance Manager (EMP-004) is restricted to liquidations and cannot transition invoices.");
        }
    }

    private void EnsureNotSelf(string? actorId, string actionName)
    {
        if (!string.IsNullOrWhiteSpace(actorId) && !string.IsNullOrWhiteSpace(EncodedBy) &&
            string.Equals(actorId.Trim(), EncodedBy.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Separation of duties violation: Creator ({actorId}) cannot {actionName} their own invoice.");
        }
    }

    public void Submit(string actorId, string actorRole)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsStaff(actorRole, actorId) && !InvoiceRole.IsFinanceManager(actorRole, actorId) && !InvoiceRole.IsHeadAccountant(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Staff (EMP-003), Head Accountant (EMP-002), or Finance Manager (EMP-001) can submit an invoice for verification.");

        var normStatus = InvoiceDocumentStatus.Normalize(DocumentStatus);
        if (normStatus != InvoiceDocumentStatus.Draft)
            throw new InvalidOperationException($"Cannot submit invoice in status '{DocumentStatus}'. Only Draft invoices can be submitted.");

        DocumentStatus = InvoiceDocumentStatus.ForReview;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Resubmit(string actorId, string actorRole)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        var normStatus = InvoiceDocumentStatus.Normalize(DocumentStatus);
        if (normStatus == InvoiceDocumentStatus.Draft)
        {
            if (!InvoiceRole.IsStaff(actorRole, actorId) && !InvoiceRole.IsFinanceManager(actorRole, actorId) && !InvoiceRole.IsHeadAccountant(actorRole, actorId))
                throw new UnauthorizedAccessException("Only Staff (EMP-003), Head Accountant (EMP-002), or Finance Manager (EMP-001) can resubmit a returned invoice.");
            DocumentStatus = InvoiceDocumentStatus.ForReview;
        }
        else if (normStatus == InvoiceDocumentStatus.ForReview)
        {
            DocumentStatus = InvoiceDocumentStatus.ForReview;
        }
        else
        {
            throw new InvalidOperationException($"Cannot resubmit invoice in status '{DocumentStatus}'. Only invoices in 'Draft' (or returned) can be resubmitted.");
        }
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Verify(string actorId, string actorRole)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        bool isHa = InvoiceRole.IsHeadAccountant(actorRole, actorId);
        bool isFm = InvoiceRole.IsFinanceManager(actorRole, actorId);

        if (!isHa && !isFm)
            throw new UnauthorizedAccessException("Only Head Accountant (EMP-002) or Finance Manager (EMP-001) can verify an invoice.");

        // Option 1 Approved: If Head Accountant created the invoice, HA cannot verify own invoice (SoD violation).
        // Finance Manager verifies and approves in this edge case to prevent deadlock.
        EnsureNotSelf(actorId, "verify");

        var normStatus = InvoiceDocumentStatus.Normalize(DocumentStatus);
        if (normStatus != InvoiceDocumentStatus.ForReview)
            throw new InvalidOperationException($"Cannot verify invoice in status '{DocumentStatus}'. Invoice must be in 'For Review'.");

        DocumentStatus = InvoiceDocumentStatus.Approved;
        VerifiedBy = actorId;
        VerifiedAt = DateTime.UtcNow;
        ApprovedBy = actorId;
        ApprovedAt = DateTime.UtcNow;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void ReturnToCreator(string actorId, string actorRole, string reason)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsHeadAccountant(actorRole, actorId) && !InvoiceRole.IsFinanceManager(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Head Accountant (EMP-002) or Finance Manager (EMP-001) can return an invoice to creator.");

        EnsureNotSelf(actorId, "return to creator");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A return reason is required and mandatory when returning an invoice to creator.", nameof(reason));

        var normStatus = InvoiceDocumentStatus.Normalize(DocumentStatus);
        if (normStatus != InvoiceDocumentStatus.ForReview)
            throw new InvalidOperationException($"Cannot return invoice in status '{DocumentStatus}' to creator. Invoice must be in 'For Review'.");

        DocumentStatus = InvoiceDocumentStatus.Draft;
        ReviewRejectionReason = reason;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void ReturnToHeadAccountant(string actorId, string actorRole, string reason)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsFinanceManager(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Finance Manager (EMP-001) can return an invoice to the Head Accountant.");

        EnsureNotSelf(actorId, "return to Head Accountant");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A return reason is mandatory when returning an invoice to Head Accountant.", nameof(reason));

        DocumentStatus = InvoiceDocumentStatus.ForReview;
        ReviewRejectionReason = reason;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Approve(string actorId, string actorRole)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        bool isFm = InvoiceRole.IsFinanceManager(actorRole, actorId);
        bool isHa = InvoiceRole.IsHeadAccountant(actorRole, actorId);

        if (!isFm && !isHa)
            throw new UnauthorizedAccessException("Only Finance Manager (EMP-001) or Head Accountant (EMP-002) can approve an invoice.");

        EnsureNotSelf(actorId, "approve");

        var normStatus = InvoiceDocumentStatus.Normalize(DocumentStatus);
        if (normStatus != InvoiceDocumentStatus.ForReview)
            throw new InvalidOperationException($"Cannot approve invoice in status '{DocumentStatus}'. Invoice must be in 'For Review'.");

        DocumentStatus = InvoiceDocumentStatus.Approved;
        ApprovedBy = actorId;
        ApprovedAt = DateTime.UtcNow;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Send(string actorId, string actorRole)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsStaff(actorRole, actorId) && !InvoiceRole.IsFinanceManager(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Staff (EMP-003) can dispatch/send an invoice.");

        if (DocumentStatus != InvoiceDocumentStatus.Approved)
            throw new InvalidOperationException($"Cannot send invoice in status '{DocumentStatus}'. Invoice must be in 'Approved' status.");

        DocumentStatus = InvoiceDocumentStatus.Sent;
        SentDate = DateTime.UtcNow;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void MarkReceived(string actorId, string actorRole, DateTime? receivedDate = null)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsStaff(actorRole, actorId) && !InvoiceRole.IsFinanceManager(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Staff (EMP-003) can record invoice receipt.");

        if (DocumentStatus != InvoiceDocumentStatus.Sent)
            throw new InvalidOperationException($"Cannot mark invoice received from status '{DocumentStatus}'. Invoice must be in 'Sent' status.");

        DocumentStatus = InvoiceDocumentStatus.Received;
        ReceivedDate = receivedDate ?? DateTime.UtcNow;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Dispute(string actorId, string actorRole, string reasonCode, string remarks)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsHeadAccountant(actorRole, actorId) && !InvoiceRole.IsFinanceManager(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Head Accountant (EMP-002) or Finance Manager (EMP-001) can record a dispute.");

        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("A dispute reason code is required.", nameof(reasonCode));

        if (DocumentStatus != InvoiceDocumentStatus.Sent && DocumentStatus != InvoiceDocumentStatus.Received)
            throw new InvalidOperationException($"Cannot dispute invoice in status '{DocumentStatus}'. Only Sent or Received invoices can be disputed.");

        DocumentStatus = InvoiceDocumentStatus.Disputed;
        IsDisputed = true;
        DisputeReasonCode = reasonCode;
        DisputeRemarks = remarks;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void ResolveDispute(string actorId, string actorRole, string resolutionRemarks)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsHeadAccountant(actorRole, actorId) && !InvoiceRole.IsFinanceManager(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Head Accountant (EMP-002) or Finance Manager (EMP-001) can resolve a dispute.");

        if (string.IsNullOrWhiteSpace(resolutionRemarks))
            throw new ArgumentException("Resolution remarks are required to resolve a dispute.", nameof(resolutionRemarks));

        if (DocumentStatus != InvoiceDocumentStatus.Disputed)
            throw new InvalidOperationException($"Cannot resolve dispute on invoice in status '{DocumentStatus}'.");

        DocumentStatus = InvoiceDocumentStatus.Received;
        ReceivedDate ??= DateTime.UtcNow;
        IsDisputed = false;
        DisputeRemarks = $"[RESOLVED: {resolutionRemarks}] " + DisputeRemarks;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Void(string actorId, string actorRole, string reason)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A void reason is mandatory. A void reason is required.", nameof(reason));

        if (DocumentStatus == InvoiceDocumentStatus.Sent || DocumentStatus == InvoiceDocumentStatus.Received)
            throw new InvalidOperationException("Cannot void an invoice that has been Sent or Received. Post-dispatch adjustments must use a Credit Memo.");

        if (AmountPaid > 0m || PaymentStatus != InvoicePaymentStatus.Unpaid)
            throw new InvalidOperationException("Cannot void an invoice with payments applied. Adjustments must use a Credit Memo.");

        if (DocumentStatus == InvoiceDocumentStatus.Disputed)
            throw new InvalidOperationException("Cannot void an invoice under Dispute.");

        if (DocumentStatus == InvoiceDocumentStatus.Void)
            throw new InvalidOperationException("Invoice is already Void.");

        var normStatus = InvoiceDocumentStatus.Normalize(DocumentStatus);
        if (normStatus == InvoiceDocumentStatus.Draft ||
            normStatus == InvoiceDocumentStatus.ForReview ||
            normStatus == InvoiceDocumentStatus.Approved)
        {
            bool isHa = InvoiceRole.IsHeadAccountant(actorRole, actorId);
            bool isFm = InvoiceRole.IsFinanceManager(actorRole, actorId);

            if (!isHa && !isFm)
                throw new UnauthorizedAccessException("Only Head Accountant (EMP-002) or Finance Manager (EMP-001) can void pre-dispatch invoices.");

            // Creator cannot void own invoice. If HA created, FM can void.
            EnsureNotSelf(actorId, "void");
        }
        else
        {
            throw new InvalidOperationException($"Cannot void invoice in status '{DocumentStatus}'.");
        }

        DocumentStatus = InvoiceDocumentStatus.Void;
        VoidReason = reason;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }
}
