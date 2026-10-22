# ClipBridge 固定 .local 名称发布

目标地址：`http://clipbridge-test.local:5033/`。本轮只处理 Windows 局域网发现及现有内置 HTTP 服务的启动，不升级或修改 NAS 服务，不切换 NAS 按需同步模式。

## 实现

新增 `src/ClipBridge.Discovery`，作为现有 SyncClipboard 的独立名称发布进程；它不启动第二个 HTTP 服务、不读取剪贴板、不访问 NAS。当前使用电脑已安装的 Apple Bonjour 服务及系统目录中签名有效的 `dnssd.dll`，不打包或重新安装 Bonjour DLL。

`BonjourMdnsPublisher` 同时完成两件事：

1. 使用 `DNSServiceCreateConnection` / `DNSServiceRegisterRecord` 在选定网卡发布唯一的 `clipbridge-test.local.` A 记录。
2. 使用 `DNSServiceRegister` 发布 `_syncclipboard._tcp.local.`，SRV 目标指向上述主机名，端口为 5033。TXT 仅包含版本、随机设备 ID 和端口。

Apple 明确说明：[指定服务目标 host 不会自动创建该 host 的地址记录](https://developer.apple.com/documentation/dnssd/1804733-dnsserviceregister)。所以不能仅依据 DNS-SD 注册回调宣布主机名可解析。API 调用约定及记录类型参考 [Apple 官方头文件](https://github.com/apple-oss-distributions/mDNSResponder/blob/main/mDNSShared/dns_sd.h)。本实现为独立编写的原生 API 绑定，不复制或分发 Bonjour 实现。

程序记录网卡 GUID，不固定写入局域网 IP。每 3 秒重新读取该网卡的 IPv4 地址；地址或接口索引变化时，先释放旧注册，再注册新地址。网卡掉线时撤销公告，重连及 Bonjour 连接故障后重试。只使用指定网卡，不把 VPN 或 Hyper-V 地址发布到物理 Wi-Fi。名称冲突会报错，不会悄悄更名导致快捷指令地址变化。原生回调处理和释放在同一个工作线程完成，注册等待有 10 秒上限，轮询每 250 毫秒检查退出。

当前实现发布 IPv4 A 记录，不发布 AAAA。没有更改 Windows 主机名、VPN 设置、网络类别或防火墙；当前 Bonjour 既有规则已允许所在网络。发布器不会代替 SyncClipboard 的认证。HTTP 仍需客户端原有内置服务器启动并监听相应端口。

## 构建与安装

```powershell
dotnet publish src/ClipBridge.Discovery -c Release -r win-x64 --self-contained true -o artifacts/clipbridge-discovery-win-x64
Get-NetAdapter | Select-Object Name, InterfaceGuid
./script/Install-ClipBridgeDiscovery.ps1 -InterfaceId '<选定物理 Wi-Fi 网卡 GUID>' -HostName clipbridge-test.local -Port 5033
```

安装脚本只为当前用户建立 `ClipBridge Bonjour Discovery.lnk` 登录启动项，并启动已构建的程序。win-x64 产物自带 .NET 运行时，以无控制台窗口方式运行。必须保留 `artifacts/clipbridge-discovery-win-x64` 和项目目录，移动目录后需重新运行安装脚本。状态和设备 ID 保存到忽略的 `.tools/bonjour-live/`，不写到 GitHub，也不包含认证信息。

如需停止，只在本机创建 `.tools/bonjour-live/stop.request`，进程将在下次轮询时撤销公告并退出。移除自动启动时，仅删除当前用户 Startup 文件夹中该名称的快捷方式。不要删除其他软件的 Bonjour 服务或其他启动项。

## 实际验证记录

| 时间／检查 | 结果 |
| --- | --- |
| 2026-10-13，Apple Bonjour 注册 | 主机 A 记录和 DNS-SD 服务注册回调成功 |
| 2026-10-13，物理 Wi-Fi mDNS 报文 | 收到 A 查询应答，RDATA 与当时物理 Wi-Fi IPv4 地址一致 |
| 2026-10-13，用户 iPhone Safari | 使用固定名称到达认证提示／服务器页面 |
| 2026-10-13，用户 iPhone Actions 快捷指令 | 用户确认“局域网连接成功” |
| 2026-10-21，独立后台产物 | self-contained win-x64 发布成功，后台进程和当前用户登录启动项已安装 |
| 2026-10-21，HTTP 认证 | 未带凭据的回环及 Wi-Fi 请求返回 401；带本机生成凭据的回环请求返回 200，未打印凭据或响应内容 |
| 2026-10-21，用户 iPhone 复测 | 后台程序接管发布后，用户再次确认“局域网连接成功” |
| 2026-10-21，核心测试 | 798/798 通过，包含新增主机名合法性与 TXT 记录测试 |
| Windows 本机通用 DNS API | 曾超时；不能据此否定 iPhone 已验证的 mDNS 路径，未修改 VPN/DNS 设置强行修复 |
| 真正改变 DHCP 租约地址 | 尚未进行；代码已实现地址变化重发布，但不能宣称真实换址测试通过 |
| 完整 Windows 重启／登录 | 启动项已检查存在；尚未实际重启电脑验收 |

## 客户端配置兼容事件

2026-10-21 开始检查时，原客户端未运行，当前用户配置文件只有 `ConfigVersion=3`，没有服务器或账号字段。原安装版本只支持配置版本 2，弹出错误并阻止 HTTP 服务启动。没有点击“覆盖配置”。经用户明确批准后，先备份原文件，仅修复版本元数据并恢复 5033 内置服务，生成新的高强度 LAN 认证信息。凭据没有输出到聊天、日志或 Git。

NAS 服务器和 NAS 账号没有被本轮配置；当前文件原本不存在 NAS 账号字段，因此本轮不能声称已还原原有公网客户端连接。后续如需恢复该连接，需另行处理已有配置或重新配对，不能从空配置推断凭据。

旧 Windows 服务注册测试直接解析用户配置，存在影响真实环境的风险。本轮将其改为临时目录中的配置；最终 Windows 测试 17/17 通过，测试前后真实配置的 SHA-256 完全一致。不要为了测试而降低或覆盖真实配置版本。
