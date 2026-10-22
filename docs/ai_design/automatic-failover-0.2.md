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

2026-10-22 配对准备：用户确认 Actions 的 Get/Set/Delete 为独立动作，Set 的 key/value 可填，关闭 iCloud 同步后用 clipbridge-test/hello 实测写入并读取成功。新增离线脚本 `script/New-ClipBridgePairingQr.py`，从私有配对文件生成 PNG，包含版本、两端设备标识、两个请求地址及各自认证头。依赖 qrcode 8.2 和 pypng；二维码含凭据，仅保存在忽略目录并在电脑本地打开，不上传聊天或仓库。正在让手机扫描后保存为本机钥匙串 clipbridge-pairing-v1；尚未确认保存完成，也尚未运行手机请求。

随后用户确认扫码流程显示“配对已保存”；该提示是手动填写的完成消息，因此还需从钥匙串重新读取并解析 version=1 来验证内容。离线生成脚本已检查配对结构、拒绝不安全 NAS 地址和 PNG 文件格式；手机请求与自动回退数据通路仍待验收。

用户已从钥匙串重新读取扫码内容、转换为词典并取得 version，实测显示 1，配对读取与 JSON 解析通过。下一项手机测试使用配对的 lanUrl，将末尾 /clipboard 替换成 /capabilities，通过配对的 LAN Authorization 头 GET，仅显示响应 version；此步骤不读取或写入剪贴板。

手机 GET 首次报告超时。电脑排查发现预览版进程已退出，物理 IP 的 5034 主动拒绝连接；Public 防火墙已有对应预览路径的 Allow 规则。最近崩溃堆栈是 H.NotifyIcon 读取 Assets/upload001.ico 时 DirectoryNotFoundException，发布目录缺失整个 Assets 目录。发布目标补入原始 Assets 文件供托盘图标从磁盘读取，保留 PRI 内资源；此缺陷需修复并实际重启验证后再让手机重试，不能直接归因于 mDNS 或要求用户重配凭据。

补充 Assets Content 后发布成功，41 个文件与源码逐一 hash 一致。预览版重启，电脑真实 LAN→NAS→LAN 请求恢复且剪贴板 hash 相同；用户随后用物理 IP GET capabilities 实测显示 1，手机网络和认证通过。

固定名称另有独立缺陷：单独编译实际 BonjourMdnsPublisher，以无凭据诊断名称调用，稳定复现 Bonjour API error -65540（BadParam）。P/Invoke 字符串由 LPWStr 修正为 LPUTF8Str 后，同一诊断完成主机 A 和 DNS-SD 服务注册。Apple DNS-SD 接口使用 UTF-8 char 字符串，不能按 Windows UTF-16 字符串传递。Windows 自身 Dns.GetHostAddressesAsync 的诊断查询仍超时，不能把注册回调成功当成手机解析验收；最终仍需用户用配对名称 GET。

含 UTF-8 修正的 WinUI 独立预览版已重新发布成功并启动。使用选定物理 IPv4 发往 224.0.0.251:5353 的 QU A 查询，实际收到名称与配对 lanUrl 主机完全相同、A 记录为当前物理地址的 mDNS 应答；随后真实 LAN→NAS→LAN 请求通过且实时 hash 一致。首次查询在启动尚未完成时超时，启动完成后通过。正在让用户把 Get Contents of URL 的 URL 恢复为 Replace Text 输出，以验收手机固定名称。

用户恢复 Replaced Text 变量后，手机固定名称 GET capabilities 实测显示 1。至此配对读取、手机入站访问、认证和新固定名称均通过。下一项测试为 POST get 读取电脑文字并显示；电脑已放入明确测试文字，实际 LAN 接口确认内容匹配。该步骤尚不覆盖手机剪贴板，也不代表最终下载入口或自动回退完成。

后续手机实测通过：CB-LAN-Read POST get 显示测试文字，再使用 Copy to Clipboard 后粘贴一致；CB-NAS-Read 使用独立 nasUrl/nasAuthorization，同 Wi-Fi 和关闭 Wi-Fi 的蜂窝网络均读取、粘贴成功；重新连接 Wi-Fi 后 CB-LAN-Read 仍成功。这些是分别运行两条入口，不是自动切换验收。

Actions 扩展请求界面与输出已由用户截图确认：Method、Body、Headers (dictionary/list)、Timeout、Follow Redirects；HTTP Response 可取 Status Code、Body Text 等属性。未认证 GET 实测返回 401 而不中断；正确 Authorization 字典返回 200；Body Text 转词典可取得 version=1。扩展 POST 使用 JSON 字典转文本作为 Body、Authorization 和 Content-Type application/json 标头。首次读取返回 422 unsupported-type，电脑重新放入明确测试文字后接口 200/ok，用户再次运行也确认 status=ok 且 profile.text 匹配。最终入口仍需 HTTP 状态、设备/请求标识、类型校验及错误回调控制，不能以测试中的裸 Copy to Clipboard 作为完成品。

用户随后确认扩展 LAN 入口加 Status Code=200 分支后显示 LAN copied，粘贴测试文字成功。复制为 NAS 扩展入口（NAS URL/认证、15 秒、关闭重定向）时返回 503。电脑仍运行，但日志持续出现 NAS TLS 握手失败。无认证诊断发现 Windows/路由器 UDP DNS 与 Google HTTPS DNS 对 NAS 返回不同 IPv4；默认地址 TLS EOF，另一地址通过 curl --resolve 保留 SNI 与证书校验可访问同一 HTTPS 服务。尚未修改系统 DNS、hosts、生产 NAS 或手机凭据；正在排查缓存/解析路径，不把固定公网 IP 作为正式解决方案。

随后默认 DNS 缓存自然到期，解析更新到可达地址，与两家 HTTPS DNS 一致。未改任何 DNS/hosts/代理设置，预览客户端自动重新连上并注册 NAS；真实 LAN→NAS→LAN 探针全部成功、hash 一致。该事件是动态公网地址变化时旧 DNS 缓存的短暂不可达，不能通过修改手机认证或关闭 TLS 校验解决。手机 NAS 扩展入口重试待确认。

用户重试 NAS 扩展入口成功，显示 NAS copied、粘贴测试文字一致。创建 CB-Auto-Read，用 x-callback-url 先运行 CB-LAN-Read-Extended，x-error 指向 CB-NAS-Read-Extended。用户依次实测：同 Wi-Fi 显示 LAN copied；在 Settings 关闭 Wi-Fi、使用蜂窝网络后显示 NAS copied；重新连接同一 Wi-Fi 后恢复 LAN copied，均粘贴一致。自动读取的断网/恢复主路径已有真机证据。

此入口仍是开发测试组合：当前 LAN 非 200 分支仅显示状态，不会针对 408/502/503/504 转 NAS；x-error 也尚未限定为网络调用错误，因此后续解析异常可能触发回退。手机尚缺设备/请求/类型响应校验；自动上传尚未建立。不能把该组合宣布为全部满足路由规范的 0.2 成品或发布版本。上传必须先保存本机文字，再获取目标 hash，并把同一写入 requestId、expectedHash、text 保存在本机状态中供两通道重试，不能从读取入口复制后把远端文字误当成待上传内容。

反向上传进展：CB-LAN-Upload 在最前保存 Get Clipboard 的文本为 UploadText；先 GET 操作读取电脑 profile.hash 保存 ExpectedHash，再用新的写入 UUID、operation=set、UploadText、ExpectedHash POST。用户确认准备阶段未改变手机剪贴板，实际 LAN 写入返回 200。电脑随后独立读取实际桥接，HTTP 200/status=ok、设备匹配、文字等于预定 iPhone 测试文本，LAN 反向写入已验证。下一项为独立 NAS 写入；尚未建立具备共享写入状态的自动上传入口，不可直接把两条各自生成 UUID/hash 的上传快捷指令用 x-error 拼接。

NAS 反向上传实测：CB-NAS-Upload 同 Wi-Fi 返回 200，电脑独立读取确认预定 NAS 测试文字匹配。关闭 Wi-Fi 后首次手机报告 Could not connect to the server，随后重试返回 200；电脑独立读取确认预定 cellular 测试文字匹配、设备匹配。首次连接失败原因尚无证据，不归因为权限或配置错误，也不据此加入盲目自动重写。蜂窝 NAS 上传的成功路径已有真机证据，仍缺跨通道共享写入状态及自动上传验收。

用户反馈手动编排过慢，要求生成可直接添加的文件。已改为准备程序生成的 .shortcut 包。纯公开、无网络/钥匙串动作的 CB-Import-Test（仅 Show Result 测试文字）生成后，通过 Shortcuty 文档接口 HTTPS 上传签名，返回 AEA1、21578 字节；现有本机代理下首次 TLS 失败，curl --noproxy '*' 重试成功，未修改系统代理或降低证书校验。测试文件仅保存在忽略的 artifacts/shortcuts-preview 中，尚未在手机导入验收。正式模板不得嵌入实际配对信息，须复用手机钥匙串。需要用户分享已验证 CB-LAN-Upload 的文件/链接以取得当前 Actions AppIntent 的准确序列化结构，再生成并核对完整包。

用户分享了 LAN 上传和 Upload-Prepare 的 iCloud 模板，准备入口也已实测 Upload saved。提取当前 AppIntent 格式后新增 `script/Build-ClipBridgeUploadShortcut.py`，生成单文件 CB-Upload-Auto-Preview（223 个动作）。通过本机固定 pending 钥匙串保存 state、text、requestId、expectedHash，LAN 写入前先保存 hash；NAS 重试复用原写入信息；已有 active 状态需用户确认恢复旧文字。成功后写入空 JSON 清除待上传正文；HTTP 非 200 正常返回，LAN 仅 408/502/503/504 转 NAS；认证、冲突、限流等不通过 HTTP 分支回退。回调 URL 只包含固定入口阶段，不包含文字或凭据。

构建产物通过 plist 回读、动作 UUID/引用顺序、条件块平衡检查，并与实际私有配对值逐项比对确认未嵌入。已取得完整 AEA1 签名产物（37286 字节）；第一次完整模板下载中断，未当成成品，重试临时文件后才替换最终文件。手机导入和执行尚待验收，不能宣称自动上传已通过。此预览仍有已知限制：异常 x-error 未完全限定为网络步骤；响应解析、文本边界及双路失败状态清理仍需进一步真机和失败路径验收。不可据此宣布完整 0.2 发布。

快捷指令预览 1 已导入，但用户运行报告多处 If 参数缺失，截图中词典 state 的比较值没有显示。用户修正单个条件后分享的实际源文件显示该条件从 code=4 变成 code=100（有值），原 active 字符串仍保留；不能据此把相等判断全部改成有值。生成器改为在所有文字比较前增加显式 detect.text，将泛型 Dictionary Value 转成 Text 后比较，保留 4/5 的相等/不等语义；魔术变量 OutputName 也与实际动作名称对应。产物现 241 动作，检查器拒绝未明确转文本的文字比较；已重放截图对应旧形态确认拒绝，并验证 active 仍是相等。此结构修正仍待手机导入验收，预览 1 的成功导入不等于成功执行。

用户安装预览 2 后运行显示 HTTP 400，尚不能确定是读取还是写入、协议 invalid-request 还是 JSON 绑定错误。电脑独立正确请求同接口仍 200/ok。为避免继续盲改字段，预览 3 在非成功分支显示安全诊断词典：operation、HTTP、serverStatus、serverTitle、requestIdValid、sourceDeviceIdValid，以及写入时 expectedHashPresent；不显示实际 UUID、认证、文字、目标 hash 或 profile。字段格式标志通过本机 UUID 正则检查产生。此版是定位 400 的诊断版本，尚未修复或验收自动上传。结构校验后产物 294 动作，完整签名文件 42174 字节。

预览 3 用户诊断：operation=set、HTTP400、serverStatus=invalid-request、两个 UUID 格式标志均 yes、expectedHashPresent=yes。依据 LanProtocol.IsValid 仍需区分实际序列化后的 text 缺失/超长、hash 全空白或字段与原变量差异，不能直接宣称已确定根因。预览 4 明确转换待上传文字、来源与请求编号，空/超长 pending 文字在本机停止并清除旧 pending 正文；发送前将实际编码 JSON 回读为词典，用其字段生成诊断，增加 textPresent/textLength/expectedHashLength/expectedHashNonblank。空剪贴板不会在此预览入口发起上传（服务协议本身仍支持空文本）。仅 HTTP400 解析诊断 JSON，其他 HTTP 错误直接显示状态，避免 401 空正文的诊断解析异常误触发 x-error。
