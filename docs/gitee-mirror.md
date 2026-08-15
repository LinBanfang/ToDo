# Gitee 镜像手动同步（Claude 执行手册）

> **背景**：从 v1.3.2 起，[release.yml](../.github/workflows/release.yml) 不再自动同步 Gitee。
> 原因是 CI 传 Gitee 的 `curl` 无超时，Gitee 抖动时作业会**永久挂起**（v1.3.1 实测挂 50+ 分钟，
> 占满 GitHub Actions Windows runner 分钟）。为彻底规避，改为：
> **GitHub 发布走 CI 自动完成；Gitee 镜像（tag / release 正文 / zip 附件）由 Claude 在会话中手动完成。**
>
> 本文档是**给全新会话**的操作手册：按顺序执行、每步有可复制命令与验证点，照着做即可。

## 1. 何时执行

每次 GitHub Release 创建成功后（CI 会自动完成，无需干预）：
- 确认入口：`https://github.com/LinBanfang/ToDo/releases` 出现 `vX.Y.Z`，且附件 `ToDo-vX.Y.Z.zip` 已上传。
- 若该版本需要 Gitee 镜像（通常都要），执行本文档剩余步骤。

## 2. 前置：Gitee PAT（唯一外部凭据）

- GitHub Actions secrets 里的 `GITEE_TOKEN` **读不到**，每次同步都要向用户现要一个。
- 请用户：Gitee → 设置 → 私人令牌 → 新建**临时**令牌，权限勾 **`projects`**（仓库读写）即可，用完撤销。
- 请用户把令牌存到本地文件（如 `D:/work/Tmp/gitee_token.txt`）。**从文件读取、不回显**，用完删除文件并提醒用户撤销。
- 读取（去掉可能的换行 / 空格）：
  ```bash
  TOKEN=$(tr -d '\r\n ' < /d/work/Tmp/gitee_token.txt)
  ```

## 3. 固定变量（在仓库根目录执行）

```bash
GITHUB_OWNER=LinBanfang
GITEE_OWNER=wu-bin-921
REPO=ToDo
TAG=vX.Y.Z   # ← 本次版本号，替换为实际值
```

## 4. 步骤

### 4.1 下载 zip（GitHub 匿名可下，无需认证）

```bash
curl -sSL --max-time 600 -o /d/tmp/ToDo-$TAG.zip \
  "https://github.com/$GITHUB_OWNER/$REPO/releases/download/$TAG/ToDo-$TAG.zip"
```
验证大小与 GitHub Release 附件一致（v1.3.1 为 75,683,511 字节）：
```bash
ls -la /d/tmp/ToDo-$TAG.zip
```

### 4.2 确保 Gitee tag 存在（幂等；已存在时报错可忽略）

```bash
curl -sS --connect-timeout 15 --max-time 60 -X POST \
  "https://gitee.com/api/v5/repos/$GITEE_OWNER/$REPO/tags?access_token=$TOKEN" \
  -d "tag_name=$TAG" -d "refs=master"
```
> 已存在会返回错误信息，忽略即可；重点是确保 tag 已在 Gitee 上（创建 release 需要）。

### 4.3 从 CHANGELOG 提取正文

规则：release 正文 = CHANGELOG 对应 `## [vX.Y.Z]` 段（到下一个 `## [v` 之前），与 GitHub Release 一致。
```bash
python -X utf8 -c "
lines=open('CHANGELOG.md',encoding='utf-8').read().splitlines()
start=next(i for i,l in enumerate(lines) if l.startswith('## [$TAG]'))
end=next((i for i in range(start+1,len(lines)) if lines[i].startswith('## [v')), len(lines))
open(r'/d/tmp/gitee_body.md','w',encoding='utf-8',newline='').write('\n'.join(lines[start:end]))
print('正文已提取：行', start+1, '-', end)
"
```

### 4.4 创建 release → 找出 release id → PATCH 刷新正文

```bash
# ① 创建（已存在则报错忽略）
curl -sS --connect-timeout 15 --max-time 60 -X POST \
  "https://gitee.com/api/v5/repos/$GITEE_OWNER/$REPO/releases?access_token=$TOKEN" \
  -d "tag_name=$TAG" -d "target_commitish=master" \
  --data-urlencode "name=Release $TAG" \
  --data-urlencode "body@/d/tmp/gitee_body.md"

# ② 找 release id（带 token 时 /releases/tags/{tag} 返回 null，必须用列表）
RID=$(curl -sS --connect-timeout 15 --max-time 60 \
  "https://gitee.com/api/v5/repos/$GITEE_OWNER/$REPO/releases?per_page=100&access_token=$TOKEN" \
  | python -X utf8 -c "import json,sys; rs=json.load(sys.stdin); print(next(r['id'] for r in rs if r['tag_name']=='$TAG'))")
echo "release id = $RID"

# ③ PATCH 刷新正文（幂等；必须带 tag_name，否则 Gitee 返回 'tag_name is missing'）
curl -sS --connect-timeout 15 --max-time 60 -X PATCH \
  "https://gitee.com/api/v5/repos/$GITEE_OWNER/$REPO/releases/${RID}?access_token=$TOKEN" \
  -d "tag_name=$TAG" \
  --data-urlencode "name=Release $TAG" \
  --data-urlencode "body@/d/tmp/gitee_body.md"
```

### 4.5 上传 zip（关键：超时 + token 放查询串）

```bash
curl -sS --connect-timeout 15 --max-time 600 -o /d/tmp/up.json -w "HTTP_CODE:%{http_code}\n" \
  -X POST "https://gitee.com/api/v5/repos/$GITEE_OWNER/$REPO/releases/$RID/attach_files?access_token=$TOKEN" \
  -F "file=@/d/tmp/ToDo-$TAG.zip"
```
验证：`HTTP_CODE:201`，响应含 `"name":"ToDo-$TAG.zip"` 且 `size` 与本地一致。

### 4.6 清理旧附件（Gitee 免费附件配额 1GB，约 14 个 self-contained zip 占满；保留最新 3 个）

```bash
TOKEN="$TOKEN" python -X utf8 <<'PY'
import json, subprocess, os
base = 'https://gitee.com/api/v5/repos/wu-bin-921/ToDo'
tok = os.environ['TOKEN']
def g(u): return json.load(subprocess.check_output(['curl', '-sS', '--max-time', '60', u]))
rels = g(f'{base}/releases?per_page=100&access_token={tok}')
keep = sorted(r['id'] for r in rels)[-3:]
for r in sorted(rels, key=lambda x: x['id'], reverse=True):
    if r['id'] in keep: continue
    for a in g(f'{base}/releases/{r["id"]}/attach_files?access_token={tok}'):
        if a['name'].startswith('ToDo-'):
            subprocess.run(['curl', '-sS', '--max-time', '60', '-X', 'DELETE',
                f'{base}/releases/{r["id"]}/attach_files/{a["id"]}?access_token={tok}'])
            print('pruned', r['tag_name'], a['name'])
print('prune done')
PY
```
> 该步是配额维护，可稍后做，不影响本次发布。

### 4.7 验证（公开 API，无需 token）

```bash
curl -s "https://gitee.com/api/v5/repos/$GITEE_OWNER/$REPO/releases/tags/$TAG" | python -X utf8 -c "
import json,sys
r=json.load(sys.stdin)
print('release id:', r.get('id'), '| tag:', r.get('tag_name'))
print('body 开头:', (r.get('body') or '')[:60].replace(chr(10),' '))
for a in r.get('assets',[]): print('asset:', a.get('name'))
"
# 再确认 zip 可下载：200 + Content-Length 与本地文件一致
curl -sIL --max-time 60 "https://gitee.com/$GITEE_OWNER/$REPO/releases/download/$TAG/ToDo-$TAG.zip" | grep -iE '^(HTTP|content-length)'
```

### 4.8 清理

- 删除本地临时文件：`/d/tmp/ToDo-$TAG.zip`、`/d/tmp/gitee_body.md`、`/d/tmp/up.json`
- 删除 token 文件（如 `/d/work/Tmp/gitee_token.txt`）
- **提醒用户撤销临时 PAT**

## 5. 踩过的坑（务必遵守）

| 坑 | 后果 | 对策 |
|---|---|---|
| curl 无超时 | Gitee 抖动时永久挂起（v1.3.1 挂 50+ 分钟） | 一律 `--connect-timeout 15 --max-time 600`；上传实测 ~400s，**别把慢当挂** |
| token 放进 `-F` form 字段 | Gitee 返回 400 | token 放 **query string**（`?access_token=$TOKEN`） |
| PATCH 不带 `tag_name` | Gitee 返回 "tag_name is missing" | PATCH 必须带 `tag_name` |
| URL 写 `$RID?access_token=` | 被 shell 解析成空变量 → 401 | 用 `${RID}?access_token=` |
| 带 token 调 `/releases/tags/{tag}` | 返回 null | 找 id 用 `/releases?per_page=100` 列表 |
| Gitee 偶发 DNS / 网络抖动 | 偶发失败 | 对每个调用做指数退避重试（3s 起，最多 5 次）；失败时保留 HTTP 码与响应体便于诊断 |
| token 落盘 / 进对话 | 泄露风险 | 只从本地文件读、不回显、用完删文件 + 提醒撤销 |

## 6. Windows / PowerShell（pwsh）执行注意

> 本机 DSH 的实际执行环境是 **Windows + pwsh 5.1**（不是 bash）；用 bash 可跳过本节。以下坑都在 pwsh 下实测踩过。

### 6.1 变量后接 `?` 被解析成变量名 → 401

`"$base/releases/$rid?access_token=$tok"` 里，PowerShell 把 `$rid?access_token` 整体当成变量名（空），URL 变成 `.../releases/=TOKEN`，token 被吞 → 401「登录失败，无权限访问该资源」。这与第 5 节 bash 的 `$RID?access_token=` 同源，但根因不同：PowerShell 变量名可含 `?`。

**对策**：用 `${rid}` 显式分隔变量名：

```powershell
$url = "$base/releases/${rid}?access_token=$tok"
```

### 6.2 curl 读文件双重编码 → 中文乱码

`curl.exe --data-urlencode "body@file"` 在 Windows 上把 UTF-8 文件按 Latin-1 读，`修`（UTF-8 `E4 BF AE`）被存成 `ä¿®`（U+00E4 U+00BF U+00AE），正文长度翻倍、中文全乱。

**对策**：先 `[Uri]::EscapeDataString` 预编码成**纯 ASCII** form 体，再 `--data-binary` 发送（全程无非 ASCII 字节，不再触发任何字符集转换）：

```powershell
# $body = 从 CHANGELOG 提取的 vX.Y.Z 正文（正确 Unicode 字符串）
$formBody = "tag_name=$TAG&name=Release%20$TAG&body=" + [System.Uri]::EscapeDataString($body)
[System.IO.File]::WriteAllText("$dir\form.txt", $formBody, (New-Object System.Text.ASCIIEncoding))
curl.exe -sS -X PATCH $url -H "Content-Type: application/x-www-form-urlencoded" --data-binary "@$($dir.Replace('\','/'))/form.txt"
```

### 6.3 PowerShell 5.1 文件编码坑（连带）

- `Set-Content -Encoding UTF8` 会写 **BOM**（正文开头多出 `锘?`）；`Get-Content -Raw` 默认按系统 GBK 读，读 UTF-8 文件会乱码。
- 写无 BOM UTF-8 用 `[IO.File]::WriteAllText($p,$s,(New-Object Text.UTF8Encoding($false)))`；读用 `[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)`。
- `Invoke-RestMethod` 在 5.1 下显示中文乱码**不代表 Gitee 存错**——校验正文应 `curl.exe -o` 抓原始字节，确认含 `修` 的 UTF-8 字节 `E4 BF AE`（而非双重编码的 `C3 A4 C2 BF C2 AE`）。

## 7. 分工

- **CI（release.yml）只做**：构建 → 打包 → 提取 CHANGELOG → 创建 GitHub Release + 上传 zip 附件。
- **Claude 手动做**：Gitee tag / release 正文 / zip 上传 / 附件配额清理。
- 手动同步凭据 = 用户临时提供的 Gitee PAT，**不落盘、不进对话、用完撤销**。
- 同步完成后在会话里汇报：正文一致 + zip 可下载（HTTP 200 / 字节数匹配）。
