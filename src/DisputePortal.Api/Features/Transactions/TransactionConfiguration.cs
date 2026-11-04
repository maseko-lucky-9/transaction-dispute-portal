using DisputePortal.Api.Features.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputePortal.Api.Features.Transactions;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    /// <summary>Serves the owner filter and the keyset order in one index scan (ADR-006).</summary>
    public const string KeysetIndexName = "ix_transactions_customer_id_transaction_date_id";

    private const int EnumMaxLength = 16;

    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable(table =>
        {
            // The row-local rules; the cross-row ones (window, caps) live in the Domain.
            table.HasCheckConstraint("ck_transactions_direction", "direction IN ('Debit', 'Credit')");
            table.HasCheckConstraint("ck_transactions_status", "status IN ('Pending', 'Posted', 'Reversed')");
            table.HasCheckConstraint("ck_transactions_amount_positive", "amount > 0");
            // Posted and reversed transactions were settled; only pending ones have no posting time.
            table.HasCheckConstraint("ck_transactions_posted_at", "(status = 'Pending') = (posted_at IS NULL)");
        });
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Id).ValueGeneratedNever();
        builder.Property(transaction => transaction.Reference).HasMaxLength(Transaction.ReferenceMaxLength).IsRequired();
        builder.HasIndex(transaction => transaction.Reference).IsUnique();
        builder.Property(transaction => transaction.MerchantName).HasMaxLength(Transaction.MerchantNameMaxLength).IsRequired();
        builder.Property(transaction => transaction.Category).HasMaxLength(Transaction.CategoryMaxLength).IsRequired();
        builder.Property(transaction => transaction.Amount).HasPrecision(19, 4);
        builder.Property(transaction => transaction.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(transaction => transaction.Direction).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(transaction => transaction.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.HasIndex(transaction => new { transaction.CustomerId, transaction.TransactionDate, transaction.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName(KeysetIndexName);
        builder.HasOne<Account>().WithMany().HasForeignKey(transaction => transaction.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(transaction => transaction.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
