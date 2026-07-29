using CommunityToolkit.Mvvm.ComponentModel;

namespace ToDo.Models;

/// <summary>
/// Close mode for a task: Complete or Cancel
/// </summary>
public enum CloseMode
{
    Complete,
    Cancel
}

public partial class CloseRecord : ObservableObject
{
    [ObservableProperty]
    private long _closedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [ObservableProperty]
    private CloseMode _closeMode = CloseMode.Complete;
}
