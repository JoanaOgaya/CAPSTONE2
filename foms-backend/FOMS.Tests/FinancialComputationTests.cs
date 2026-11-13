using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FOMS.Application.Features;
using FOMS.Application.Services;
using FOMS.Domain.Entities;
using FOMS.Infrastructure.Persistence;
using Xunit;

namespace FOMS.Tests;

public class FinancialComputationTests
{
    private async Task<ApplicationDbContext> GetDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await ApplicationDbContextSeed.SeedSampleDataAsync(context);
        return context;
    }

    [Fact]
    public void VAT_ShouldBeCalculatedAsTwelvePercentOfSubtotal()
    {
        // Arrange
        decimal subtotal = 10000.00m;
        decimal expectedVat = 1200.00m;

        // Act
        decimal actualVat = BillingComputationService.ComputeVat(subtotal);

        // Assert
        Assert.Equal(expectedVat, actualVat);
    }

    [Fact]
    public void InvoiceFormula_ShouldApplyVatToSubtotalAndFuelSurchargeToFreight()
    {
        var invoice = new Invoice
        {
            FreightCharges = 600m,
            OtherCharges = 10m,
            FuelSurchargeRate = 0.15m,
            DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd")
        };

        BillingComputationService.RecalculateInvoice(invoice);

        Assert.Equal(610m, invoice.Subtotal);
        Assert.Equal(73.20m, invoice.VatAmount);
        Assert.Equal(90m, invoice.Surcharge);
        Assert.Equal(773.20m, invoice.TotalAmount);
    }

    [Fact]
    public void OutstandingBalance_ShouldBeTotalMinusPaidAndNeverNegative()
    {
        // Act & Assert
        Assert.Equal(6000.00m, BillingComputationService.ComputeOutstandingBalance(10000.00m, 4000.00m));
        Assert.Equal(0.00m, BillingComputationService.ComputeOutstandingBalance(10000.00m, 10000.00m));
        Assert.Equal(0.00m, BillingComputationService.ComputeOutstandingBalance(10000.00m, 12000.00m)); // Overpayment balance is capped at 0
    }

    [Fact]
    public void InvalidAndMismatchedPayments_ShouldNotBeApplied()
    {
        // Act & Assert
        Assert.NotNull(BillingComputationService.ValidatePaymentAmount(11000.00m, 10000.00m)); // Overpayment
        Assert.NotNull(BillingComputationService.ValidatePaymentAmount(0m, 10000.00m));       // Zero payment rejection
        Assert.NotNull(BillingComputationService.ValidatePaymentAmount(-500m, 10000.00m));     // Negative payment rejection
        Assert.NotNull(BillingComputationService.ValidatePaymentAmount(4000.00m, 10000.00m));  // Partial payment
        Assert.Null(BillingComputationService.ValidatePaymentAmount(10000.00m, 10000.00m));   // Valid full payment
    }

    [Fact]
    public void AgingBuckets_ShouldMapCorrectlyBasedOnDueDays()
    {
        // Arrange
        var today = DateTime.UtcNow.Date;
        
        var currentDueDate = today.AddDays(5).ToString("yyyy-MM-dd");
        var overdue10Days = today.AddDays(-10).ToString("yyyy-MM-dd");
        var overdue45Days = today.AddDays(-45).ToString("yyyy-MM-dd");
        var overdue75Days = today.AddDays(-75).ToString("yyyy-MM-dd");
        var overdue100Days = today.AddDays(-100).ToString("yyyy-MM-dd");

        // Act & Assert
        Assert.Equal("Current", BillingComputationService.ComputeAgingBucket(currentDueDate));
        Assert.Equal("1-30", BillingComputationService.ComputeAgingBucket(overdue10Days));
        Assert.Equal("31-60", BillingComputationService.ComputeAgingBucket(overdue45Days));
        Assert.Equal("61-90", BillingComputationService.ComputeAgingBucket(overdue75Days));
        Assert.Equal("90+", BillingComputationService.ComputeAgingBucket(overdue100Days));
    }

    [Fact]
    public void PaymentStatus_ShouldUpdateCorrectlyBasedOnBalancePaidAndDueDate()
    {
        // Arrange
        var today = DateTime.UtcNow.Date;
        var futureDue = today.AddDays(10).ToString("yyyy-MM-dd");
        var pastDue = today.AddDays(-10).ToString("yyyy-MM-dd");

        // Act & Assert
        Assert.Equal("Paid", BillingComputationService.ComputePaymentStatus(0m, 10000.00m, futureDue));
        Assert.Equal("Unpaid", BillingComputationService.ComputePaymentStatus(10000.00m, 0m, futureDue));
        Assert.Equal("Unpaid", BillingComputationService.ComputePaymentStatus(6000.00m, 4000.00m, futureDue));
        Assert.Equal("Unpaid", BillingComputationService.ComputePaymentStatus(10000.00m, 0m, pastDue));
        Assert.Equal("Unpaid", BillingComputationService.ComputePaymentStatus(6000.00m, 4000.00m, pastDue));
    }

    [Fact]
    public async Task RecordPaymentCommandHandler_ShouldQueuePaymentMismatchesAndHardenGuards()
    {
        // Arrange
        using var context = await GetDbContextAsync();
        var handler = new PaymentFeatures.RecordPaymentCommandHandler(context);

        // Retrieve seeded invoice LZD-2026-0001
        var invoice = await context.Invoices.FirstAsync(i => i.InvoiceNo == "LZD-2026-0001");
        // Normalize due date to the future to avoid overdue surcharge interfering with assertions
        invoice.DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        BillingComputationService.RecalculateInvoice(invoice);
        await context.SaveChangesAsync();

        decimal startingBalance = invoice.Balance; // Should be 53,200.00
        decimal paymentAmount = 20000.00m;

        // Act - Record a payment below the exact invoice amount
        var command = new PaymentFeatures.RecordPaymentCommand(
            OrNumber: "OR-SHORT-TEST",
            InvoiceId: invoice.Id,
            InvoiceNo: invoice.InvoiceNo,
            ClientId: invoice.ClientId,
            ClientName: invoice.ClientName,
            PaymentDate: DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Amount: paymentAmount,
            PaymentMethod: "Check",
            ReferenceNumber: "GC-SHORT-123",
            ProofImageUrl: "http://example.com/proof.jpg",
            Remarks: "Test short payment exception",
            RecordedBy: "Test User"
        );
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(paymentAmount, result.Amount);
        Assert.Equal("Exception", result.PaymentStatus);
        Assert.Contains("Short payment", result.RejectionReason);
        var updatedInvoice = await context.Invoices.FindAsync(invoice.Id);
        Assert.NotNull(updatedInvoice);
        // Per business rules: Payment is pending validation, invoice amount paid updates only upon Finance validation
        Assert.Equal(0m, updatedInvoice!.AmountPaid);
        Assert.Equal(startingBalance, updatedInvoice.Balance);

        // Overpayments are retained in the exception queue for Finance Manager resolution.
        var overpayCommand = command with
        {
            OrNumber = "OR-OVERPAY",
            Amount = startingBalance + 1.00m,
            ReferenceNumber = "GC-OVERPAY-124"
        };
        var overpayment = await handler.Handle(overpayCommand, CancellationToken.None);
        Assert.Equal("Exception", overpayment.PaymentStatus);
        Assert.Contains("Overpayment", overpayment.RejectionReason);

        // Assert negative payment rejection
        var negativeCommand = command with { OrNumber = "OR-NEGATIVE", Amount = -100m };
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(negativeCommand, CancellationToken.None));

        // Assert zero payment rejection
        var zeroCommand = command with { OrNumber = "OR-ZERO", Amount = 0m };
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(zeroCommand, CancellationToken.None));
    }

    [Fact]
    public void RecalculateInvoice_CreditMemoReducesReceivableWithoutRepeatedDeduction()
    {
        var invoice = new Invoice
        {
            FreightCharges = 1000m,
            DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
            CreditMemoAmount = 100m
        };

        BillingComputationService.RecalculateInvoice(invoice);
        var firstTotal = invoice.TotalAmount;
        BillingComputationService.RecalculateInvoice(invoice);

        Assert.Equal(1170m, firstTotal);
        Assert.Equal(firstTotal, invoice.TotalAmount);
        Assert.Equal(firstTotal, invoice.Balance);
    }

    [Fact]
    public async Task PaidInvoiceCreditMemoBecomesClientCreditAppliedToNextInvoice()
    {
        using var context = await GetDbContextAsync();
        var client = new Client
        {
            Id = "CA-CREDIT-TEST",
            ClientCode = "CA-CREDIT-TEST",
            Name = "Credit Test Client",
            BusinessName = "Credit Test Client"
        };
        context.Clients.Add(client);

        var dueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        var paidInvoice = new Invoice
        {
            Id = "BI-CREDIT-PAID",
            InvoiceNo = "BI-CREDIT-PAID",
            ClientId = client.Id,
            FreightCharges = 1000m,
            DueDate = dueDate
        };
        BillingComputationService.RecalculateInvoice(paidInvoice);
        paidInvoice.AmountPaid = paidInvoice.TotalAmount;
        BillingComputationService.RecalculateInvoice(paidInvoice);

        var nextInvoice = new Invoice
        {
            Id = "BI-CREDIT-OPEN",
            InvoiceNo = "BI-CREDIT-OPEN",
            ClientId = client.Id,
            FreightCharges = 500m,
            DueDate = dueDate
        };
        BillingComputationService.RecalculateInvoice(nextInvoice);
        context.Invoices.AddRange(paidInvoice, nextInvoice);
        await context.SaveChangesAsync();

        var cashFlowCount = await context.CashFlowTransactions.CountAsync();
        var createHandler = new AdjustmentFeatures.CreateAdjustmentCommandHandler(context);
        var adjustment = await createHandler.Handle(new AdjustmentFeatures.CreateAdjustmentCommand(
            paidInvoice.InvoiceNo,
            "Credit Memo",
            100m,
            "Approved credit memo",
            "Accountant"), CancellationToken.None);
        Assert.NotNull(adjustment);

        var approveHandler = new AdjustmentFeatures.ApproveAdjustmentCommandHandler(context);
        var approved = await approveHandler.Handle(
            new AdjustmentFeatures.ApproveAdjustmentCommand(adjustment!.Id, "Finance Manager"),
            CancellationToken.None);

        Assert.NotNull(approved);
        Assert.Equal("Paid", paidInvoice.PaymentStatus);
        Assert.Equal(0m, client.AvailableCredit);
        Assert.Equal(100m, nextInvoice.CreditApplied);
        Assert.Equal(nextInvoice.TotalAmount - 100m, nextInvoice.Balance);
        Assert.Equal(cashFlowCount, await context.CashFlowTransactions.CountAsync());
    }

    [Fact]
    public async Task PaymentExceptionResolution_OnlyAllowsApprovedOverpaymentsIntoValidation()
    {
        using var context = await GetDbContextAsync();
        var invoice = await context.Invoices.FirstAsync(i => i.InvoiceNo == "LZD-2026-0001");
        var overpayment = new Payment
        {
            Id = "PAY-OVERPAY-TEST",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            ClientId = invoice.ClientId,
            Amount = invoice.Balance + 10m,
            PaymentStatus = "Exception",
            RejectionReason = "Overpayment: test exception"
        };
        var shortPayment = new Payment
        {
            Id = "PAY-SHORT-TEST",
            InvoiceId = invoice.Id,
            InvoiceNo = invoice.InvoiceNo,
            ClientId = invoice.ClientId,
            Amount = invoice.Balance - 10m,
            PaymentStatus = "Exception",
            RejectionReason = "Short payment: test exception"
        };
        context.Payments.AddRange(overpayment, shortPayment);
        await context.SaveChangesAsync();

        var handler = new PaymentFeatures.ResolvePaymentExceptionCommandHandler(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new PaymentFeatures.ResolvePaymentExceptionCommand(
                shortPayment.Id, "send-to-validation", "Not eligible", "Finance Manager"),
            CancellationToken.None));

        var resolved = await handler.Handle(
            new PaymentFeatures.ResolvePaymentExceptionCommand(
                overpayment.Id, "send-to-validation", "Approved excess as client credit", "Finance Manager"),
            CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal("Pending Validation", resolved!.PaymentStatus);
        Assert.Equal("Approved excess as client credit", resolved.ExceptionResolutionReason);
        Assert.Null(resolved.RejectionReason);
    }
}
