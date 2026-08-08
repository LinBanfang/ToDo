using CommunityToolkit.Mvvm.ComponentModel;

namespace ToDo.Models;

public partial class ListGroup : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private bool _collapsed;

    [ObservableProperty]
    private long _modifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
