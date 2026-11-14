namespace DisputePortal.Api.Shared.Secrets;

/// <summary>
/// The inner-exception chain of a Vault startup failure as one line, for the Critical startup log: the outer message
/// alone hid a TLS trust failure behind "unreachable". Only exception type names and messages are used, never request
/// bodies, headers or <see cref="Exception.Data"/>; the provider's messages never carry the token, role id or secret id.
/// </summary>
public static class VaultFailureDetail
{
    public const int MaxDepth = 4;

    public const int MaxMessageLength = 200;

    private const string Separator = " <- ";

    /// <summary>Type and message of up to <see cref="MaxDepth"/> inner exceptions; empty when there are none.</summary>
    public static string Describe(Exception exception)
    {
        List<string> causes = [];
        for (Exception? cause = exception.InnerException; cause is not null && causes.Count < MaxDepth; cause = cause.InnerException)
        {
            causes.Add($"{cause.GetType().Name}: {OneLine(cause.Message)}");
        }

        return string.Join(Separator, causes);
    }

    // Vault's error bodies can span lines; the startup log stays one line.
    private static string OneLine(string message)
    {
        string flat = message.ReplaceLineEndings(" ");
        return flat.Length <= MaxMessageLength ? flat : string.Concat(flat.AsSpan(0, MaxMessageLength), "...");
    }
}
