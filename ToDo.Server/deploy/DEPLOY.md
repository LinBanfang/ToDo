# To Do 同步服务器部署指南

自建同步服务器部署到已有 Ubuntu/Debian VPS,HTTPS 用 Caddy + Let's Encrypt 自动签证书。
部署产物 = 自包含 linux-x64 可执行文件,**VPS 不需要装 .NET**。

## 部署拓扑

```
WPF 客户端 (Windows)                    VPS (Ubuntu/Debian)
┌────────────────────┐   HTTPS 443    ┌──────────────────────────────┐
│  设置→同步 填地址密钥 │ ────────────▶ │ Caddy (Let's Encrypt 自动证书) │
│  POST /api/sync     │                │      │  reverse_proxy        │
└────────────────────┘                │      ▼  http://127.0.0.1:5080 │
                                      │  ToDo.Server (systemd 服务)    │
                                      │      │  SQLite                │
                                      │      ▼ /var/lib/todo-sync/sync.db│
                                      │  /etc/todo-sync.env (SYNC_KEY)  │
                                      └──────────────────────────────┘
```

- 服务只监听 `127.0.0.1:5080`,**公网不暴露**,所有流量走 Caddy HTTPS
- 认证 = 共享同步密钥 `X-Sync-Key` 头(固定时间比较);无账号系统,单用户
- 数据库文件在 `/var/lib/todo-sync/sync.db`,WAL 模式

---

## 0. 前提检查(3 分钟)

1. **域名已解析**:`ping <你的域名>` 返回 VPS 公网 IP。
2. **Windows 本机有**:.NET SDK(9+)、SSH 客户端(Win11 自带 OpenSSH)。**不需要 rsync** —— `deploy.sh` 自己打包 + 增量传输。
3. **VPS 能连**:`ssh root@<IP>` 能登录;已把域名加进 Caddy 的站点会更好(不是必须,deploy.sh 会自动追加)。
5. **SSH 免密(强烈建议)**:`deploy.sh` 会调用多次 ssh,没配密钥要输好几遍密码。配一次:
   ```bash
   ssh-keygen -t ed25519          # 没有密钥就生成(一路回车)
   ssh-copy-id root@<IP>          # 把公钥推上去,之后 ssh 不用再输密码
   ```

## 1. 配置部署目标(本机,不进 git)

**不要在 deploy.sh 里填真实 IP/域名** —— 那是提交到仓库的文件。在 `ToDo.Server/deploy/` 下新建一个 **`deploy.local`**(已被 .gitignore 排除,只存在于你本机):

```bash
HOST="root@1.2.3.4"             # VPS 的 SSH 目标
DOMAIN="sync.your-domain.com"   # 指向 VPS 的子域名
```

`deploy.sh` 启动时会自动读取它;没有这个文件就用占位符默认值并报错让流程失败而不是误传。

## 2. VPS 一次性准备(只在第一次做)

SSH 到 VPS,执行:

```bash
# 装 Caddy(官方源,apt 自带的版本太旧)
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update && sudo apt install -y caddy rsync openssl

# 开防火墙(80/443 给 Caddy,SSH 别锁死自己)
sudo ufw allow OpenSSH
sudo ufw allow 80,443/tcp
sudo ufw enable
```

确认 Caddy 在跑:`systemctl status caddy`(绿色 active)。

## 3. 一键部署(Windows 本机执行)

```bash
cd d:\Dev\Code\ToDo
./ToDo.Server/deploy/deploy.sh
```

脚本做 5 件事:

1. `dotnet publish` → linux-x64 自包含产物(本机需能访问 NuGet,首次会拉 runtime pack)
2. 二进制打成**一个 tar.gz** 传 `/opt/todo-sync/`(**增量**:只传 md5 变化的文件,重部署通常几个 KB);服务单元、Caddyfile 拷到 `/tmp`
3. 生成同步密钥,写入 `/etc/todo-sync.env`(**已存在则不覆盖**,密钥永久稳定)
4. 安装 systemd 单元并启动 `todo-sync`
5. 把 Caddy 反向代理块追加进 `/etc/caddy/Caddyfile` 并 reload

结束后脚本会打 health check,看到 `{"status":"ok"}` 即成功。

## 4. 验证

```bash
# ① 存活检查
curl https://<你的域名>/healthz
#   期望: {"status":"ok","protocolVersion":1}   (protocolVersion 由客户端用于"版本不符"检测)

# ② 认证:错误密钥必须 401
curl -i -X POST https://<你的域名>/api/sync \
  -H "X-Sync-Key: wrong-key" \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"t","since":0,"changes":[]}'
#   期望: HTTP/1.1 401 Unauthorized

# ③ 推送一条真实数据(正确密钥)
curl -i -X POST https://<你的域名>/api/sync \
  -H "X-Sync-Key: $(ssh root@<IP> 'sudo cat /etc/todo-sync.env | cut -d= -f2')" \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"t1","since":0,"changes":[{"type":"task","id":"smoke-test","modifiedAt":1,"deleted":false,"payload":"{\"Id\":\"smoke-test\",\"Title\":\"ok\"}"}]}'
#   期望: HTTP/1.1 200,body 里 serverSeq:1
#   再跑一次 with since:1 → 应返回空 changes(增量游标生效)
```

> 首次 HTTPS 证书签发要等 30~60 秒(Let's Encrypt),`curl` 报证书错就等一会儿再试。

## 5. 取出同步密钥(客户端要填)

```bash
ssh root@<IP> "sudo cat /etc/todo-sync.env"
#   形如: SYNC_KEY=ab12cd34ef56...
```

把等号后面的值记下来。**这是一切数据的钥匙,别外泄;换密钥会强制所有设备重新同步。**

## 6. WPF 客户端配置

1. 打开应用 → 设置 → **同步**
2. 打开「启用多设备同步」开关
3. **服务器地址**填:`https://<你的域名>`(不带路径、不带斜杠)
4. **同步密钥**填:第 5 步的值
5. **设备 ID** 自动生成,不用填(每台设备一个)
6. 点「立即同步」→ 状态变「已同步 · 上次同步 …」即成功

第二台设备重复第 6 步即可(设备 ID 各自不同)。两台都建数据后互相同步。

## 7. 日常运维

| 操作 | 命令 |
|---|---|
| 看运行状态 | `ssh root@<IP> "systemctl status todo-sync"` |
| 看实时日志 | `ssh root@<IP> "journalctl -u todo-sync -f"` |
| 重部署新版本 | 改完代码后在本机重跑 `./ToDo.Server/deploy/deploy.sh`。**只传变化的文件(通常几个 KB),快得很**;密钥不覆盖,数据不丢 |
| 备份数据库 | `ssh root@<IP> "sudo cp /var/lib/todo-sync/sync.db ~/sync-$(date +%F).db"`(拷走即可) |
| 停/启 | `systemctl stop/start todo-sync` |

**更新注意**:
- **数据库结构变化跟上传无关** —— 数据库在服务器 `/var/lib/todo-sync/sync.db`,deploy.sh 只更新程序文件,从不碰数据目录。
- 首次部署传全量(~47MB 压缩包),以后**只传变化的文件**。想强制全量重传一次,删掉本机的 `~/.todo-sync-publish-manifest.md5` 再跑 deploy.sh 即可。
- WPF 客户端升级走应用自带的更新通道,与服务器上传完全无关(客户端更新不用动服务器)。

## 8. 常见问题

| 症状 | 排查 |
|---|---|
| `curl https://域名/healthz` 连不上/超时 | 防火墙没放行 80/443;或证书还没签好(等 1 分钟再试) |
| 返回 `502 Bad Gateway` | Caddy 在但后端没起来:`systemctl status todo-sync`;或 Caddyfile 没追加成功(见下) |
| 客户端同步状态「同步失败」 | 服务器地址/密钥填错;或服务端日志 `journalctl -u todo-sync -n 50` 看报错 |
| 客户端「同步密钥被拒绝(401)」 | 密钥与 `/etc/todo-sync.env` 不一致(复制时别带 `SYNC_KEY=` 前缀,别带空格) |
| 客户端状态**红色「服务器版本不符」** | 服务器跑的协议版本比客户端旧(或新)。在 VPS 上确认 `curl https://域名/healthz` 的 `protocolVersion` 是否等于客户端期望值;重跑 `deploy.sh` 部署最新版(增量上传,只传变化的文件) |
| 域名已经有网站 | `deploy.sh` 只在 Caddyfile 里**没出现过该域名**时才追加。若你已有该域名的 site 块,需手动把 `reverse_proxy 127.0.0.1:5080` 加进去,再 `systemctl reload caddy` |
| Caddyfile 被追加了重复块 | 每个 site 块只能有一个域名头,检查 `/etc/caddy/Caddyfile` 有无重复的 `你的域名 {`,删掉旧的再 reload |

---

## 附录 A:增量上传是怎么工作的

`deploy.sh` 每次发布后在本地记一份文件清单(`~/.todo-sync-publish-manifest.md5`,每个文件的 md5)。

- **首次部署**:没有清单 → 全量传(打包成单个 ~47MB tar.gz)。
- **以后部署**:对比新发布和上次清单 → **只传 md5 变化的文件** + 删除服务器上已不存在的文件。47MB 里 99% 是永远不会变的 .NET 运行时,所以重部署通常只传几个几百 KB 的 DLL。
- **想强制全量重传**:删除本机 `~/.todo-sync-publish-manifest.md5` 再跑一遍即可。

## 附录 B:先本地跑通再上 VPS(可选,推荐)

不想直接动服务器,可先在 Windows 本地把全链路跑一遍:

```bash
# 终端 1:起服务
cd d:\Dev\Code\ToDo
SYNC_KEY=test dotnet run --project ToDo.Server

# 终端 2:验证
curl http://localhost:5080/healthz
```

然后在应用设置里填 `http://localhost:5080` + 密钥 `test`,开两台实例(复制一份 exe 目录)互相同步。
本地通后再部署到 VPS,客户端把地址改成 `https://<域名>` 即可。
