# LAN-first 实际验证记录

日期：2026-10-13（Asia/Shanghai）。环境：Windows x64；项目内 .NET SDK 10.0.302；Git 2.45.2；项目内 GitHub CLI 2.102.0。上游基线 ae6c3aa5。原工作目录为空，未覆盖已有代码。开发分支 feat/lan-first-shortcuts。

## 自动化与构建

| 检查 | 实际结果 |
| --- | --- |
| Core/Test 编译 | 通过，0 警告、0 错误 |
| 平台无关完整回归 | 789/789 通过；其中新增 12 项：9 项 Bridge／配置测试、1 项真实 HTTP/SignalR 集成、1 项 LAN HTTP 认证、1 项 NAS 能力开关防护 |
| 新增 Bridge 行为 | 中英文／Emoji／多行、读当前内容、使用原 setter、重复请求不覆盖新复制、同 Hash 并发仅一方成功、敏感内容过滤、无效请求、离线明确失败均通过 |
| 真实 NAS 协议集成（本机回环） | HTTP Basic 认证→SignalR 注册→请求→关联响应通过；断线返回 503 且没有旧 Profile。它使用模拟设备，不能替代 NAS 实机及 OS 剪贴板验收 |
| WinUI3 x64 Release | 使用 SDK 自带 MSBuild (`dotnet build`) 成功，0 警告、0 错误；旧 VS 2022 MSBuild 17.11.2 加载 .NET 10 SDK 失败。需使用匹配工具链 |
| NAS 服务端 Release publish | 通过；本地产物 artifacts/lan-first-server。未构建 Docker 镜像，未部署 DS220+ |
| Windows WinUI3 服务注册测试 | 最终 17/17 通过。首次 14 通过、1 失败，定位为旧测试仍要求未注册的 IWebDav。更新该陈旧断言以验证实际 RemoteClipboardServerFactory，并新增 LanClipboardBridge、LanFirstService 注册验证；未添加无用产品服务。首次测试构建出现 PRI257/PRI263 资源语言警告，最终增量运行通过 |
| C# 格式 | 对本次修改文件执行 dotnet format，限定文件检查通过 |
| XAML 格式 | 对唯一修改的 XAML 执行 XamlStyler，校验通过 |
| 凭据扫描 | 新增及修改文件未匹配 GitHub Token／私钥／URL 内嵌密码模式，人工审查无真实配对凭据；不提交 .tools、测试结果或用户配置。扫描不能证明绝对无泄露 |
| 许可检查 | 保留原 LICENSE / LICENSES；最终恢复资产中列出 114 个产品依赖的软件包许可元数据，2 项仅提供旧 licenseUrl（codecracker.CSharp、Windows SDK BuildTools），尚须发布前核查完整许可和 redistributable 文件 |

日志与 TRX 位于忽略的 `.tools/` 和各测试项目 TestResults；不上传潜在含环境信息的完整日志。

## Windows 本机网络实测

通过只读 Windows Network List Manager 检查，当前系统 0 个许可私有 IPv4 地址，主动公共网络模式探测到 2 个 IPv4 地址。诊断只发随机测试设备的服务公告，不开 HTTP 端口、不读写实际剪贴板；结束时撤销公告。原生 mDNS 注册成功回调已收到，Windows `.local` 名称解析 10 秒超时。因此只能记为“注册回调通过，主机名解析未通过”，不能声明 iPhone 自动定位已可用。没有为测试自动修改网络类别、防火墙或路由器。

## 用户提出的 15 项验收

| # | 场景 | 自动化／本机证据 | iPhone／NAS 实机 |
| --- | --- | --- | --- |
| 1 | 家庭 Wi-Fi 直连 | Bridge 与本机回环 HTTP 可用；主机解析尚未通过 | 尚未验证 |
| 2 | Windows IP 改变仍定位 | 已实现 5 秒地址重建／公告；未实测 DHCP 换址 | 尚未验证 |
| 3 | 更换可互访 Wi-Fi | 已实现网络类别／地址重检 | 尚未验证 |
| 4 | mDNS 阻断明确失败及降级 | 本机解析超时；有测试按钮和 LAN/NAS 菜单教程 | 尚未验证菜单真机行为 |
| 5 | LAN 期间无非必要 NAS 上传 | 按需模式文本分支停止自动上传；独立历史同步、图片／文件仍可能写入 | 未抓包、未检查 NAS 磁盘 |
| 6 | NAS 离线 LAN 可用 | LAN Bridge 不依赖 NAS；本机 HTTP 测试独立启动 | 尚未验证双设备 |
| 7 | 蜂窝网络取最新文本 | 本机 HTTP/SignalR 请求在线设备通过 | 尚未验证 |
| 8 | NAS 旧内容不覆盖新文本 | Hash 冲突／重复请求／并发测试通过，重连旧快照被忽略；OS 原子写入仍缺失 | 尚未验证 |
| 9 | NAS 发送自动写入 Windows | Bridge 使用现有 setter 的自动化通过；实际 WinUI 剪贴板写入未验证 | 尚未验证 |
| 10 | 中英、Emoji、多行 | Bridge 与 SignalR 集成通过 | 尚未验证 |
| 11 | 未授权 LAN 请求失败 | 最终回环 HTTP 认证测试结果见上表 | 尚未验证真机 |
| 12 | Windows 休眠唤醒 | 地址重检与原连接检测保留 | 尚未验证 |
| 13 | NAS 重启恢复 | 注册随现有连接重建执行 | 尚未验证 |
| 14 | 重复消息无循环 | 同请求 ID 仅一次 setter，PULL_STOP 使用 Profile 消息类型 | 跨重启回执不持久化，尚未验证实机 |
| 15 | 图片／文件／历史原功能 | 原回归测试通过；新增通道仅文本、其他沿用旧通道 | 尚未验证真实图片／文件 |

## 发布前仍需完成

1. 修复／验证实际 `.local` 主机解析，测试真实 DHCP 换址与另一 Wi-Fi；原生注册回调需进一步增加有界取消与退出保护。
2. 增加 Windows 原子条件剪贴板写入，处理应用最终读与 OS 外部写入间的竞争窗口。
3. 实现 NAS 持久离线发送队列及恢复、明确的缓存状态和修改时间；当前返回 offline，不静默回放旧内容。
4. 扩展完整 Profile/图片/文件 LAN 直连，评估历史同步独立策略。
5. 在 iPhone 创建并运行快捷指令、验证 UUID 动作和局域网权限；可靠异常捕获未确认前保留手动菜单路径；获取用户认可的可导入分享版本。
6. 真机完成上述 15 项验收，检查流量／NAS 写入。
7. 完成发布产物许可核查、多语言界面和最终视觉验收，所有门槛通过后才创建 Release。

当前提交应使用 Draft PR，不自动合并、不发布正式 Release。已成功构建不等同于完整产品验收通过。
