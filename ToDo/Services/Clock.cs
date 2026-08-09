namespace ToDo.Services;

/// <summary>
/// Testable time source. Production uses the real <see cref="SystemClock"/>; tests
/// inject a fake to pin "today" so date-driven logic (My Day reset, due-today filters)
/// can be exercised deterministically.
/// </summary>
public interface IClock
{
    /// <summary>Current local date (start of day).</summary>
    DateTime Today { get; }

    /// <summary>Current UTC instant, used for ModifiedAt stamps.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Real clock — the production <see cref="IClock"/>.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTime Today => DateTime.Today;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    private SystemClock() { }
}
