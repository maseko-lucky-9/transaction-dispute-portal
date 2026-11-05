using DisputePortal.Api.Features.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputePortal.Api.Features.Transactions;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();
        builder.Property(account => account.MaskedNumber).HasMaxLength(Account.MaskedNumberMaxLength).IsRequired();
        builder.Property(account => account.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.HasOne<Customer>().WithMany().HasForeignKey(account => account.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
