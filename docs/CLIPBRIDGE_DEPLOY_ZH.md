# ClipBridge 0.2：从全新 NAS、Windows 和 iPhone 开始

本指南适用于 `v0.2.0-preview.1`。下载入口：
[GitHub Release](https://github.com/Steven0416/SyncClipboard/releases/tag/v0.2.0-preview.1)。

你需要三个下载：Windows x64 ZIP、`ClipBridge-NAS-Kit-0.2.0-preview.1.zip`、
`ClipBridge-Relay-0.2.0-preview.1-linux-x64.tar.gz`。iPhone 快捷指令在 Windows ZIP 内，Release 也有单独下载。
NAS Kit 包含公开的 `compose.yaml`、`.env.example` 和本指南；**需要自己填写账号和地址**。
现有用户的配置、二维码和密码不在公开包中。

## 1. 先准备 NAS 与公网 HTTPS

推荐顺序：NAS 中继 → HTTPS → Windows → iPhone。NAS 和电脑可以在不同的家。
手机与电脑在同一局域网时直接通信；不在同网时，手机和电脑都通过公网 HTTPS 连接 NAS。

NAS 需要 Docker / Container Manager，DS220+ 可使用提供的 Linux x64 镜像。
其他架构参考 NAS Kit README 从源码构建。准备自己的域名、有效且受手机和 Windows 信任的证书，
以及能从外网访问的 HTTPS 端口。DDNS 只能跟踪地址，不能自动解决运营商 CGNAT 或端口不通的问题。
没有公网入站条件时，需要自行准备支持 HTTPS 和 WebSocket 的可达入口。

### 1.1 安装独立中继容器

1. 群晖安装 Container Manager。创建独立目录，例如 `/volume1/docker/clipbridge`。
   已有原版 SyncClipboard 时保留其容器、端口和数据，不能共用数据目录。
2. 按 Release 的 `.sha256` 核对镜像，完整上传到 NAS，从 Container Manager 的映像页面导入。
   应出现 `clipbridge-relay:0.2.0-preview.1`。
3. 解压 NAS Kit，将 `compose.yaml`、`.env.example` 放入上述目录。
   把 `.env.example` 复制为 `.env`，替换两个 `REPLACE_...` 占位值：
   一个专用用户名，一个密码管理器生成的长随机密码（建议至少 32 字符）。保留模板单引号。
   不使用 DSM 管理员账号密码。确认文件名不是 `.env.txt`。
4. Container Manager → Project / 项目 → Create / 新增：选择该目录、`compose.yaml`，
   项目名 `clipbridge`，启动。使用本地已导入镜像，不要求拉取公共镜像。
   若界面不能读取 `.env`，使用 Kit README 的 SSH 命令显式指定 `--env-file .env`。
5. 容器应保持运行。内部只映射 NAS 的 `127.0.0.1:8787`，因此暂时不能从手机直接访问该端口，这是预期配置。
   若 8787 已被占用，在 `.env` 修改 `CLIPBRIDGE_LOCAL_PORT`，下面代理目标端口也随之修改。

NAS Kit 的 Compose 使用相对目录 `./data` 持久化服务数据。
缺少用户名或密码会阻止 Compose 启动，但模板中的占位文字仍需你主动替换。
环境变量语法参见 [Docker 官方说明](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/)。

### 1.2 群晖反向代理与证书

以下用 `https://clip.example.com:8788/` 举例，**example.com 不是可使用的服务地址**，请全部换成自己的域名。

1. DSM → Control Panel / 控制面板 → Security / 安全性 → Certificate / 证书，安装域名对应的有效证书。
2. DSM → Control Panel → Login Portal / 登录门户 → Advanced / 高级 → Reverse Proxy / 反向代理，新增：
   - 名称：`ClipBridge`。
   - 来源：协议 **HTTPS**，主机名 `clip.example.com`，端口 `8788`（也可用未冲突的 443）。
   - 目标：协议 **HTTP**，主机名 `127.0.0.1`，端口 `8787`。
   - Custom Header / 自定义标题 → Create / 新增 → **WebSocket**，让 DSM 创建 Upgrade / Connection 标头。
   - 若界面提供目标 HTTP 版本，选择 HTTP/1.1。不要对该入口另加 DSM 登录门户认证或路径重写。
3. 在证书设置中，将该反向代理服务分配到正确证书，证书域名必须匹配。
4. 路由器仅把所选 HTTPS 端口转发到 NAS 对应端口；防火墙允许此入口。不要公开内部 8787 或 5033。
   使用外部端口与 NAS 来源端口不同时，客户端地址填写外部端口。
5. 外网浏览器访问 `https://自己的域名:端口/api/lan-first/v1/capabilities`，
   允许正常 HTTP Basic 登录提示并用 `.env` 的专用账号登录。
   成功返回包括 `version: 1`、`textRelay: true`、`offlineQueue: false` 的 JSON，且没有证书警告。
   不要把证书警告忽略后视为部署成功。

DSM 的菜单与 WebSocket 设置可参考
[群晖反向代理说明](https://kb.synology.com/en-my/DSM/help/DSM/AdminCenter/system_login_portal_advanced)和
[群晖 WebSocket 排查说明](https://kb.synology.com/en-global/Surveillance/tutorial/WebSocket_connection_fail_streaming_issue)。
不要填写 DSM 的管理地址；原版 SyncClipboard Server 的旧 HTTP 地址也不能代替这个新增中继。

## 2. 全新 Windows 电脑

要求 Windows 10 19041 或更新的 x64 系统。ZIP 已包含 .NET 和 Windows App SDK 运行时。

1. 下载 Windows ZIP，完整解压到固定目录，例如 `D:\ClipBridge`。不要在压缩包预览里运行。
2. 从 [Apple 官方页面](https://support.apple.com/en-la/106380)安装 Bonjour。
   在 Windows 服务中确认 Bonjour Service 已启动、启动类型为自动。它是后台服务，无需每天手动打开。
3. 退出这台电脑其他 SyncClipboard 实例，双击 `Setup.cmd`。
4. 在向导中选当前物理 Wi-Fi / 以太网网卡，确认它是你信任的局域网。
   填刚才测试通过的 **HTTPS 基地址**，例如 `https://clip.example.com:8788/`，
   不加 `/api/...`；输入 `.env` 中相同的专用用户名和密码。
5. 向导先验证中继能力，通过后生成此电脑独立身份、LAN 密钥与配对二维码。
   若 Windows 提示网络访问权限，对自己信任的网络允许，以供 iPhone 直连。
6. 向导询问登录启动时输入 `Y`。二维码窗口留着，下一步手机扫描。

以后运行 `Start.cmd`；设置登录启动后，登录 Windows 自动运行新版客户端。
不用另开原版 SyncClipboard，也不用手动打开 Bonjour。启用启动后不要随意移动解压目录。
`Pair.cmd` 可重新打开当前电脑的二维码，`DisableStartup.cmd` 移除此目录创建的登录启动项。

电脑选定网卡的局域网 IP 改变后会重新发布固定设备名称，手机不需要手动改 IP。
换成另一张物理网卡时需重新配置选择。局域网访问使用 HTTP 和独立密钥，限可信局域网使用；公网走 HTTPS。

## 3. 全新 iPhone（英文 Shortcuts 界面同样适用）

本版快捷指令已在用户的 iOS 27 与 Actions 环境验证；不需要手工搭建动作。

1. 安装 [Actions App](https://sindresorhus.com/actions)。保留此 App，它提供快捷指令需要的扩展动作。
2. 在 iPhone Safari 打开 Release，下载 `ClipBridge.shortcut`，点开文件，在 Shortcuts 中选择
   **Add Shortcut**。保留名称 **ClipBridge**，内部错误回调依赖这个名称。
   如 Safari 仅保存文件，可在 Files / 文件中点开它；不要将文件改成文本再复制。
3. 运行 **ClipBridge**，选择 **扫码配对**，按提示允许相机，扫描电脑 Setup / Pair 显示的二维码。
   配对信息写入 Actions 本机 Keychain。不要截图分享二维码、公开完整配对词典或打开 iCloud 钥匙串同步。
4. 首次通信按系统提示允许本地网络、连接自己的 NAS 域名和读取剪贴板；错误回调提示打开 Shortcuts 时允许。
   如果缺少权限，在系统设置对应 App 的权限页检查。不要输入 DSM 管理员密码。

只需要一个入口快捷指令 **ClipBridge**。无需创建 CB-Upload-Prepare、Pending 或任何辅助 CB-*。
一部手机当前只保存一台目标电脑；改用另一台电脑时扫描那台电脑的二维码。
配对后不依赖 Windows 的二维码窗口保持打开。

## 4. 首次验收：每次只做一个测试

完成一个再做下一个，避免并发任务干扰。

1. **同 Wi-Fi 上传**：电脑保持运行，iPhone 复制一段新文字，运行 ClipBridge →
   **发送当前剪贴板（新任务）**。提示“已通过 LAN 发送”后在电脑粘贴核对。
2. **同 Wi-Fi 下载**：电脑复制另一段文字，iPhone 运行 ClipBridge →
   **获取电脑剪贴板（新任务）**，提示成功后在手机粘贴核对。
3. **公网回退**：只关闭 iPhone Wi-Fi，保持蜂窝网络，电脑仍联网运行。
   手机复制新的文字并开始新发送；LAN 尝试失败后应提示通过 NAS 发送，电脑粘贴核对。
4. **恢复 LAN**：手机重新连接电脑所在 Wi-Fi，再复制新文字开始新发送，应恢复通过 LAN。

每个新任务先尝试 LAN（网络请求超时约 4 秒），符合回退条件时尝试 NAS（约 15 秒）。
名称解析、系统调度或回调切换可能增加等待时间。认证失败、数据不一致或写入冲突会停止，不能当作成功继续。
两路都失败后可选“继续上次任务”，它重试**先前保存的文字**；要发送新内容须选择新任务。

## 5. 日常使用与限制

- 手机发送：复制文字 → ClipBridge → 发送当前剪贴板。电脑接收后直接粘贴。
- 手机接收：电脑复制文字 → 手机 ClipBridge → 获取电脑剪贴板 → 粘贴。
- 电脑必须在线、客户端运行且未睡眠。NAS 只转发到在线电脑，`offlineQueue=false`，不提供离线排队。
- 目前自动选路覆盖文字；不承诺图片、文件或手机后台常驻自动同步。快捷指令需要主动触发。
- 多台电脑可分别安装，生成各自设备身份。手机切换目标需重新扫码，不会自动选择附近电脑。
- 本版本为预览版，LAN → 蜂窝 NAS → LAN 已有实机验证，不能替代所有路由器和 NAS 的兼容性验证。

## 6. 常见问题

| 现象 | 处理 |
| --- | --- |
| Setup 无法验证中继 | 用 HTTPS capabilities 地址检查证书、反向代理和版本；不要用 DSM 管理端口或旧 5033 服务。 |
| 401 | 核对 `.env` 与电脑配置的中继账号；改密码后重建容器环境、重配电脑并重新扫码。不要发送密码排查。 |
| 404 | 常见于旧服务、错误端口或路径重写，检查本版 capabilities 接口。 |
| NAS 503 / device-offline | 电脑需在线且客户端已连接同一个 NAS；检查代理 WebSocket、证书、电脑网络和设备身份。 |
| LAN 超时但 NAS 正常 | 检查 Bonjour Service、选择的物理网卡、防火墙、同网互访和访客 Wi-Fi 隔离。无需更改公网 NAS。 |
| 在家 HTTPS 可用，蜂窝不可用 | 检查 DDNS、公网端口转发、防火墙与 CGNAT；必须从外网实际测试。 |
| 在家无法访问自己的公网域名 | 路由器可能不支持 NAT 回环，需要路由器内网 DNS 或可用回环配置；电脑也必须能连接 HTTPS NAS。 |
| 手机上提示 choose a value / 动作缺失 | 确认安装并启用了 Actions，再导入本 Release 的 ClipBridge，勿使用旧测试生成版本。 |
| 收到旧文字 | “继续上次任务”使用保存内容；要发送当前剪贴板请选择“新任务”。 |

## 7. 备份、更新与可删除的测试项

NAS 私有备份：`.env` 和整个 `data/`。不要公开它们。
Windows 私有备份：`client/SyncClipboard.json`、`StaticConfig.json`、`appdata`、`pairing-private`（若存在）。
二维码和 `RelayConnection.private.json` 都含认证信息。分享给别人请只分享原始 Release ZIP。

新电脑应从 Setup 生成新身份；不要把旧电脑整个配置复制后让两台电脑同时以同一个设备身份运行。
自己的 NAS 账号可手工输入，或私下传递 `RelayConnection.private.json` 给新电脑 Setup 使用。
更新程序时先退出客户端，保留上述私有配置；NAS 换镜像前先备份，不删除数据卷，不覆盖原版服务。

手机确认新版正常后，**保留 ClipBridge 和 Actions**，可以删除本项目所有旧 `CB-*` 测试/预览/诊断快捷指令，
包括 CB-配对、检查配对、局域网测试、LAN/NAS Read/Upload、Upload-Prepare、Upload-Pending、
Upload-Auto-Preview，以及旧“局域网连接测试”和临时 Keychain 测试。
不要清除 Actions 内 `clipbridge-pairing-v1` 钥匙串；删除旧快捷指令本身不影响配对。
