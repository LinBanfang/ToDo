// Seeds a fresh To Do database with demo data that exercises every feature the UI
// can render: system lists, custom lists with sidebar groups, per-list task groups,
// sub-steps, colored tags, due dates, My Day, Important, notes, and completed /
// cancelled close records. Run with the target DB path as the first argument:
//
//   dotnet run --project ToDo.Demo -- "d:\tmp\todo-demo.db"
//
// The app can be pointed at the result by setting DbPath in %LOCALAPPDATA%\ToDo\settings.json.
using ToDo.Models;
using ToDo.Services;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: todo-demo <db-path>");
    return 1;
}
var dbPath = args[0];
if (File.Exists(dbPath)) File.Delete(dbPath);

var db = new DatabaseService(dbPath);   // auto-seeds the 4 system lists

long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
long Ms(int daysFromToday, int hour = 9, int minute = 0) =>
    new DateTimeOffset(DateTime.Today.AddDays(daysFromToday).AddHours(hour).AddMinutes(minute)).ToUnixTimeMilliseconds();

TaskStep Step(string title, bool done, int order) => new() { Title = title, Completed = done, Order = order };

TaskList NewList(string id, string name, string icon, string? groupId, int order) =>
    new() { Id = id, Name = name, Icon = icon, Type = ListType.Custom, GroupId = groupId, Order = order, CreatedAt = Now() };

TaskItem Task(string listId, string title, int order,
    string? groupId = null, IEnumerable<Tag>? tags = null,
    bool important = false, bool myDay = false, int myDayOrder = -1,
    long? due = null, string? note = null, IEnumerable<TaskStep>? steps = null) =>
    new()
    {
        Title = title, ListId = listId, GroupId = groupId, Order = order,
        IsImportant = important, IsMyDay = myDay, MyDayOrder = myDayOrder,
        DueDate = due, Note = note,
        Steps = steps == null ? new() : new(steps),
        TagIds = tags?.Select(t => t.Id).ToList() ?? new(),
    };

// ─── Tags ────────────────────────────────────────────────
var tagSpecs = new (string Name, string Color)[]
{
    ("紧急", "#D13438"), ("重要", "#F7630C"), ("工作", "#0078D4"),
    ("个人", "#107C10"), ("学习", "#8764B8"), ("健康", "#038387"),
    ("购物", "#E74856"), ("家庭", "#FFB900"),
};
var tags = new Dictionary<string, Tag>();
foreach (var (name, color) in tagSpecs)
{
    var tag = new Tag { Name = name, Color = color };
    db.Tags.Insert(tag);
    tags[name] = tag;
}

// ─── Sidebar list groups ─────────────────────────────────
var gProject = new ListGroup { Id = "lg-project", Name = "项目", Order = 0 };
var gLife    = new ListGroup { Id = "lg-life",    Name = "生活", Order = 1 };
db.ListGroups.Insert(gProject);
db.ListGroups.Insert(gLife);

// ─── Custom lists ────────────────────────────────────────
var work   = NewList("list-work",   "工作", "💼", gProject.Id, 0);
var study  = NewList("list-study",  "学习", "📚", gProject.Id, 1);
var life   = NewList("list-life",   "生活", "🛒", gLife.Id, 0);
var fitness= NewList("list-fitness","健身", "🏃", gLife.Id, 1);
db.Lists.Insert(work);
db.Lists.Insert(study);
db.Lists.Insert(life);
db.Lists.Insert(fitness);

// ─── Task groups inside 工作 ─────────────────────────────
var grpDev  = new TaskGroup { Id = "grp-dev",  ListId = work.Id, Name = "开发任务",   Order = 0 };
var grpMeet = new TaskGroup { Id = "grp-meet", ListId = work.Id, Name = "会议与沟通", Order = 1 };
db.Groups.Insert(grpDev);
db.Groups.Insert(grpMeet);

// ─── 工作 · 开发任务 ─────────────────────────────────────
db.Tasks.Insert(Task(work.Id, "重构同步引擎的 LWW 合并逻辑", 0,
    groupId: grpDev.Id, important: true, myDay: true, myDayOrder: 0, due: Ms(1, 18),
    note: "服务器按 ModifiedAt 做 last-writer-wins;输家重推时由服务器返回更新版本自愈。",
    tags: new[] { tags["重要"], tags["工作"] },
    steps: new[] {
        Step("编写合并单元测试", true, 0),
        Step("实现 LWW 合并逻辑", true, 1),
        Step("跑全量回归测试",   false, 2),
    }));

db.Tasks.Insert(Task(work.Id, "修复数据库文件被占用导致的备份失败", 1,
    groupId: grpDev.Id, important: true, due: Ms(0, 14),
    note: "备份前先释放 LiteDB 文件句柄,再复制文件。",
    tags: new[] { tags["紧急"], tags["工作"] },
    steps: new[] {
        Step("复现问题",       true, 0),
        Step("修复句柄释放",   true, 1),
        Step("验证备份与恢复", false, 2),
    }));

db.Tasks.Insert(Task(work.Id, "升级 NuGet 依赖到最新稳定版", 2,
    groupId: grpDev.Id, due: Ms(2, 10),
    tags: new[] { tags["工作"] },
    steps: new[] { Step("评估破坏性变更", false, 0) }));

db.Tasks.Insert(Task(work.Id, "为同步协议补充协议版本校验", 3,
    groupId: grpDev.Id, due: Ms(4, 10), tags: new[] { tags["工作"], tags["重要"] }));

var tDoc = Task(work.Id, "编写同步服务器部署文档", 4,
    groupId: grpDev.Id, tags: new[] { tags["工作"] },
    steps: new[] {
        Step("整理部署步骤", true, 0),
        Step("补充常见问题", true, 1),
    });
tDoc.CloseRecord = new CloseRecord { CloseMode = CloseMode.Complete, ClosedAt = Ms(-1, 17) };
db.Tasks.Insert(tDoc);

// ─── 工作 · 会议与沟通 ───────────────────────────────────
db.Tasks.Insert(Task(work.Id, "周一晨会同步开发进度", 0,
    groupId: grpMeet.Id, due: Ms(0, 9), note: "同步四个模块的合并进展与阻塞项。",
    tags: new[] { tags["工作"] }));

db.Tasks.Insert(Task(work.Id, "给团队分享 LWW 合并方案", 1,
    groupId: grpMeet.Id, important: true, due: Ms(5, 15), tags: new[] { tags["工作"] }));

db.Tasks.Insert(Task(work.Id, "回复用户的同步失败反馈", 2,
    groupId: grpMeet.Id, due: Ms(0, 16), tags: new[] { tags["紧急"], tags["工作"] }));

var tCancel = Task(work.Id, "预约与客户的产品评审会", 3,
    groupId: grpMeet.Id, tags: new[] { tags["工作"] });
tCancel.CloseRecord = new CloseRecord { CloseMode = CloseMode.Cancel, ClosedAt = Ms(-2, 11) };
db.Tasks.Insert(tCancel);

// ─── 学习 ────────────────────────────────────────────────
db.Tasks.Insert(Task(study.Id, "完成《C# 并发编程实战》第 7 章", 0,
    myDay: true, myDayOrder: 3, due: Ms(1, 20),
    tags: new[] { tags["学习"] },
    steps: new[] {
        Step("通读本章",     true, 0),
        Step("跑通示例代码", false, 1),
        Step("整理读书笔记", false, 2),
    }));

db.Tasks.Insert(Task(study.Id, "刷 LeetCode 二叉树专题", 1,
    tags: new[] { tags["学习"] },
    steps: new[] {
        Step("前序遍历", true, 0),
        Step("中序遍历", true, 1),
        Step("后序遍历", false, 2),
        Step("层序遍历", false, 3),
    }));

db.Tasks.Insert(Task(study.Id, "整理设计模式笔记", 2,
    due: Ms(6, 10), tags: new[] { tags["学习"], tags["重要"] }));

db.Tasks.Insert(Task(study.Id, "学习 EF Core 事务与并发控制", 3,
    important: true, tags: new[] { tags["学习"] }));

// ─── 生活 ────────────────────────────────────────────────
db.Tasks.Insert(Task(life.Id, "买猫粮和猫砂", 0,
    myDay: true, myDayOrder: 1, due: Ms(0, 19), tags: new[] { tags["购物"] }));

db.Tasks.Insert(Task(life.Id, "预约牙医复诊", 1,
    important: true, due: Ms(1, 10), tags: new[] { tags["个人"] }));

db.Tasks.Insert(Task(life.Id, "缴纳水电燃气费", 2,
    due: Ms(0, 18), tags: new[] { tags["个人"] }));

db.Tasks.Insert(Task(life.Id, "给爸妈打电话", 3,
    myDay: true, myDayOrder: 4, due: Ms(0, 20), tags: new[] { tags["家庭"] }));

var tClean = Task(life.Id, "周末大扫除", 4, tags: new[] { tags["家庭"] });
tClean.CloseRecord = new CloseRecord { CloseMode = CloseMode.Complete, ClosedAt = Ms(-3, 15) };
db.Tasks.Insert(tClean);

// ─── 健身 ────────────────────────────────────────────────
db.Tasks.Insert(Task(fitness.Id, "晨跑 5 公里", 0,
    myDay: true, myDayOrder: 2, due: Ms(0, 7),
    tags: new[] { tags["健康"] },
    steps: new[] {
        Step("热身", true, 0),
        Step("跑步", true, 1),
        Step("拉伸", false, 2),
    }));

db.Tasks.Insert(Task(fitness.Id, "周三晚瑜伽课", 1,
    important: true, due: Ms(3, 19), tags: new[] { tags["健康"], tags["重要"] }));

db.Tasks.Insert(Task(fitness.Id, "记录一周饮食", 2,
    tags: new[] { tags["健康"] }));

db.Dispose();
Console.WriteLine($"demo db seeded: {Path.GetFullPath(dbPath)}");
return 0;
