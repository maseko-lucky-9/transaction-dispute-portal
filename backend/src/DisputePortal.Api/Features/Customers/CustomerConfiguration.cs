using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputePortal.Api.Features.Customers;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();
        builder.Property(customer => customer.KeycloakSubject).HasMaxLength(Customer.SubjectMaxLength).IsRequired();
        builder.HasIndex(customer => customer.KeycloakSubject).IsUnique();
        builder.Property(customer => customer.DisplayName).HasMaxLength(Customer.DisplayNameMaxLength).IsRequired();
    }
}
