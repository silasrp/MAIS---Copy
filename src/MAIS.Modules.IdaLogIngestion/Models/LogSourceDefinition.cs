namespace MAIS.Modules.IdaLogIngestion.Models;

public sealed class LogSourceDefinition
{
    public required string AppId           { get; set; }
    public required string DisplayName     { get; set; }
    public required string LogFolderPath   { get; set; }
    public required string ActiveFileName  { get; set; }

    public string MultilineStartPattern    { get; set; } = @"^\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2},\d{3}";
    public string CompatibilityAppName     { get; set; } = "trading_app";

    public int    MaxQueueSizeMb           { get; set; } = 200;
    public int    MaxQueueAgeHours         { get; set; } = 48;
    public int    MaxTemplates             { get; set; } = 500;
    public double TemplateSimilarityThreshold { get; set; } = 0.5;

    public int    RegistryRefreshIntervalSeconds { get; set; } = 300;
    public int    StatsBucketSeconds             { get; set; } = 60;
    public int    UploadBackoffMinSeconds        { get; set; } = 5;
    public int    UploadBackoffMaxSeconds        { get; set; } = 300;
    public int    RotationBackstopPollSeconds    { get; set; } = 10;
    public int    MultilineMaxWaitSeconds        { get; set; } = 5;
}
