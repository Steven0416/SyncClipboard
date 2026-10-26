# ClipBridge 0.2.0-preview.1 · Windows x64

包含 Windows 客户端、.NET/Windows App SDK 运行时、本地配置与二维码向导，以及已在 iPhone 实测的单入口快捷指令。
文字传输在每次新任务先使用 LAN，不可达时回退到 HTTPS NAS，回到局域网后再优先 LAN。
需要主动运行手机快捷指令；不包含 iPhone 后台监控、图片/文件自动选路或多电脑自动择优。

## 新电脑首次使用

1. 将整个 ZIP 解压到固定目录，例如 `D:\ClipBridge`，不要从 ZIP 预览窗口直接运行。
2. 安装或确认 Apple Bonjour，且 Bonjour Service 正在运行。
   [Apple 官方下载页](https://support.apple.com/en-la/106380)。Bonjour 为独立依赖，未随包重新分发；无需手动打开它。
3. 退出这台电脑上其他正在运行的 SyncClipboard，双击 `Setup.cmd`。
4. 选择真实 Wi-Fi/有线网卡，只确认自己信任的局域网。填写支持 ClipBridge v1 的 **HTTPS 中继基地址**及账号。
   这是已升级的独立中继入口，不是旧原版的 HTTP 5033，也不是 DSM 管理页面。
   或者输入你从已配置电脑取得的 `RelayConnection.private.json` 文件路径，复用中继连接信息。
   向导会认证检查中继能力，再为此电脑生成独立设备编号、名称与 LAN 密钥，不改 NAS 服务设置。
5. 手机安装 Actions App，导入包内 `ClipBridge.shortcut`（也可从同一 GitHub Release 单独下载）。
   保留快捷指令名称 **ClipBridge**，运行后选“扫码配对”，扫描电脑弹出的二维码。
6. 向导询问是否登录 Windows 自动启动时，输入 `Y` 即启用；Windows 首次询问网络访问时，
   只对你信任的网络允许。无需关闭防火墙或把所有网络改成专用。

二维码保存在 `client\pairing-private\pairing.png`，含认证凭据，请勿分享到聊天或 GitHub。
`Pair.cmd` 可在本机重新打开二维码，保留此电脑现有身份与账号。
同一部手机当前只保存一个目标电脑；换电脑时对新电脑重新扫码，回到原电脑也可用其二维码重新配对。

## 日常启动与传输

- 双击 `Start.cmd`。若设置了登录启动，下次登录 Windows 自动启动，不需要另开原版或 Bonjour。
- iPhone → 电脑：先复制文字，运行 ClipBridge，选择“发送当前剪贴板（新任务）”。
- 电脑 → iPhone：电脑复制文字，运行 ClipBridge，选择“获取电脑剪贴板（新任务）”，随后粘贴。
- 两路都失败后，“继续上次任务”重试原先保存的文字；新任务会替换旧任务。一次只运行一个任务。
- 自动名称验证每 30 秒运行。发现记录丢失、Bonjour 断开或网卡变化后重新发布，HTTP 服务保持可用。
  若 Bonjour 尚未启动，客户端会继续重试；网卡/IP 变化不会改变设备身份，手机无需改地址。
- `DisableStartup.cmd` 只移除此解压目录创建的登录启动项。启用启动后不要移动整个目录。

## 可删除的手机测试快捷指令

**只需保留新版 `ClipBridge`。**它没有调用旧 CB-* 快捷指令的依赖。
可以删除本项目建立的这些测试项及其复制/旧预览版本：

- `CB-Import-Test`、`CB-LAN-Test`、`CB-Fallback-Test`
- `CB-配对`、`CB-检查配对`、`CB-局域网测试`
- `CB-LAN-Read`、`CB-NAS-Read`、它们的 Extended 版本、`CB-Auto-Read`
- `CB-LAN-Upload`、`CB-NAS-Upload`
- `CB-Upload-Prepare`、`CB-LAN-Upload-Pending`、`CB-NAS-Upload-Pending`（若有）
- `CB-Upload-Auto-Preview` 及其所有旧预览/诊断副本
- 为本项目建立的旧“局域网连接测试”、临时 Keychain 读写测试

保留 Actions App，不要清除其本机配对钥匙串 `clipbridge-pairing-v1`。
删除旧快捷指令本身不会删除该配对。旧 `clipbridge-upload-pending-v1` 已不再被新版入口使用。

## 升级现有 0.2 预览电脑

先退出客户端，把**整个新包的 `client` 程序文件**覆盖到现有 0.2 预览目录，保留旧目录中的
`SyncClipboard.json`、`StaticConfig.json`、`appdata` 和 `pairing-private`。
新 ZIP 不含用户配置，不需要重设账号或重新配对；不要用本版本覆盖原版 5033 的目录。
若改为全新目录，应先迁移自己的上述私有文件，再调整登录启动项到新位置。

## 迁移中继连接信息

`client\pairing-private\RelayConnection.private.json` 包含中继账号密码。
可私下传到自己的另一台电脑，在 Setup 地址提示处输入其路径，新电脑会生成自己的设备身份和二维码。
不要把这份文件上传 GitHub、发在聊天里，或将已配置目录重新压缩公开分享。

## 依赖、许可与验证范围

支持 Windows 10 19041 及之后的 x64 桌面系统；本包自包含 .NET 10 和 Windows App SDK。
需要可访问的 HTTPS ClipBridge 0.2 中继，旧原版 SyncClipboard Server 不提供该协议。
同网仍需允许设备互访；访客隔离、电脑离线/睡眠时不能直连。NAS 仅在目标电脑在线时中继，不是离线队列。

已验证：手机 LAN 上传、蜂窝 NAS 回退、回到 Wi-Fi 后恢复 LAN；本版补充名称记录丢失自动恢复测试。
完整重启、真实 DHCP 重新分配和所有路由器环境不以短时测试替代。当前为可下载预览版。

SyncClipboard 原作者 JericX，MIT。Fork 源码：https://github.com/Steven0416/SyncClipboard
QRCoder 为 MIT 许可，包含于配置向导；Apple Bonjour 单独安装。
`SHA256SUMS.txt` 记录本包文件校验值。
