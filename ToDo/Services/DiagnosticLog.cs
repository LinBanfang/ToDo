using System.IO;

namespace ToDo.Services;

/// <summary>
/// Minimal diagnostic logger. Appends timestamped lines to
/// &lt;exe dir&gt;\logs\app.log with size-based rotation. Every write is
/// wrapped so a logging failure never affects the app (design: docs/logging.md).
/// </summary>
public static class DiagnosticLog
{
    private const long MaxFileBytes = 1024 * 1024; // 1 MB
    private const int MaxBackups = 1;

    private static readonly object Sync = new();
    private static readonly string LogDir = GetLogDir();
    private static readonly string FilePath = Path.Combine(LogDir, "app.log");
    private static readonly string BackupPath = Path.Combine(LogDir, "app.log.1");

    public static void Info(string module, string message) => Write("INFO", module, message);
    public static void Warn(string module, string message) => Write("WARN", module, message);
    public static void Error(string module, string message) => Write("ERROR", module, message);

    private static void Write(string level, string module, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= MaxFileBytes)
                    Rotate();

                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{module}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the app (unwritable dir, locked file, ...)
        }
    }

    /// <summary>Roll app.log → app.log.1, dropping any older backup.</summary>
    private static void Rotate()
    {
        if (File.Exists(BackupPath)) File.Delete(BackupPath);
        File.Move(FilePath, BackupPath);
    }

    private static string GetLogDir()
    {
        try
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        }
        catch
        {
            // Fall back to a temp location rather than failing the static init
            return Path.Combine(Path.GetTempPath(), "ToDo", "logs");
        }
    }
}
