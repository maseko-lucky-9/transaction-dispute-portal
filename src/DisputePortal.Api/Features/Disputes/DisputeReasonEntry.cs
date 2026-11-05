namespace DisputePortal.Api.Features.Disputes;

/// <summary>A row of the reason catalogue (dispute_reasons): reference data seeded by the migration, read-only to the app.</summary>
public sealed class DisputeReasonEntry
{
    public const int CodeMaxLength = 40;
    public const int LabelMaxLength = 80;
    public const int DescriptionMaxLength = 300;

    public string Code { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>The order the form lists the reasons in ("other" last).</summary>
    public int SortOrder { get; init; }
}
