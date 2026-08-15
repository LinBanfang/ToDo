using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using ToDo.Plugin.Abstractions;
using ToDo.Services;

namespace ToDo.Plugins;

/// <summary>插件更新结果：Success 为假时 <see cref="Error"/> 说明原因；RequiresRestart 为真表示 UI 插件需重启生效。</summary>
public sealed record PluginUpdateResult(
    bool Success, string PluginId, string Version, bool RequiresRestart, string? Error);

/// <summary>
/// 插件安装/更新管线（M5）：从 zip 做 SHA256 验签（ROADMAP-13「校验发布物哈希」路径；完整代码签名是远期），
/// 解压到临时目录，校验 manifest（id / 契约版本 / minAppVersion），原子替换 plugins\&lt;Id&gt;\。
/// UI 插件（hasUi）更新后需重启应用（ADR-020 U4：WPF 钉住旧程序集）；后台插件可由 PluginManager 热重载。
/// </summary>
public sealed class PluginUpdater
{
    private readonly string _pluginsRoot;

    public PluginUpdater(string pluginsRoot) => _pluginsRoot = pluginsRoot;

    public PluginUpdateResult UpdateFromZip(string zipPath, string expectedSha256)
    {
        string? temp = null;
        string? target = null;
        string? backup = null;
        try
        {
            // 1. SHA256 验签（不匹配即拒绝，防篡改）
            if (!string.Equals(ComputeSha256(zipPath), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                return Fail("", "SHA256 校验失败：包哈希与预期不符（可能被篡改）");

            // 2. 解压到临时目录
            temp = Path.Combine(Path.GetTempPath(), "todo-plugin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(zipPath, temp);

            // 3. 校验 manifest
            var manifestPath = Path.Combine(temp, "manifest.json");
            if (!File.Exists(manifestPath)) return Fail("", "包缺少 manifest.json");
            var manifest = PluginManifest.Load(manifestPath);
            if (string.IsNullOrWhiteSpace(manifest.Id)) return Fail("", "manifest.id 为空");
            if (manifest.ContractVersion is { } cv && cv != PluginContract.Version)
                return Fail(manifest.Id, $"契约版本 {cv} != {PluginContract.Version}");
            if (!PluginManager.IsAppVersionCompatible(manifest.MinAppVersion))
                return Fail(manifest.Id, $"需要应用版本 >= {manifest.MinAppVersion}");

            // 4. 原子替换 plugins\<Id>\（旧目录 → 备份 → 新目录落位 → 删备份；失败则回滚）
            target = Path.Combine(_pluginsRoot, manifest.Id);
            Directory.CreateDirectory(_pluginsRoot);
            if (Directory.Exists(target))
            {
                backup = target + ".old-" + Guid.NewGuid().ToString("N");
                Directory.Move(target, backup);
            }
            Directory.Move(temp, target);
            temp = null;
            if (backup != null && Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);

            return new PluginUpdateResult(true, manifest.Id, manifest.Version, manifest.HasUi, null);
        }
        catch (Exception ex)
        {
            if (backup != null && target != null && Directory.Exists(backup) && !Directory.Exists(target))
            {
                try { Directory.Move(backup, target); } catch { /* 回滚尽力而为 */ }
            }
            return new PluginUpdateResult(false, "", "", false, ex.Message);
        }
        finally
        {
            if (temp != null && Directory.Exists(temp))
            {
                try { Directory.Delete(temp, recursive: true); } catch { /* 临时目录清理尽力而为 */ }
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static PluginUpdateResult Fail(string id, string error) => new(false, id, "", false, error);
}
