using System;
using System.ComponentModel.DataAnnotations;
using FOMS.Domain.Constants;

namespace FOMS.Domain.Entities;

public static class InvoiceDocumentStatus
{
    public const string Draft = "Draft";
    public const string PendingHaVerification = "Pending HA Verification";
    public const string PendingFmApproval = "Pending FM Approval";
    public const string ReturnedToCreator = "Returned to Creator";
    public const string ReturnedToHa = "Returned to HA";
    public const string Approved = "Approved";
    public const string Sent = "Sent";
    public const string Received = "Received";
    public const string Disputed = "Disputed";
    public const string Void = "Void";

    public static readonly string[] AllCanonical = new[]
    {
        Draft,
        PendingHaVerification,
        PendingFmApproval,
        ReturnedToCreator,
        ReturnedToHa,
        Approved,
        Sent,
        Received,
        Disputed,
        Void
    };
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
        if (!InvoiceRole.IsStaff(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Staff (EMP-003) can submit an invoice for verification.");

        if (DocumentStatus != InvoiceDocumentStatus.Draft)
            throw new InvalidOperationException($"Cannot submit invoice in status '{DocumentStatus}'. Only Draft invoices can be submitted.");

        DocumentStatus = InvoiceDocumentStatus.PendingHaVerification;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Resubmit(string actorId, string actorRole)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (DocumentStatus == InvoiceDocumentStatus.ReturnedToHa)
        {
            if (!InvoiceRole.IsHeadAccountant(actorRole, actorId))
                throw new UnauthorizedAccessException("Only Head Accountant (EMP-002) can resubmit an invoice returned to HA.");
            DocumentStatus = InvoiceDocumentStatus.PendingFmApproval;
        }
        else if (DocumentStatus == InvoiceDocumentStatus.ReturnedToCreator)
        {
            if (!InvoiceRole.IsStaff(actorRole, actorId))
                throw new UnauthorizedAccessException("Only Staff (EMP-003) can resubmit a returned invoice.");
            DocumentStatus = InvoiceDocumentStatus.PendingHaVerification;
        }
        else
        {
            throw new InvalidOperationException($"Cannot resubmit invoice in status '{DocumentStatus}'. Only invoices in 'Returned to Creator' or 'Returned to HA' can be resubmitted.");
        }
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Verify(string actorId, string actorRole)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsHeadAccountant(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Head Accountant (EMP-002) can verify an invoice.");

        EnsureNotSelf(actorId, "verify");

        if (DocumentStatus != InvoiceDocumentStatus.PendingHaVerification && DocumentStatus != InvoiceDocumentStatus.ReturnedToHa)
            throw new InvalidOperationException($"Cannot verify invoice in status '{DocumentStatus}'. Invoice must be in 'Pending HA Verification' or 'Returned to HA'.");

        DocumentStatus = InvoiceDocumentStatus.PendingFmApproval;
        VerifiedBy = actorId;
        VerifiedAt = DateTime.UtcNow;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void ReturnToCreator(string actorId, string actorRole, string reason)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsHeadAccountant(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Head Accountant (EMP-002) can return an invoice to creator.");

        EnsureNotSelf(actorId, "return to creator");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A return reason is required and mandatory when returning an invoice to creator.", nameof(reason));

        if (DocumentStatus != InvoiceDocumentStatus.PendingHaVerification && DocumentStatus != InvoiceDocumentStatus.ReturnedToHa)
            throw new InvalidOperationException($"Cannot return invoice in status '{DocumentStatus}' to creator.");

        DocumentStatus = InvoiceDocumentStatus.ReturnedToCreator;
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

        if (DocumentStatus != InvoiceDocumentStatus.PendingFmApproval)
            throw new InvalidOperationException($"Cannot return invoice in status '{DocumentStatus}' to Head Accountant. Invoice must be in 'Pending FM Approval'.");

        DocumentStatus = InvoiceDocumentStatus.ReturnedToHa;
        ReviewRejectionReason = reason;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Approve(string actorId, string actorRole)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsFinanceManager(actorRole, actorId))
            throw new UnauthorizedAccessException("Only Finance Manager (EMP-001) can approve an invoice.");

        EnsureNotSelf(actorId, "approve");

        if (DocumentStatus != InvoiceDocumentStatus.PendingFmApproval)
            throw new InvalidOperationException($"Cannot approve invoice in status '{DocumentStatus}'. Invoice must be in 'Pending FM Approval'.");

        DocumentStatus = InvoiceDocumentStatus.Approved;
        ApprovedBy = actorId;
        ApprovedAt = DateTime.UtcNow;
        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        UpdatedBy = actorId;
    }

    public void Send(string actorId, string actorRole)
    {
        EnsureNotAssistantFm(actorRole, actorId);
        if (!InvoiceRole.IsStaff(actorRole, actorId))
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
        if (!InvoiceRole.IsStaff(actorRole, actorId))
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

        if (DocumentStatus == InvoiceDocumentStatus.Approved)
        {
            if (!InvoiceRole.IsFinanceManager(actorRole, actorId))
                throw new UnauthorizedAccessException("Only Finance Manager (EMP-001) can void an Approved invoice.");

            EnsureNotSelf(actorId, "void");
        }
        else if (DocumentStatus == InvoiceDocumentStatus.Draft ||
                 DocumentStatus == InvoiceDocumentStatus.PendingHaVerification ||
                 DocumentStatus == InvoiceDocumentStatus.PendingFmApproval ||
                 DocumentStatus == InvoiceDocumentStatus.ReturnedToCreator ||
                 DocumentStatus == InvoiceDocumentStatus.ReturnedToHa)
        {
            if (!InvoiceRole.IsHeadAccountant(actorRole, actorId))
                throw new UnauthorizedAccessException("Only Head Accountant (EMP-002) can void pre-approval invoices.");

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
