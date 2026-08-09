using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;

namespace ToDo.Models;

public partial class TaskStep : ObservableObject, IOrdered
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _completed;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    [BsonIgnore]
    private bool _isEditing;

    [ObservableProperty]
    [BsonIgnore]
    private string _editTitle = string.Empty;
}
