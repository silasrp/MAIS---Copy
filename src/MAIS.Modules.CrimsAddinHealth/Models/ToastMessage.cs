namespace MAIS.Modules.CrimsAddinHealth.Models;

public sealed class ToastMessage
{
    public string    ToastId                  { get; init; } = Guid.NewGuid().ToString();
    public string    Title                    { get; init; } = "";
    public string    Body                     { get; init; } = "";
    public ToastType Type                     { get; init; }
    public bool      RequiresAction           { get; init; }
    public string?   ActionLabel              { get; init; }
    public string?   ActionCallbackMessageType { get; init; }
}

public enum ToastType { Info, Warning, Success, Critical }