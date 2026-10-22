# 0.2 自动选路开发记录

2026-10-22。目标是在同一对设备间自动优先直连 LAN，失败后经 NAS 中继，网络恢复后重新直连。iPhone 继续使用快捷指令和 Actions；用户报告 iOS 27，并能找到 Get Contents of URL (Extended)。这不是后台自动读取 iPhone 剪贴板的承诺，操作入口仍是上传/下载。

## 已完成的代码

- `LanFirstRequestRouter`：v1 协议客户端，单次顺序请求，LAN 超时 2 秒，NAS 超时 15 秒；每次操作重新尝试 LAN，不按 SSID 判断、不永久粘住 NAS。
- 可回退：连接错误、请求超时、HTTP 408/502/503/504。不会同时向两个端点写入。
- 不回退：用户取消、401/403、冲突、限流、其他明确失败、重定向、无效 JSON、错误设备或请求编号。失败不冒充成功、不覆盖调用者剪贴板。
- 两个独立 HttpClient；LAN 不使用系统代理；生产工厂禁止重定向。LAN Bearer 与 NAS Basic 分开配置，不放入 URL；NAS 要求 HTTPS。
- 同一次传输回退时保留完整 LanRequest，包括 requestId、text、expectedHash；不能因为 LAN 回复丢失而重新获取 Hash 强制覆盖。已有桥接层保存成功回执，NAS 重试得到缓存结果，后续本机复制不会被重复写入覆盖。
- 集成 LAN 服务改用已在 0.1 阶段验证过的 Apple Bonjour 绑定，包含主机 A 记录及服务发布；发布任务退出时会重新尝试注册。新服务使用其独立设备名称/5034，与现有 5033 试用环境分开。

## 验证

19 项路由测试通过。追加路由 + 实际 LanClipboardBridge 的丢失回复场景测试后，平台无关回归共 818/818 通过。此处实际桥接使用模拟 OS setter，不是 iPhone 真机跨网络验收。新增路由器尚未接入最终 iPhone 入口，不能据此宣称 0.2 已可安装使用。当前本机原版程序和 0.1 运行配置未替换。

## NAS 和 HTTPS 部署前提

用户现有服务是群晖 DS220+ Docker 中的 SyncClipboard Server。2026-10-22 不带任何认证的只读探测：原 HTTP 根路径返回 401，新 v1 capabilities 路径返回 404。此结果不足以判定具体镜像版本，但当前入口不提供已验证的中继能力。没有向公网 HTTP 发送账号、密码或剪贴板内容。

应先确认 DSM 管理地址和 DSM 版本、原容器镜像/挂载目录/端口/账号来源，备份容器定义并在停止写入的情况下备份持久化数据。准备 Fork 镜像后，优先使用独立测试容器和复制的数据目录验收，不让两个容器共享正在写入的 SQLite 数据目录。公网通过 DSM 反向代理的 HTTPS 入口转发至测试服务；证书须匹配域名，SignalR 须支持 WebSocket。原入口保留至用户切换前可回退。

[DSM 反向代理官方说明](https://kb.synology.com/en-my/DSM/help/DSM/AdminCenter/system_login_portal_advanced)提供 WebSocket 头设置；[证书官方说明](https://kb.synology.com/en-us/DSM/help/DSM/AdminCenter/connection_certificate)说明证书及续期。标准 DSM Let's Encrypt HTTP 验证依赖公网 80 端口；若环境不可用，应选择适合现有网络的证书申请方式，不能仅更改 URL 为 https。

## iPhone 验收前提

[Actions 官方动作列表](https://sindresorhus.com/actions)确认扩展 URL 请求提供状态码、响应头和超时，但不能从这些描述推断 DNS/断网错误会返回结果。正在请用户用不存在的 .local 名称、2 秒超时、后接显示结果做单项测试。若动作直接终止，需使用 Apple 官方支持的 [x-callback-url x-error](https://support.apple.com/en-ph/guide/shortcuts/apdcd7f20a6f/ios) 或另一个经实机确认的错误处理入口；不能用“如果结果为空”替代异常处理。

下发快捷指令前必须验证：同网只访问 LAN；不可达时自动转 NAS；LAN 写成功而回包丢失不重复写；401 和冲突不回退；两路失败保留 iPhone 剪贴板；恢复 LAN 后重新走直连。读写目标必须校验 Windows 设备 ID，不能只相信名称相同。iPhone 明文文本仅保存在运行所需的本地变量/受控状态中，不放进回调 URL、日志或分享文件。

## 当前交付状态

开发中，没有发布 0.2 Release。缺少实际 HTTPS 入口、NAS 升级部署和 iPhone 错误处理实机结果。旧 0.1 Release 继续代表此前手动局域网方案。

管理页面只读检查：用户提供的 HTTP 管理入口自动跳转到已有 HTTPS 管理登录页，说明管理端具备 TLS；这不等于剪贴板服务已经有 HTTPS 反向代理。已打开该页面等待用户登录，尚未读取或更改容器/证书配置。

2026-10-22 后续实测：Actions 的扩展 URL 请求超时报 actions.timeoutError，动作链终止。用户随后按 Apple x-error 方式运行，确认能显示另一个快捷指令中的“回退入口已触发”。因此错误回调控制流已有真机证据，但 NAS 数据传输尚未接入，不能宣称完整自动回退验收通过。
