# 插件加载机制 spike

验证 [plugin-system.md](../../docs/plugin-system.md) 的三个关键技术风险（契约单载 / 可回收 ALC 卸载 / WPF 从外部目录加载带 XAML 的 UserControl）。

## 结构

- `Contract/` — 共享契约（net9.0），宿主与插件引用同一份。
- `Plugin/` — 插件（net9.0-windows，`UseWPF`），含编译 XAML（`WidgetView`）与纯代码 UI（`CodeWidgetPlugin`）两个变体。
- `Host/` — 控制台 + WPF 混合宿主，逐项跑 U1–U6 实验并打印 PASS/FAIL。

## 运行

```powershell
# 1. 发布插件（framework-dependent，输出含 Plugin.dll + Plugin.deps.json）
dotnet publish spikes/plugin-loading/Plugin/Plugin.csproj -c Release -o spikes/plugin-loading/_out/plugin-release

# 2. 跑全部实验
dotnet run -c Release --project spikes/plugin-loading/Host/Host.csproj -- spikes/plugin-loading/_out/plugin-release

# 3. 单独跑某个实验（隔离进程，避免状态污染）
dotnet run -c Release --project spikes/plugin-loading/Host/Host.csproj -- spikes/plugin-loading/_out/plugin-release U4
```

## 结论（net9.0-windows，Debug/Release 均复现）

| 实验 | 结论 |
|---|---|
| U1 契约单载 | `plugin is IPlugin == true` ✅ |
| U2 非 UI 插件卸载 | 干净卸载、文件锁释放 ✅ |
| U3a 编译 XAML 经 ALC 从外部目录加载 | 可用 ✅ |
| U3b 纯代码 UI 经 ALC | 可用 ✅ |
| U3c 编译 XAML 经默认上下文 | 可用 ✅ |
| U3d `AssemblyResolve` 钩子 | 可用（资源解析不稳时的确定性兜底）✅ |
| U4 实例化 WPF 视图后卸载 | **程序集被 WPF 钉住、文件锁不释放 ❌**（→ UI 插件不可热卸载）|
| U6 同名多副本并存时编译 XAML | 可用 ✅ |

> 注意：collectible ALC 卸载测试必须在「加载方法返回 WeakReference 后、在调用方 GC」——若在同一方法内持有 `Assembly`/`ALC` 局部变量时 GC，JIT 会保活局部变量导致假性「卸载失败」。本仓库 `Program.cs` 已用 `NoInlining` 的 `LoadAndUnload` 规避。
