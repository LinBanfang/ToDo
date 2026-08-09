namespace ToDo.Sync;

/// <summary>Logging seam so the sync engine (which lives in ToDo.Core) can report to
/// the host app's logger without the library depending on it. The app wires the
/// channels once at startup (e.g. to DiagnosticLog); null = no-op.</summary>
public static class SyncDiagnostics
{
    /// <summary>INFO channel.</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>WARN channel (off-nominal but handled: apply failures, LWW conflicts).</summary>
    public static Action<string>? LogWarn { get; set; }

    /// <summary>ERROR channel (round-trip-level failures).</summary>
    public static Action<string>? LogError { get; set; }

    public static void Info(string message) => Log?.Invoke(message);
    public static void Warn(string message) => LogWarn?.Invoke(message);
    public static void Error(string message) => LogError?.Invoke(message);
}
