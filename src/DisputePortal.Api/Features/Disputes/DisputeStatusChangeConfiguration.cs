using DisputePortal.Api.Features.Customers;
using DisputePortal.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputePortal.Api.Features.Disputes;

public sealed class DisputeStatusChangeConfiguration : IEntityTypeConfiguration<DisputeStatusChange>
{
    private const string Statuses = "('Submitted', 'UnderReview', 'Accepted', 'Rejected', 'Withdrawn')";
    private const int EnumMaxLength = 16;

    public void Configure(EntityTypeBuilder<DisputeStatusChange> builder)
    {
        builder.ToTable("dispute_status_changes", table =>
        {
            table.HasCheckConstraint("ck_dispute_status_changes_actor_type", "actor_type IN ('Customer', 'Agent')");
            table.HasCheckConstraint("ck_dispute_status_changes_to_status", "to_status IN " + Statuses);
            table.HasCheckConstraint("ck_dispute_status_changes_from_status", "from_status IS NULL OR from_status IN " + Statuses);
        });
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).UseIdentityByDefaultColumn();
        builder.Property(change => change.From).HasColumnName("from_status").HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(change => change.To).HasColumnName("to_status").HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(change => change.ActorType).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(change => change.ActorId).HasMaxLength(Customer.SubjectMaxLength).IsRequired();
        builder.Property(change => change.Note).HasMaxLength(Dispute.NoteMaxLength);
        // The database stamps the time; EF never sends it (the app role has no INSERT on the column) and reads it back.
        builder.Property(change => change.OccurredAt)
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd()
            .Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        builder.HasIndex(change => new { change.DisputeId, change.OccurredAt });
    }
}
