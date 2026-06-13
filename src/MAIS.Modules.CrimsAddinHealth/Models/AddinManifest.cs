namespace MAIS.Modules.CrimsAddinHealth.Models;

public sealed class AddinManifest
{
    public DateTimeOffset GeneratedAt    { get; init; } = DateTimeOffset.UtcNow;
    public string         RepositoryPath { get; init; } = "";
    public IReadOnlyList<AddinManifestEntry> Entries { get; init; } = [];
}

public sealed class AddinManifestEntry
{
    public string         FileName      { get; init; } = "";
    public string         Version       { get; init; } = "";
    public long           FileSizeBytes { get; init; }
    public string         Sha256        { get; init; } = "";
    public string?        ReleaseNotes  { get; init; }
    public DateTimeOffset LastModified  { get; init; }
}