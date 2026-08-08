namespace ToDo.Sync;

/// <summary>Logging seam so the sync engine (which lives in ToDo.Core) can report to
/// the host app's logger without the library depending on it.</summary>
public static class SyncDiagnostics
{
    /// <summary>Wired by the app (e.g. to DiagnosticLog); null = no-op.</summary>
    public static Action<string>? Log { get; set; }

    public static void Info(string message) => Log?.Invoke(message);
}
