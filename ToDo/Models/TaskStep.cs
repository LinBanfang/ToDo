using CommunityToolkit.Mvvm.ComponentModel;

namespace ToDo.Models;

public partial class TaskStep : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _completed;

    [ObservableProperty]
    private int _order;
}
