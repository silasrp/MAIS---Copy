using MAIS.Core.Models;

namespace MAIS.Modules.CrimsAddinHealth;

public sealed class CrimsAddinHealthOptions
{
    public const string SectionName = "Modules:CrimsAddinHealth";

    public ModuleHostType HostType { get; set; } = ModuleHostType.Both;

    // ── Server-side ──────────────────────────────────────────────────────────
    public string RepositoryFolderPath  { get; set; } = @"D:\MAIS\CrimsAddinRepository";
    public string AuditFolderPath       { get; set; } = @"D:\MAIS\Audit\addin-health";
    public string RepositoryUncPath     { get; set; } = @"\\server\MAIS_CrimsAddins";
    public int    MaxConcurrentUpdates  { get; set; } = 1;
    public string AgentExePath          { get; set; } = @"Agent\agent.exe";
    public int    AgentTimeoutSeconds   { get; set; } = 30;

    // ── Client-side ──────────────────────────────────────────────────────────
    public string ServerApiUrl { get; set; } = "";
    public string CrimsAddinsFolder              { get; set; } = @"C:\CharlesRiver\23R3\Client\AddIns";
    public string AssetTag                       { get; set; } = "";
    public string CrimsUserId                    { get; set; } = "";
    public string MachineRole  { get; set; } = "";

    public string ScanSchedule                   { get; set; } = "0 8 * * *";
    public int    CrimsCloseTimeoutSeconds       { get; set; } = 300;
    public int    ValidationRetryCount           { get; set; } = 3;
    public int    UpdateResponseTimeoutSeconds   { get; set; } = 120;
    public int    CrimsStatusCheckIntervalSeconds { get; set; } = 5;
}