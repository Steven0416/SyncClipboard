# Windows、NAS 与 iPhone 配置（实验版）

先阅读 [限制和协议](lan-first.md)。当前不能替换已验证的正式版本作为唯一同步方式。

## Windows 构建与试用

需要 global.json 指定的 .NET SDK 10.0.302，以及支持 .NET 10 的 Visual Studio/Build Tools、Windows SDK、WinUI 构建组件。不要构建整个跨平台 solution。

```powershell
dotnet restore src/SyncClipboard.WinUI3
msbuild src\SyncClipboard.WinUI3\SyncClipboard.WinUI3.csproj /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:Configuration=Release /p:WindowsAppSDKSelfContained=true /p:SelfContained=true /v:m -restore
dotnet test src/SyncClipboard.Test --filter "TestCategory!=PlatformWindows&TestCategory!=PlatformMacOS&TestCategory!=PlatformLinux"
```

仅运行实际成功构建的目录中的应用；当前构建状态见 [记录](lan-first-validation.md)。不要覆盖原安装和用户配置。设置→服务器→LAN-first，先启用局域网直连；抄录 .local 名称、设备 ID、端口和 Token。Token 用于 iPhone 的 Authorization 请求头；不要截图或提交真实配对信息。

将家庭网络在 Windows 设置中设为“专用”仅应由用户确认可信后手动完成。默认没有符合条件的私有网络时服务保持离线。本实现不自动打开防火墙；可在管理员 PowerShell 中按需运行以下规则（应用路径、端口按实际安装修改）：

```powershell
New-NetFirewallRule -DisplayName 'SyncClipboard LAN HTTP' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5034 -Profile Private -Program 'C:\Apps\SyncClipboard\SyncClipboard.exe'
New-NetFirewallRule -DisplayName 'SyncClipboard LAN mDNS' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -Profile Private -Program 'C:\Apps\SyncClipboard\SyncClipboard.exe'
```

原生 DNS 服务可能由 Windows DNS Client 进程响应；若系统 mDNS 被策略或防火墙阻断，需要按系统策略排查，不能靠应用 TCP 放行解决。删除试用规则可执行 `Remove-NetFirewallRule -DisplayName 'SyncClipboard LAN HTTP'` 和对应 mDNS 规则。重置 Token 后旧快捷指令将得到 401，设备 ID 与主机名保持不变。

## NAS 升级与回滚

1. 记录当前官方镜像完整 tag/digest、容器参数、端口、UID/GID、目录挂载；停止容器后备份整个持久化目录和反向代理配置。不要在这里公开域名或凭据。
2. 本 Fork 的服务端源代码位于 `src/SyncClipboard.Server`。从仓库根目录执行 `docker build --platform linux/amd64 -f src/SyncClipboard.Server/Dockerfile -t syncclipboard-lan-first:experimental src` 构建自己的镜像；不要误用仍指向上游的 Docker Hub 镜像名。此命令尚未在本机执行。
3. 使用与原容器相同的持久化挂载和认证设置，在临时端口先测试；原 WebDAV/Profile API 和数据库迁移机制未修改。为新 API 配置同样的 HTTPS 反向代理，保持 SignalR/WebSocket 转发。
4. 通过 HTTPS + 原 Basic 认证请求 `/api/lan-first/v1/capabilities`，确认 `version=1`、`textRelay=true`。404 表示 NAS 仍是旧版，不能启用文本按需模式。
5. 保留 NAS 官方账号为当前活动账号，Windows 开启“NAS 文本按需同步”，刷新状态确认 `NAS on-demand connected`。用新 NAS 快捷指令请求 Windows 的实时文本；旧官方快捷指令读旧 Profile 时不能获得未上传文本。
6. 回滚：先在 Windows 关闭按需模式，再换回原镜像；必要时恢复停止容器时备份的持久化目录。旧快捷指令和原同步模式即可恢复。不要向远端 master 推送，也不要强制覆盖 Git 历史。

```powershell
dotnet publish src/SyncClipboard.Server/SyncClipboard.Server.csproj -c Release -o artifacts/server
```

DS220+ 镜像平台应为 linux/amd64；是否在 DS220+ 运行成功仍须实机验证。此分支没有自动推送 Docker 镜像、访问 NAS 或修改现有容器。

DDNS 必须指向当前公网地址；证书须覆盖实际公网域名并保持续期。公网统一使用 HTTPS，不把 Windows LAN 端口做路由器公网映射。iPhone 的 .local 目标直接指向 Windows，NAS 域名的内网 DNS 不作为 LAN 直连实现。若有反向代理路径前缀，保留现有前缀并把新增路径接在它后面。

## iPhone 快捷指令配置

先复制保存官方 [手动同步快捷指令](https://www.icloud.com/shortcuts/321944d62831416f9d2ba7917917686a)，保留原版可回退使用。以下是需要在 iPhone 编辑器实际创建、运行验证的步骤，不是已经测试过的 iCloud 分享成品。

首次配对只需固定填写 Windows 的 .local 名称、端口、Token、设备 ID，以及独立的 iPhone UUID。iPhone UUID 可在 Windows 用 `[guid]::NewGuid()` 生成一次并填入两个通道，之后切换网络不修改 IP。

创建“LAN 获取 Windows”：

1. URL 为 `http://<Windows稳定名称>:5034/api/lan-first/v1/clipboard`。
2. 生成新的请求 UUID（使用设备上可用的 UUID 动作；若系统版本没有该动作，不要用秒级时间戳冒充唯一 ID，需在真机选择可靠替代）。构造字典 requestId、sourceDeviceId、operation=`get`。
3. “获取 URL 内容”：POST，JSON 请求体为上述字典，头 `Authorization: Bearer <Token>`。
4. 解析回复字典，确认 status=`ok` 后才把 profile.text 复制到剪贴板。isCache=true 表示同请求 ID 的重放结果；要重新获取最新内容应产生新 ID。失败时不修改 iPhone 剪贴板。

创建“LAN 发送到 Windows”：

1. **先**“获取剪贴板”保存 iPhone 文本为变量，避免接下来的步骤改变它。
2. 使用相同 LAN URL 发送 get 请求，读取 profile.hash，但不复制回复文本到 iPhone。
3. 生成另一个请求 UUID，字典增加 operation=`set`、text=保存的 iPhone 文本、expectedHash=上一步 Hash，POST 到同一 URL。
4. 只有 status=`ok` 才显示发送完成。409 必须提示 Windows 内容已变化，不自动覆盖；超时不能声称发送成功，用户重试应保留原请求 ID／载荷以避免重复写入。

复制上述两个指令为“NAS 获取 Windows”和“NAS 发送到 Windows”：URL 改为 `https://<自己的NAS域名>/api/lan-first/v1/devices/<Windows设备ID>/clipboard`，认证头改为原 NAS Basic 认证（Base64 编码用户名和密码，Base64 不是加密，必须 HTTPS）。LAN Token 不发送给 NAS，NAS 凭据不放进 LAN 请求。

创建“同步剪贴板”入口，使用“从菜单中选取”：LAN 获取、LAN 发送、NAS 获取、NAS 发送，分别“运行快捷指令”。该入口没有可靠自动异常捕获承诺。LAN 请求失败可能直接终止动作链，应退出后选 NAS 操作；不能仅在失败动作后接“如果为空”就宣布自动回退成功。

首次请求在 iPhone 系统弹出局域网／网站访问权限时，用户按信任情况允许；权限的位置和提示随 iOS 版本变化，应记录实机版本。Apple 的[快捷指令隐私设置说明](https://support.apple.com/guide/shortcuts/adjust-privacy-settings-apd961a4fc65/ios)可用于核查授权。没有进行真机确认前，不宣称 .local 请求、UUID 动作或自动化后台运行已经可用。

## 故障排查

| 现象 | 检查 |
| --- | --- |
| LAN 服务显示无许可网络 | Windows 网络类别是否私有；不要为酒店网络盲目修改类别 |
| IP 可通、.local 不通 | UDP 5353、mDNS 系统服务、AP 组播限制；发现公告不等于主机解析 |
| 同 Wi-Fi 仍无法直连 | AP 客户端隔离、访客 VLAN、公司策略；选择 NAS 路径 |
| 401 | LAN Token 是否重置／NAS Basic 是否正确；不要把真实头部发到日志 |
| 409 | 目标内容变化或请求 ID 被复用；提醒用户选择当前希望保留的内容 |
| 503 | Windows 未连接、休眠、重启、NAS 未注册；不把缓存误标为实时 |
| 504 | 请求超时；可能已写入，使用相同 ID 重试，检查结果 |
| NAS 旧内容仍存在 | 正常：按需文本不会镜像旧存储 API；新快捷指令必须调用 v1 API |
| NAS 仍有写入 | 图片／文件原通道、手动上传、启用的历史同步仍可能写入 |

切换 Wi-Fi、唤醒、NAS 重启后需重新验证 LAN 地址／公告和 SignalR 注册状态。网络屏蔽互访时选择 NAS，无需反复手填局域网 IP。
