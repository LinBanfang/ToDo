using System.Text;
using ToDo.Plugin.Abstractions;

namespace ExportPlugin;

/// <summary>
/// 首个样例插件：把全部任务按列表分组导出为 Markdown 周报（走宿主门面只读 + SaveTextFile）。
/// 用于验证「加载 → Initialize → 侧边栏入口 → GetTasks → 写文件」整条插件管线。
/// </summary>
public sealed class MarkdownExportPlugin : ITodoPlugin
{
    private ITodoHost _host = null!;

    public string Id => "com.example.export";
    public string Name => "导出 Markdown 周报";
    public string Version => "1.0.0";

    public void Initialize(ITodoHost host)
    {
        _host = host;
        host.Ui?.RegisterSidebarEntry(new SidebarEntry("导出周报", "📤", 100, Export));
    }

    public void Shutdown() { }

    private void Export()
    {
        var tasks = _host.GetTasks(null);                       // 全部任务
        var listNames = _host.GetLists().ToDictionary(l => l.Id, l => l.Name);

        var sb = new StringBuilder();
        sb.AppendLine("# 周报");
        sb.AppendLine();

        foreach (var group in tasks.GroupBy(t => t.ListId))
        {
            sb.AppendLine($"## {listNames.GetValueOrDefault(group.Key, group.Key)}");
            sb.AppendLine();
            foreach (var t in group.OrderBy(t => t.Completed).ThenByDescending(t => t.CreatedAt))
            {
                var mark = t.Completed ? "x" : " ";
                var due = t.DueDate is { } d
                    ? $"  ⏰ {DateTimeOffset.FromUnixTimeMilliseconds(d).LocalDateTime:yyyy-MM-dd}"
                    : "";
                sb.AppendLine($"- [{mark}] {t.Title}{due}");
                if (!string.IsNullOrEmpty(t.Note))
                    sb.AppendLine($"  - {t.Note}");
            }
            sb.AppendLine();
        }

        var path = _host.SaveTextFile("weekly-report.md", sb.ToString(), "Markdown (*.md)|*.md");
        if (path != null)
            _host.Notify("导出完成", path);
    }
}
