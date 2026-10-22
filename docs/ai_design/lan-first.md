# LAN-first 实验性文本通道

本 Fork 基于 [Jeric-X/SyncClipboard](https://github.com/Jeric-X/SyncClipboard)，原作者 JericX，保留完整 Git 历史、MIT LICENSE 及 LICENSES。新增代码沿用 MIT。当前分支是可审查的实验性实现，**尚未达到用户提出的完整 LAN-first 发布要求**；请先保留正在使用的官方客户端、NAS 镜像及快捷指令。

English: [LAN-first notes](lan-first.en.md)。配置步骤：[Windows、NAS、iPhone](lan-first-setup.md)。测试与未完成项：[验证记录](lan-first-validation.md)。

## 已实现与边界

- 独立、默认关闭的 Windows LAN HTTP 服务，默认端口 5034，避免与现有 5033 内置服务器冲突。现有设置全部保留。
- 复用 IClipboardFactory、LocalClipboardSetter、LocalClipboard.Semaphore 与 remoteProfilemutex，不新增剪贴板监听器。LAN 接口读取系统当前文本并通过现有 UI 线程写入器写入系统剪贴板。
- 随机 UUID 设备 ID、256 位随机 Token，HTTP Bearer 认证，常量时间比较，不在 URL、mDNS、日志中输出 Token。
- 默认只在 Windows 私有网络 IPv4 地址监听；每次请求重新检查网络类别，5 秒内重建监听与公告。公共／域网络必须主动启用风险开关。分类失败时关闭访问。未修改用户网络类别或防火墙。
- Windows 原生 DNS-SD API 发布 `_syncclipboard._tcp.local` 服务和指定 IPv4 主机记录。稳定名称为 `syncclip-<完整设备UUID>.local`，TXT 仅 version、port、deviceId。地址变化后撤销旧公告、重新发布。
- 新增 NAS HTTP API 与 SignalR Hub 方法。NAS 只在请求发生时向指定设备发送控制消息；返回结果直接交给调用者，不写入原有 SyncClipboard.json 或历史数据库。
- NAS 文本按需模式显式启用后，停止自动上传文本，忽略旧 NAS 文本快照及重连时的初始快照。图片、文件仍使用原自动上传及实时下载通道；手动上传仍保留。这不是“所有类型零 NAS 写入”。独立历史同步若已启用，仍可能上传历史；若要最大限度减少 NAS 写入，关闭历史同步。
- 设置页先通过 HTTPS 检查 NAS v1 能力才允许打开按需模式；未升级／不可达时保留原模式。手动编辑配置绕过检查后，新 Hub 注册失败会显示升级提示。关闭按需开关后恢复原同步协议。请勿在升级和配对前启用。

当前限制：新增 LAN/按需接口只支持文本（最多 262144 UTF-16 code units）；Windows 离线时返回 503，不返回过期缓存，也未实现离线发送持久队列。iPhone 快捷指令提供操作教程，尚无已签名、真机验证过的 `.shortcut` 文件。自动故障回退、跨 Wi-Fi 重定位、Windows 真实剪贴板写入、NAS 实机验证未完成。设置页为中文实验性界面，未完成完整多语言适配。WinUI3 主客户端构建结果见验证记录。

## 源码审查结论

| 上游模块 | 结论及本次复用 |
| --- | --- |
| ServerService / ServerConfig | 内置 ASP.NET 服务独立启动，可与 NAS 客户端共存；原默认 admin/admin 不用于新 LAN 服务 |
| Server.Core/Web / SyncClipboardController | 原接口读写服务器目录和历史 Profile，并不等于当前 Windows 系统剪贴板；新 LAN 服务不复用这些存储接口 |
| OfficialAdapter | 保留 SignalR 连接、原认证和全部旧接口，新增请求事件处理及设备注册 |
| RemoteClipboardServerFactory | 原账号、代理及适配器生命周期保留；改变按需配置后重建当前连接 |
| OfficialEventDrivenServer | 按需模式拒绝旧文本广播和重连快照，保留其他原事件及连接检测 |
| NetworkAccountSwitchService | 原 Wi-Fi/网络规则账号切换保留；不把 SSID 相同当作可直连证明 |
| SystemNetworkContextProvider | 原 IP/SSID 采集不能证明网络可信；新增 Windows NLM 类别判定，不索取 Wi-Fi 定位权限 |
| UploadService / DownloadService | 原监听、下载、历史、Profile Hash 去重保留；仅在显式按需模式跳过文本自动上传；新写入发送既有 PULL_START/PULL_STOP 消息防止回传 |

## 协议 v1

LAN 受 Bearer 认证：`GET /api/lan-first/v1/capabilities`、`POST /api/lan-first/v1/clipboard`。

NAS 使用现有 Basic 认证与外部 HTTPS：`GET /api/lan-first/v1/capabilities`、`POST /api/lan-first/v1/devices/{WindowsDeviceId}/clipboard`。

请求体示例（所有 ID 均为示例，配对时自行生成）：

```json
{"requestId":"11111111-1111-4111-8111-111111111111","sourceDeviceId":"22222222-2222-4222-8222-222222222222","operation":"get"}
```

发送先执行 get，保存 `profile.hash`，再发送新的请求 ID：

```json
{"requestId":"33333333-3333-4333-8333-333333333333","sourceDeviceId":"22222222-2222-4222-8222-222222222222","operation":"set","text":"Hello 中文 😀\n第二行","expectedHash":"上一步返回的 profile.hash"}
```

回复有 requestId、deviceId、status、profile、updatedAt、isCache。updatedAt 是本次快照读取／写入完成的服务器观察时间，**不是操作系统剪贴板原始复制时间**。不比较不同设备时钟决定冲突。

| HTTP | status | 调用方行为 |
| --- | --- | --- |
| 200 | ok | get 可复制 profile.text；set 表示写入器已完成 |
| 409 | conflict / request-id-conflict | 保留本地内容，提示冲突；不自动以新 Hash 强制重试覆盖 |
| 422 | unsupported-type | 新通道暂不支持当前类型，使用原图片／文件同步流程 |
| 400 | invalid-request / filtered / too-large | 不修改剪贴板 |
| 429 | busy | 延迟后使用相同请求重试，最多有限次数 |
| 503 / 504 | offline / timeout | 不复制、不显示“已发送”；允许用户重试 |

每台 Windows 最多保存 256 个成功请求的内存回执，重复请求返回 isCache=true，不再次写入。重复 ID 对不同载荷返回冲突。缓存随 Windows 重启清空；写入仍需要目标 Hash 一致。NAS 同时最多 32 个进行中的请求；同 ID 同时到达返回 busy；超时 12 秒；Windows 处理超时 10 秒。断线立即终止对应等待。NAS 只接受指定连接完成对应请求，其他连接不能伪造关联回复。

两条通道共享相同 Bridge、锁和去重回执。所有网络写入在同一 lock order 内串行化，写入前再次读取目标 Profile；如果 Windows 正在外部复制，读写间仍存在无法靠应用 semaphore 消除的 OS 竞争窗口。**尚未实现 Win32 剪贴板序列号与原子条件写入；不能声称所有极端并发均已解决。** 不在 HTTP 客户端断开后主动回滚已经完成的剪贴板写入；超时重试必须复用原 requestId。

## 发现与快捷指令研究

Microsoft 提供原生 [DnsServiceConstructInstance](https://learn.microsoft.com/en-us/windows/win32/api/windns/nf-windns-dnsserviceconstructinstance) 和 [DnsServiceRegister](https://learn.microsoft.com/en-us/windows/win32/api/windns/nf-windns-dnsserviceregister)，可指定主机、地址和服务端口。本机已收到成功注册回调，但本机 .local 解析诊断超时，因此“注册成功”和“iPhone 可以解析”必须分开验收。

Apple 文档确认 [获取 URL 内容](https://support.apple.com/en-gb/guide/shortcuts/apd58d46713f/ios) 可发 HTTP 请求和 JSON 请求体。该说明并未确认通用 DNS-SD 浏览或可靠的网络异常捕获。本项目不将“如果结果为空”的分支等同于捕获请求动作失败；尚未宣称自动 NAS 回退可用。当前使用 LAN/NAS 两组快捷指令及菜单入口，无第三方 iPhone App 依赖。

## 上游兼容与许可

开发基于上游提交 `ae6c3aa5`，完整 SHA 可用 `git log upstream/master -1` 查询。旧 HTTP 路由、Basic 认证和 Hub 事件名称保留。新功能默认全部关闭；上游原版 NAS 可继续使用原模式。按需模式要求此 Fork 的 Windows 和 NAS 同时升级。

未新增产品 NuGet 依赖。依赖许可元数据清单见 `lan-first-dependency-licenses.json`；它记录当前实际恢复的软件包许可表达式／许可文件／来源，不能替代发布前对缺失或自定义许可的人工检查。原始 LICENSES 保留；项目内下载的 SDK、CLI、XamlStyler、诊断工具均在忽略的 `.tools/`，不作为软件产物发布。
