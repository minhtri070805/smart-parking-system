using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingOperations.Domain.Entities;

namespace ParkingOperations.Persistence.EntityTypeConfigurations;

public sealed class CashReconciliationConfiguration : IEntityTypeConfiguration<CashReconciliation>
{
    public void Configure(EntityTypeBuilder<CashReconciliation> entity)
    {
        entity.ToTable("cash_reconciliations");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.ShiftId).HasColumnName("shift_id").IsRequired();
        entity.Property(x => x.OpeningCash).HasColumnName("opening_cash").HasPrecision(14, 2);
        entity.Property(x => x.CashReceipts).HasColumnName("cash_receipts").HasPrecision(14, 2);
        entity.Property(x => x.CashRefunds).HasColumnName("cash_refunds").HasPrecision(14, 2);
        entity.Property(x => x.CashDisbursements).HasColumnName("cash_disbursements").HasPrecision(14, 2);
        entity.Property(x => x.ExpectedClosingCash).HasColumnName("expected_closing_cash").HasPrecision(14, 2);
        entity.Property(x => x.CountedClosingCash).HasColumnName("counted_closing_cash").HasPrecision(14, 2);
        entity.Property(x => x.Variance).HasColumnName("variance").HasPrecision(14, 2);
        entity.Property(x => x.PaymentSummaryReference).HasColumnName("payment_summary_reference").HasMaxLength(200).IsRequired();
        entity.Property(x => x.PaymentSummaryCapturedAtUtc).HasColumnName("payment_summary_captured_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.VarianceReason).HasColumnName("variance_reason").HasMaxLength(500);
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(x => x.SubmittedByManagerId).HasColumnName("submitted_by_manager_id").IsRequired();
        entity.Property(x => x.SubmittedAtUtc).HasColumnName("submitted_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.ApprovedByManagerId).HasColumnName("approved_by_manager_id");
        entity.Property(x => x.ApprovedAtUtc).HasColumnName("approved_at_utc").HasColumnType("timestamp with time zone");
        entity.Property(x => x.ApprovalNote).HasColumnName("approval_note").HasMaxLength(500);
        entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        entity.HasOne(x => x.Shift).WithOne().HasForeignKey<CashReconciliation>(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => x.ShiftId).IsUnique().HasDatabaseName("ux_cash_reconciliations_shift");
        entity.HasIndex(x => new { x.Status, x.SubmittedAtUtc }).HasDatabaseName("ix_cash_reconciliations_status_submitted_at");
    }
}
