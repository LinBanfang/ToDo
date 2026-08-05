# ADR-005: GitHub Actions 自动发布 + Gitee 同步

## 状态
已采纳

## 背景
手动打包、上传 Release 繁琐且易错。需要一键发版，并让国内用户能通过 Gitee 获取更新。

## 决策
- `.github/workflows/release.yml` 在推送 `v*` tag（或手动触发）时：构建自包含 win-x64 → 打包 zip → 创建 GitHub Release（自动生成说明）。
- 同一步骤把 zip 同步到 Gitee：先确保 tag 存在（`POST /tags`，从 master 创建），再创建 release（表单参数、必带 `body`），最后上传 zip。
- 一开始用 `YuZhiYuanOrg/release-sync`，但其资产上传有 bug（原生 FormData 无 `getHeaders()` 且异常被吞）；改为 PowerShell 直连 Gitee API，失败显红。

## 后果
- 优点：`git tag && push` 即完成 GitHub + Gitee 双端发布。
- 权衡：Gitee 需要 `GITEE_TOKEN` secret 且 tag 需存在；Gitee 单资产 100MB 上限；Gitee 的 `releases/tags/{tag}` 带 token 返回 null（用列表端点规避）。
- 版本号由 csproj `<Version>` 驱动，应用更新检测依赖它。
