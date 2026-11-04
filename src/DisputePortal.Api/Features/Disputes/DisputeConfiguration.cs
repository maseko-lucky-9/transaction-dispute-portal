using DisputePortal.Api.Features.Transactions;
using DisputePortal.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputePortal.Api.Features.Disputes;

public sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    /// <summary>The integrity backstop: at most one Submitted or UnderReview dispute per transaction (ADR-007).</summary>
    public const string OneActivePerTransactionIndex = "ux_disputes_one_active_per_transaction";

    /// <summary>Serves the Owner filter and the history keyset order (created_at desc, id desc) in one index scan.</summary>
    public const string KeysetIndexName = "ix_disputes_customer_id_created_at_id";

    public const string PrimaryKeyName = "pk_disputes";

    private const int EnumMaxLength = 16;

    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_disputes_status", "status IN ('Submitted', 'UnderReview', 'Accepted', 'Rejected', 'Withdrawn')");
            table.HasCheckConstraint("ck_disputes_amount_positive", "disputed_amount > 0");
            table.HasCheckConstraint("ck_disputes_resolved_at", "(status IN ('Accepted', 'Rejected', 'Withdrawn')) = (resolved_at IS NOT NULL)");
            table.HasCheckConstraint("ck_disputes_resolution_note", "(status IN ('Accepted', 'Rejected')) = (resolution_note IS NOT NULL)");
        });
        builder.HasKey(dispute => dispute.Id).HasName(PrimaryKeyName);
        builder.Property(dispute => dispute.Id).ValueGeneratedNever();
        builder.Property(dispute => dispute.ReasonCode).HasMaxLength(DisputeReasonEntry.CodeMaxLength).IsRequired();
        builder.Property(dispute => dispute.Description).HasMaxLength(Dispute.DescriptionMaxLength).IsRequired();
        builder.ComplexProperty(dispute => dispute.DisputedAmount, amount =>
        {
            amount.Property(money => money.Amount).HasColumnName("disputed_amount").HasPrecision(19, 4);
            amount.Property(money => money.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });
        builder.Property(dispute => dispute.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(dispute => dispute.ResolutionNote).HasMaxLength(Dispute.NoteMaxLength);
        // xmin: Postgres bumps it on every update, so it is the optimistic-concurrency token and the ETag.
        builder.Property(dispute => dispute.Version).IsRowVersion();
        builder.HasMany(dispute => dispute.StatusChanges).WithOne().HasForeignKey(change => change.DisputeId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(dispute => dispute.StatusChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
        // Composite, like the account FK on transactions: a dispute can only name a transaction of its own customer, so
        // the Owner filter on disputes.customer_id can be trusted. Creates UNIQUE (id, customer_id) on transactions.
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(dispute => new { dispute.TransactionId, dispute.CustomerId })
            .HasPrincipalKey(transaction => new { transaction.Id, transaction.CustomerId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DisputeReasonEntry>().WithMany().HasForeignKey(dispute => dispute.ReasonCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(dispute => new { dispute.CustomerId, dispute.CreatedAt, dispute.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName(KeysetIndexName);
        // The agent queue: by status, oldest first.
        builder.HasIndex(dispute => new { dispute.Status, dispute.CreatedAt, dispute.Id });
        // Per-transaction reads (the latest dispute, the count) in index order; it also covers the composite foreign key.
        builder.HasIndex(dispute => new { dispute.TransactionId, dispute.CustomerId, dispute.CreatedAt, dispute.Id })
            .IsDescending(false, false, true, true);
        builder.HasIndex(dispute => dispute.TransactionId)
            .IsUnique()
            .HasFilter("status IN ('Submitted', 'UnderReview')")
            .HasDatabaseName(OneActivePerTransactionIndex);
    }
}
