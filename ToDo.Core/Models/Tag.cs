using CommunityToolkit.Mvvm.ComponentModel;

namespace ToDo.Models;

public partial class Tag : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _color = "#0078D4";

    [ObservableProperty]
    private long _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
