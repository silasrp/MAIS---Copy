using MAIS.Core.Contracts;
using MAIS.Core.Models;

namespace MAIS.Server.Service.Api.Dto;

// ── API Response helpers ──────────────────────────────────────────────────

/// <summary>
/// Helper methods for creating API responses.
/// </summary>
public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail<T>(string message) =>
        new() { Success = false, Message = message };
}

// ── Module DTO ────────────────────────────────────────────────────────────

/// <summary>Serialisable projection of a <see cref="ModuleDescriptor"/> for server API responses.</summary>
public sealed class ModuleDto
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Version { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public string? StatusMessage { get; init; }
    public string? LaunchUri { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
    public DateTimeOffset? LastHealthCheck { get; init; }

    public static ModuleDto FromDescriptor(ModuleDescriptor d) => new()
    {
        Id = d.Id,
        DisplayName = d.DisplayName,
        Description = d.Description,
        Version = d.Version,
        Type = d.Type.ToString(),
        Status = d.Status.ToString(),
        StatusMessage = d.StatusMessage,
        LaunchUri = d.LaunchUri,
        RegisteredAt = d.RegisteredAt,
        LastHealthCheck = d.LastHealthCheck
    };
}

// ── Health DTO ────────────────────────────────────────────────────────────

/// <summary>Serialisable projection of a <see cref="ModuleHealth"/> for server API responses.</summary>
public sealed class HealthDto
{
    public required string ModuleId { get; init; }
    public required string Status { get; init; }
    public string? StatusMessage { get; init; }
    public IReadOnlyDictionary<string, object> Diagnostics { get; init; }
        = new Dictionary<string, object>();
    public DateTimeOffset CheckedAt { get; init; }
    public double CheckDurationMs { get; init; }

    public static HealthDto FromHealth(ModuleHealth h) => new()
    {
        ModuleId = h.ModuleId,
        Status = h.Status.ToString(),
        StatusMessage = h.StatusMessage,
        Diagnostics = h.Diagnostics,
        CheckedAt = h.CheckedAt,
        CheckDurationMs = h.CheckDuration.TotalMilliseconds
    };
}
