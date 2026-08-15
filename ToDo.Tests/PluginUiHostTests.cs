using System.IO;
using System.Windows.Threading;
using ToDo.Plugin.Abstractions;
using ToDo.Plugins;
using ToDo.Services;
using ToDo.ViewModels;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// M3 测试：IUiHost 的三个扩展点——快速添加拦截器、设置页节、资源字典合并（headless 下为 no-op）。
/// </summary>
[Collection("settings-shared")]
public sealed class PluginUiHostTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-pluginuihost-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private readonly TodoEvents _events = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly TodoHost _host;

    public PluginUiHostTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
        SettingsService.UseDirectory(_dir);
        _vm = new MainViewModel(_db, events: _events);
        _host = new TodoHost(_db, _vm, _dispatcher, _events, "test.plugin", _ => { });
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class FakeInterceptor : IQuickAddInterceptor
    {
        public int Calls;
        public bool Match;
        public NewTaskDraft Draft = new();

        public bool TryParse(string text, out NewTaskDraft draft)
        {
            Calls++;
            if (Match) { draft = Draft; return true; }
            draft = null!;
            return false;
        }
    }

    [Fact]
    public void RegisterQuickAddInterceptor_is_consulted_by_AddTask()
    {
        var interceptor = new FakeInterceptor
        {
            Match = true,
            Draft = new NewTaskDraft { Title = "交报告", DueDate = 1234567890000 },
        };
        _host.Ui!.RegisterQuickAddInterceptor(interceptor);

        _vm.AddTask("明天下午3点 交报告");

        Assert.Equal(1, interceptor.Calls);
        var task = _vm.Tasks.Single(t => t.Title == "交报告");
        Assert.Equal(1234567890000, task.DueDate);
    }

    [Fact]
    public void AddTask_falls_back_to_default_when_interceptor_does_not_match()
    {
        _host.Ui!.RegisterQuickAddInterceptor(new FakeInterceptor { Match = false });

        _vm.AddTask("普通任务");

        Assert.Contains(_vm.Tasks, t => t.Title == "普通任务");
    }

    [Fact]
    public void RegisterSettingsSection_adds_section()
    {
        _host.Ui!.RegisterSettingsSection("我的插件设置", () => new object());

        var section = Assert.IsType<PluginSettingsSection>(_vm.Settings.Sections.Last());
        Assert.Equal("我的插件设置", section.Title);
        Assert.NotNull(section.View);
    }

    [Fact]
    public void MergeResourceDictionary_is_noop_headless()
    {
        // 无 Application.Current（headless 测试），应安全跳过而非抛异常。
        _host.Ui!.MergeResourceDictionary(new Uri("pack://application:,,,/Foo;component/Resources.xaml"));
    }
}
