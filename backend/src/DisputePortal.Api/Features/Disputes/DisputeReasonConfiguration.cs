using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DisputePortal.Api.Features.Disputes;

public sealed class DisputeReasonConfiguration : IEntityTypeConfiguration<DisputeReasonEntry>
{
    public void Configure(EntityTypeBuilder<DisputeReasonEntry> builder)
    {
        builder.ToTable("dispute_reasons");
        builder.HasKey(reason => reason.Code);
        builder.Property(reason => reason.Code).HasMaxLength(DisputeReasonEntry.CodeMaxLength);
        builder.Property(reason => reason.Label).HasMaxLength(DisputeReasonEntry.LabelMaxLength).IsRequired();
        builder.Property(reason => reason.Description).HasMaxLength(DisputeReasonEntry.DescriptionMaxLength).IsRequired();
        // Reference data: the migration inserts it, and a change to it is a new migration.
        builder.HasData(
            Reason(1, "unrecognised_transaction", "I don't recognise this transaction", "You did not make or authorise this transaction."),
            Reason(2, "duplicate_charge", "Charged more than once", "The same purchase was charged to your account more than once."),
            Reason(3, "incorrect_amount", "Wrong amount charged", "You were charged a different amount from the one you agreed to pay."),
            Reason(4, "goods_not_received", "Goods or services not received", "You paid but did not receive what you ordered."),
            Reason(5, "subscription_cancelled", "Subscription already cancelled", "You were charged for a subscription you had already cancelled."),
            Reason(6, "card_lost_or_stolen", "Card lost or stolen", "The transaction happened after your card was lost or stolen."),
            Reason(7, "other", "Something else", "Another problem; describe it in your own words."));
    }

    private static DisputeReasonEntry Reason(int sortOrder, string code, string label, string description) =>
        new() { Code = code, Label = label, Description = description, SortOrder = sortOrder };
}
