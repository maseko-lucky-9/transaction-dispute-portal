namespace DisputePortal.IntegrationTests;

/// <summary>Files of the repository the tests run from, found by walking up to DisputePortal.slnx.</summary>
public static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string PathOf(params string[] segments) => Path.Combine([Root, .. segments]);

    /// <summary>The backend folder; the paths ApiSources returns are relative to it (src/DisputePortal.Api/...).</summary>
    private static string BackendRoot { get; } = Path.Combine(Root, "backend");

    /// <summary>The API's C# sources as backend-relative paths with forward slashes, without bin/obj.</summary>
    public static IReadOnlyList<(string Path, string Text)> ApiSources() =>
        [.. Directory.EnumerateFiles(Path.Combine(BackendRoot, "src", "DisputePortal.Api"), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(BackendRoot, file).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
            .Select(path => (path, File.ReadAllText(Path.Combine(BackendRoot, path))))];

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DisputePortal.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DisputePortal.slnx not found above the test output.");
    }
}
