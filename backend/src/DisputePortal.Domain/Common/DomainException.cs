namespace DisputePortal.Domain.Common;

/// <summary>A domain rule refused an operation. <see cref="Code"/> is stable and machine-readable; the API maps it to 422.</summary>
public class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public DomainException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
