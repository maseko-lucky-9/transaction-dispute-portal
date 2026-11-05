namespace DisputePortal.Api.Shared.Errors;

/// <summary>A unique constraint refused the write; <see cref="ConstraintName"/> lets a caller tell which one.</summary>
public sealed class DuplicateKeyException(string? constraintName, Exception? cause = null)
    : ApiException("The resource already exists.", StatusCodes.Status409Conflict, "duplicate", cause)
{
    public string? ConstraintName { get; } = constraintName;
}
