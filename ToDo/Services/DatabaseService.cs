using LiteDB;
using ToDo.Models;
using System.IO;

namespace ToDo.Services;

public class DatabaseService : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly string _dbPath;

    public ILiteCollection<TaskList> Lists { get; }
    public ILiteCollection<TaskGroup> Groups { get; }
    public ILiteCollection<TaskItem> Tasks { get; }
    public ILiteCollection<Tag> Tags { get; }
    public ILiteCollection<ListGroup> ListGroups { get; }

    public DatabaseService(string? dbPath = null)
    {
        // Store DB in project root (not bin/), survives clean builds
var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToDo");
Directory.CreateDirectory(dataDir);
_dbPath = dbPath ?? Path.Combine(dataDir, "todo.db");
        _db = new LiteDatabase($"Filename={_dbPath};Connection=direct");

        Lists = _db.GetCollection<TaskList>("lists");
        Groups = _db.GetCollection<TaskGroup>("groups");
        Tasks = _db.GetCollection<TaskItem>("tasks");
        Tags = _db.GetCollection<Tag>("tags");
        ListGroups = _db.GetCollection<ListGroup>("listgroups");

        // Ensure indexes
        Lists.EnsureIndex(x => x.Type);
        Lists.EnsureIndex(x => x.Order);
        Groups.EnsureIndex(x => x.ListId);
        Tasks.EnsureIndex(x => x.ListId);
        Tasks.EnsureIndex(x => x.GroupId);
        Tasks.EnsureIndex(x => x.IsMyDay);
        Tasks.EnsureIndex(x => x.IsImportant);
        Tasks.EnsureIndex(x => x.DueDate);
        ListGroups.EnsureIndex(x => x.Order);
        Tags.EnsureIndex(x => x.Name, unique: true);

        SeedDefaultData();
    }

    private void SeedDefaultData()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var systemDefaults = new Dictionary<string, (string Name, string Icon, ListType Type)>
        {
            ["list-myday"]     = ("My Day",    "☀️", ListType.MyDay),
            ["list-important"] = ("Important", "⭐",   ListType.Important),
            ["list-planned"]   = ("Planned",   "📅", ListType.Planned),
            ["list-tasks"]     = ("Tasks",     "🏠", ListType.Tasks),
        };

        if (Lists.Count() == 0)
        {
            var systemLists = systemDefaults.Select(kv => new TaskList
            {
                Id = kv.Key, Name = kv.Value.Name, Icon = kv.Value.Icon,
                Type = kv.Value.Type, IsSystem = true, Order = 0, CreatedAt = now
            }).ToArray();
            Lists.InsertBulk(systemLists);
        }
        else
        {
            // Migrate: ensure system lists have icons
            foreach (var (id, (name, icon, type)) in systemDefaults)
            {
                var existing = Lists.FindById(id);
                if (existing != null && string.IsNullOrEmpty(existing.Icon))
                {
                    existing.Icon = icon;
                    Lists.Update(existing);
                }
            }
        }
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
