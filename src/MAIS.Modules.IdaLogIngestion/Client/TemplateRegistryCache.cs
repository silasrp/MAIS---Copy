using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MAIS.Modules.IdaLogIngestion.Models;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.IdaLogIngestion.Client;

/// <summary>
/// Client-side read-through cache of the server's canonical template registry for one application.
/// Persists the last-known snapshot to local disk so the pipeline can classify without network
/// access during startup or while the server is temporarily unreachable.
/// </summary>
public sealed class TemplateRegistryCache
{
    private readonly HttpClient                    _http;
    private readonly string                        _appId;
    private readonly string                        _localCachePath;
    private readonly ILogger<TemplateRegistryCache> _logger;

    // Replaced atomically on every successful refresh; readers never see a partial update.
    private volatile IReadOnlyDictionary<string, TemplateRegistryEntry> _entries =
        new Dictionary<string, TemplateRegistryEntry>();
    private int _cachedVersion = -1;

    private static readonly JsonSerializerOptions _json = new();

    public TemplateRegistryCache(
        HttpClient http,
        string appId,
        string localCachePath,
        ILogger<TemplateRegistryCache> logger)
    {
        _http           = http;
        _appId          = appId;
        _localCachePath = localCachePath;
        _logger         = logger;
    }

    public TemplateRegistryEntry? Lookup(string templateId) =>
        _entries.GetValueOrDefault(templateId);

    /// <summary>
    /// Loads the local disk cache (if any) then attempts a conditional GET from the server.
    /// Safe to call concurrently — the volatile reference swap is the only write path.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        if (_cachedVersion < 0)
            await TryLoadLocalCacheAsync(ct);

        try
        {
            var response = await _http.GetAsync(
                $"/api/v1/ida/templates/registry?appId={_appId}&currentVersion={_cachedVersion}",
                ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return;

            response.EnsureSuccessStatusCode();

            var snapshot = await response.Content.ReadFromJsonAsync<TemplateRegistrySnapshot>(
                cancellationToken: ct);

            if (snapshot is null) return;

            ApplySnapshot(snapshot);
            await PersistLocalCacheAsync(snapshot, ct);

            _logger.LogInformation(
                "Template registry refreshed for {App}: {Count} entries (v{Version})",
                _appId, snapshot.Entries.Count, snapshot.Version);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                "Registry refresh failed for {App}, staying on cached v{Version}: {Msg}",
                _appId, _cachedVersion, ex.Message);
        }
    }

    private void ApplySnapshot(TemplateRegistrySnapshot snapshot)
    {
        _cachedVersion = snapshot.Version;
        _entries = snapshot.Entries.ToDictionary(e => e.TemplateId);
    }

    private async Task TryLoadLocalCacheAsync(CancellationToken ct)
    {
        if (!File.Exists(_localCachePath)) return;
        try
        {
            var json     = await File.ReadAllTextAsync(_localCachePath, ct);
            var snapshot = JsonSerializer.Deserialize<TemplateRegistrySnapshot>(json, _json);
            if (snapshot is not null)
            {
                ApplySnapshot(snapshot);
                _logger.LogInformation(
                    "Template registry loaded from local cache for {App} (v{Version})",
                    _appId, snapshot.Version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read local registry cache for {App}; will fetch from server", _appId);
        }
    }

    private async Task PersistLocalCacheAsync(TemplateRegistrySnapshot snapshot, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(_localCachePath);
            if (dir is not null) Directory.CreateDirectory(dir);

            var tempPath = _localCachePath + ".tmp";
            var json     = JsonSerializer.Serialize(snapshot, _json);
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, _localCachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist local registry cache for {App}", _appId);
        }
    }
}
