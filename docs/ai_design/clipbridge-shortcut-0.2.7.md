# ClipBridge 单入口快捷指令预览 7

2026-10-26。用户要求停止手动编排，改为生成一个可导入、可分享的完整入口。
本预览覆盖当前文字协议的发送、获取、自动 LAN/NAS 选路、继续任务、扫码配对和清除任务。
图片、文件、后台监控不在当前协议范围内；仍需主动运行快捷指令。

## 使用

安装 Actions，导入 `ClipBridge.shortcut`，保留名称 **ClipBridge**。
已有 `clipbridge-pairing-v1` 本机钥匙串配对可直接复用；新手机从菜单选择扫码配对。
发送前先复制文字，运行后选择“发送当前剪贴板（新任务）”；获取后直接粘贴。
首次按 iOS 提示允许 Actions、剪贴板和对应局域网/HTTPS 服务的访问。
只需这一条快捷指令，无需依赖旧 CB-* 测试入口。

每个新任务优先 LAN，等待 4 秒；连接异常或 HTTP 408/502/503/504 后顺序使用 NAS，等待 15 秒。
本机只读实测 LAN 处理约 2.4 秒，故本快捷指令较原 2 秒计划增加等待，避免过早回退。
认证、冲突、限流、格式/设备/请求/内容校验失败停止。两条通道均不可用时保留任务。
“继续上次任务”使用已保存的原文字；“新任务”替换旧任务。一次只运行一个任务。
完成或清除后移除会话正文和请求，只保留完成状态/通道；配对凭据保留在本机钥匙串。

## 实现与参考

生成器：`script/Build-ClipBridgeShortcut.py`，复用已从用户实际导出文件确认的 AppIntent 序列化工具。
当前 iOS 导出元数据版本 `5037.111`；所有字符串 If 先显式转 Text，数量判断使用 Count。
请求/设备 UUID 和 SHA256 用限长十六进制正则匹配，忽略大小写，避免泛型/UUID 对象比较错误。
用协议正文实际序列化的 requestId 校验回执，接收文字 SHA256 与 profile.hash 核对后才复制。

运行状态在本机 `clipbridge-session-v2`，与旧 pending 隔离。
LAN/NAS HTTP worker 只发送并保存原始响应；PROCESS 使用无错误回调的新实例解析。
回调 URL 仅携带阶段和会话 UUID，拒绝旧会话回调；不携带凭据或剪贴板。
上传先获取基线，持久化完整写入 JSON，再发送。超时/回退/继续均复用 requestId/text/expectedHash，
不重新读手机剪贴板或刷新基线强行覆盖。无跨进程原子锁，因此不支持同一手机并行任务。

参考 [Actions 作者动作说明](https://sindresorhus.com/actions)和
[作者导出的动作参数说明](https://gist.github.com/sindresorhus/fbba65a774fb9da915e624807a02a6d2)、
[Apple x-callback-url 文档](https://support.apple.com/guide/shortcuts/apdcd7f20a6f/ios)。
另读取 [Shortcuts Playground 的 ToolKit v78 / iOS 27 元数据](https://github.com/viticci/shortcuts-playground-plugin)，
核对 Hash、Copy to Clipboard、Match Text、Count 和控制流字段。未安装该插件。

## 验证与限制

- 生成产物 plist 回读、UUID 唯一、引用顺序、条件/菜单嵌套平衡校验。
- `script/Test-ClipBridgeShortcut.py` 直接解释生成动作和变量连线，模拟协议服务器：LAN 双向、NAS 回退、
  恢复 LAN、丢失写回执后的相同请求去重、冲突不覆盖、HTTP 错误分类、损坏 JSON 不回退、
  请求/设备/正文校验、保留原文字继续、旧回调拒绝、扫码/清除及回调不含凭据，共 11 项。
  这是本地动作模型，不等同于 Apple 执行环境。
- 外部 ToolKit 检查器没有未解释的结构问题；其剩余结果是注释风格规则、合法的空字典字符串，
  以及把相机扫码旧动作映射到 macOS 27 必填 imageFile 的目录差异。保留用户已在 iOS 使用的相机扫码动作，
  不添加会要求选择图片的 macOS 参数。
- 独立比对本地实际配对的凭据/地址/设备编号及 Basic 编码，确认通用产物均未嵌入。
- 修复桥接层常规 ProfileDto 在超过 10240 字符时拆文件的问题，文字专用协议改为内联完整正文。
  `LanFirstTests` 16 项通过，新增长文字双向和超限拒绝用例；WinUI x64 自包含发布成功。
- 当前电脑预览客户端已更新；实际 LAN、HTTPS NAS 只读请求均 200/ok。
  原 NAS 5033 容器和原版程序配置未修改。

签名服务只接收无凭据通用模板。签名产物需确认 AEA1 头和完整下载后才发布。
手机导入、Actions 权限提示和最终整条自动选路执行仍待用户验收，不能用模拟测试替代真机结果。

## 后续真机反馈

用户确认预览 7 可以导入，电脑到 iPhone 获取剪贴板正常；随后发送测试提示通过 NAS，且用户在电脑看到收到的文字。
因此单入口导入、获取及 NAS 上传已有用户实测证据，仍未确认新入口在同一 Wi-Fi 优先使用 LAN。

电脑排查：物理 WLAN 2 地址仍为 192.168.6.164，Bonjour 服务运行；预览 UI 刷新报告 LAN listening、NAS connected。
系统 TCP 枚举一度未显示 5034，不能据此判定监听已停止：随后直接认证请求 capabilities 连续 200、设备匹配，
约 0.09–0.19 秒；LAN 和 NAS 的实际只读 clipboard 请求也均为 200/ok，约 0.12/0.16 秒。
本机原始 mDNS QU 查询两次 5 秒内没有匹配响应，该结果尚不足以认定手机无法解析。
下一步只让手机运行此前已验证固定名称的“CB-局域网测试”，区分名称/网络路径与新入口的扩展请求路径。
未因本轮状态探测更改防火墙、网卡类别、凭据或 NAS 配置。

用户随后反馈旧固定名称测试卡在 Get Contents of URL，没有返回。进一步用 netsh/netstat 交叉检查，
实际 Bonjour 服务 PID 与 UDP 5353 监听存在，匹配实际可执行路径的 Public 入站允许规则也存在；
Get-Net* 的部分枚举与原生命令不一致，不能把其缺项当作服务未监听的证据。
通过 dnssd.dll 的 DNSServiceQueryRecord 对接口 6 和全部接口查询，均 4 秒内无配对名称 A 记录，
但预览 UI 仍报告 registered，说明该状态不能保证实际名称发布仍有效。
只重启准确预览路径进程，未更改任何配置；随后认证 capabilities 正常，两个 Bonjour 查询均返回
接口 6 的正确 IPv4，原始 mDNS 线级查询也收到匹配响应。根因尚不能仅凭重启恢复认定，
后续应补充名称发布存活检查/恢复，并验收网络变化及长期运行；下一项手机只重试原固定名称测试。

后续用户确认固定名称测试显示 1，新单入口通过 LAN 上传文字到电脑成功；关闭 iPhone Wi-Fi 后通过
蜂窝 NAS 发送成功，重新连接 Wi-Fi 后再次通过 LAN 发送成功，电脑均实际收到。
因此手机新入口 LAN → NAS → LAN 自动选路已有三段真机验收。
用户要求修复发布稳定性并交付其他电脑使用的完整包，后续实现及验证见
[0.2 Windows 发布与恢复记录](lan-recovery-release-0.2.md)。
