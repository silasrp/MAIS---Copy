using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MAIS.Modules.IdaLogIngestion.Models;

namespace MAIS.Modules.IdaLogIngestion.Client;

/// <summary>
/// Drain-style local template miner. Clusters log messages into stable token-pattern
/// templates by matching them against shapes already seen by this process, then checking
/// whether the matched shape is registered in the server's canonical registry.
///
/// The miner owns local shape state; the registry owns canonical, persisted knowledge.
/// IsNew on the returned TemplateMatch means "not in the currently cached registry" —
/// which can mean genuinely new fleet-wide, or simply not yet refreshed on this client.
/// The server resolves which.
/// </summary>
public sealed class LogTemplateMiner
{
    private readonly TemplateRegistryCache _registryCache;
    private readonly double _similarityThreshold;
    private readonly int _maxTemplates;
    private readonly object _syncRoot = new();

    // First-level filter: token count → list of shapes with that count.
    private readonly Dictionary<int, List<LocalTemplateShape>> _localShapesByTokenCount = new();

    public LogTemplateMiner(LogSourceDefinition source, TemplateRegistryCache registryCache)
    {
        _registryCache       = registryCache;
        _similarityThreshold = source.TemplateSimilarityThreshold;
        _maxTemplates        = source.MaxTemplates;
    }

    public TemplateMatch Process(string message)
    {
        var tokens = Tokenize(message);
        if (tokens.Length == 0)
            return new TemplateMatch(ComputeTemplateId([message]), isNew: true, [], []);

        lock (_syncRoot)
        {
            var shape = MatchAgainstKnownShapes(tokens);

            if (shape is not null)
            {
                var known = _registryCache.Lookup(shape.TemplateId);
                return new TemplateMatch(
                    shape.TemplateId,
                    isNew: known is null,
                    extractedVariables: shape.DiffPositions(tokens),
                    tokenPattern: (string[])shape.Tokens.Clone());
            }

            var newShape = LocalTemplateShape.CreateFrom(tokens);
            RegisterLocalShape(newShape);
            return new TemplateMatch(newShape.TemplateId, isNew: true, [], (string[])newShape.Tokens.Clone());
        }
    }

    private LocalTemplateShape? MatchAgainstKnownShapes(string[] tokens)
    {
        if (!_localShapesByTokenCount.TryGetValue(tokens.Length, out var candidates))
            return null;

        foreach (var shape in candidates)
            if (shape.TryMerge(tokens, _similarityThreshold))
                return shape;

        return null;
    }

    private void RegisterLocalShape(LocalTemplateShape shape)
    {
        var totalShapes = _localShapesByTokenCount.Values.Sum(l => l.Count);
        if (totalShapes >= _maxTemplates)
            return; // cap reached; novel templates keep being flagged but aren't tracked locally

        if (!_localShapesByTokenCount.TryGetValue(shape.Tokens.Length, out var list))
        {
            list = new List<LocalTemplateShape>();
            _localShapesByTokenCount[shape.Tokens.Length] = list;
        }

        list.Add(shape);
    }

    private static string[] Tokenize(string message) =>
        message.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string ComputeTemplateId(string[] tokens) =>
        LocalTemplateShape.ComputeId(tokens);
}

/// <summary>
/// A single template shape held in the miner's local state. Tokens are either literal
/// constants or the wildcard sentinel "<*>" at positions where different messages have
/// shown different values. The TemplateId is set from the original tokens and never
/// changes, even as more wildcard positions accumulate through merges.
/// </summary>
internal sealed class LocalTemplateShape
{
    private const string Wildcard = "<*>";

    public string   TemplateId { get; }
    public string[] Tokens     { get; }

    private LocalTemplateShape(string templateId, string[] tokens)
    {
        TemplateId = templateId;
        Tokens     = tokens;
    }

    public static LocalTemplateShape CreateFrom(string[] tokens)
    {
        var id = ComputeId(tokens);
        return new LocalTemplateShape(id, (string[])tokens.Clone());
    }

    /// <summary>
    /// Attempts to merge <paramref name="incoming"/> into this shape. If the proportion
    /// of matching positions (exact match or existing wildcard) meets the threshold,
    /// positions that differ are promoted to wildcards in place and true is returned.
    /// </summary>
    public bool TryMerge(string[] incoming, double threshold)
    {
        if (incoming.Length != Tokens.Length) return false;

        int matches = 0;
        for (int i = 0; i < Tokens.Length; i++)
            if (Tokens[i] == Wildcard || Tokens[i] == incoming[i])
                matches++;

        if ((double)matches / Tokens.Length < threshold) return false;

        for (int i = 0; i < Tokens.Length; i++)
            if (Tokens[i] != Wildcard && Tokens[i] != incoming[i])
                Tokens[i] = Wildcard;

        return true;
    }

    /// <summary>Returns the token values at wildcard positions — these are the extracted variables.</summary>
    public string[] DiffPositions(string[] tokens)
    {
        var vars = new List<string>();
        for (int i = 0; i < Tokens.Length && i < tokens.Length; i++)
            if (Tokens[i] == Wildcard)
                vars.Add(tokens[i]);
        return [.. vars];
    }

    /// <summary>Stable 8-byte hex ID derived from the token pattern.</summary>
    public static string ComputeId(string[] tokens)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(' ', tokens)));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
