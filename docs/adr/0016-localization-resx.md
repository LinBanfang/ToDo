# ADR-016: 本地化 RESX 管线（新增语言 = 新增一个 .xx.resx）

## 状态

已采纳（v1：字符串迁入 `Strings.resx`（zh 中性）+ `Strings.en.resx`（en 卫星），`Loc` 保留同名静态门面；新增语言只需新增一个 `.xx.resx`）

## 背景

本地化此前是「静态字符串」——`Loc` 静态类 221 个成员全部是 `Language == AppLanguage.Chinese ? "中文" : "English"` 内联三元。能工作，但**新增语言 = 逐条手改 221 个 C# 成员**，代价高、易漏、diff 巨大。

消费面：127 处 XAML `{x:Static services:Loc.X}` 绑定 + 216 处 C# `Loc.X` 引用。只要 `Loc` 保持同名公开 API，全部调用点零改动。

工程现状：`ToDo.Core`（RootNamespace = `ToDo`，net9.0）无任何 resx / NeutralLanguage 配置；发布为多文件自包含（卫星程序集可用）。

## 决策

### 资源文件：中性 = zh，en 走卫星

- `ToDo.Core/Resources/Strings.resx`：中性（zh 值），manifest 名 `ToDo.Resources.Strings`（RootNamespace 决定）。
- `ToDo.Core/Resources/Strings.en.resx`：en 卫星，自动编译为 `en/ToDo.Core.resources.dll`。
- SDK 默认 glob 自动嵌入，无需 csproj 资源配置；`ToDo.Core.csproj` 加 `<NeutralLanguage>zh-CN</NeutralLanguage>` 作文档。

### `Loc` 门面：公开 API 逐字节不变，内部改读 RESX

`Loc` 保留枚举 / `Language` / `LanguageChanged` / `SetLanguage` / `Toggle` 与全部 221 个成员名，内部用 `ResourceManager` 读取：

```csharp
private static readonly ResourceManager Res = new("ToDo.Resources.Strings", typeof(Loc).Assembly);
private static CultureInfo _culture = CultureInfo.GetCultureInfo("zh-CN"); // 默认中文
private static string S(string key)
{
    try { return Res.GetString(key, _culture) ?? "⟦" + key; }
    catch (MissingManifestResourceException) { return "⟦" + key; }
}
public static void SetLanguage(AppLanguage lang) { /* Language 变更时换 _culture = zh-CN / en-US 并触发 LanguageChanged */ }
```

- **显式文化**：`_culture` 由 `SetLanguage` 设定，资源查找**不依赖**环境线程文化，测试宿主文化不会干扰。
- **哨兵兜底**：缺失键返回 `⟦key⟧`——**不能返回 key 本身**，因为多个 en 值合法等于键名（`OK` / `Delete` / `Save` / `Date`）。反射扫描据此判定缺资源。
- 日期方法统一 `CultureInfo.InvariantCulture` 格式化（zh/en 输出 token 均文化无关）；`Culture` 只用于资源查找。`ReminderTimeOnly`、`RecurrenceName`（枚举 switch）、`TitleTextRecommend`（组合）保持纯逻辑，不进 resx。

### 日期模板：必须是单个自定义模式

`string.Format` 中 `{0:M}` / `{0:d}` 是**标准格式说明符**（`M` = 完整月份名、`d` = 短日期），不是不补零的数字月/日。正确写法是把整个模式写进一个占位符：

| 键 | zh | en |
|---|---|---|
| RelativeDateFormat | `{0:yyyy年M月d日}` | `{0:MMM d, yyyy}` |
| ShortDateFormat | `{0:M月d日}` | `{0:MMM d}` |
| ReminderTimeFormat | `{0:M月d日 HH:mm}` | `{0:MMM d, HH:mm}` |

（`M`/`d` 为自定义格式下的不补零数字月/日；CJK 字符是字面量。）

### 测试三层保证

- **黄金基线** `ToDo.Tests/TestData/loc-golden.txt`：迁移前一次性 capture 全部 221 成员 × 2 语言的值；`LocGoldenTests` 逐值比对，**任何值漂移即失败**（零漂移规格）。
- **zh/en parity** `ResxParityTests`：两套 `ResourceSet` 键集合一致 + 值非空，新增语言漏键即失败。
- **哨兵扫描** `LocTests`：反射遍历字符串属性，任一解析为 `⟦` 哨兵即失败。

### 新增语言配方（不改任何 C# 逻辑）

1. 复制 `Strings.en.resx` → `Strings.xx.resx`，翻译全部 219 条值（键名保持）。
2. `AppLanguage` 枚举加 `Xx` 值。
3. `Loc.SetLanguage` 加 `xx` → 文化的映射分支。
4. `App.OnStartup` 的 `SetLanguage` 调用加 `"Xx"` → 枚举的映射（`SettingsService.Language` 存字符串）。
5. 设置页「语言」ComboBox 加一项。
6. 跑 parity 测试确认键一致；golden 测试对新语言可另存一份 spec。

### 不做的（明确出界）

- **运行时即时切语言**：维持**重启生效**（`LanguageChanged` 生产代码不订阅，同 ADR-0008）。
- 新增第三种语言演示（221 条翻译量；配方写文档即可）。
- 改 settings.json schema、改 vendored `AutoUpdater`、改线程文化（避免影响其他格式化路径）。

## 后果

- 优点：
  - 新增语言从「逐条改 221 个 C# 成员」降到「新增一个 `.xx.resx`」，translation 可交给纯资源文件 diff。
  - 消费面零改动（同名静态 API）；既有测试（`LocTests` / `LocDateTests` / 转换器 / 同步 / 设置分区）全部保持通过。
  - 哨兵 + 黄金 + parity 三层测试把转写漂移和漏键都钉死。
  - `Loc` 门面让 XAML 静态绑定与代码引用都不必改为 `{x:Static}` 之外的机制。
- 权衡 / 已知限制：
  - **单文件发布风险**：卫星程序集（`en/ToDo.Core.resources.dll`）在单文件（`IncludeAllContentForSelfExtract`）场景下可能不随包解压，导致非中性语言不可用。当前发布为**多文件自包含**，已确认 `publish/en/ToDo.Core.resources.dll` 存在；若未来改单文件发布需回归验证。
  - 语言切换仍重启生效（既有约束，未扩大范围）。
  - 日期模板依赖各文化的内置格式分量（`MMM` 等），新增语言时若目标文化无对应分量需检查输出。
