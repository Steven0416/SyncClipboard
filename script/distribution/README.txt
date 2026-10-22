ClipBridge LAN Kit 0.1.0-preview.1 / Windows x64

这是已验证的固定主机名辅助工具，不是完整替换版 SyncClipboard。
包含独立名称发布器、自带 .NET 运行时、配置和启动工具；不包含你的账号、密码、网卡信息或 Apple Bonjour。

新电脑首次使用
1. 下载原版 SyncClipboard Windows x64 并解压到固定目录：
   https://github.com/Jeric-X/SyncClipboard/releases/tag/v3.3.1
   本轮实测使用原版 3.3.1。不要同时启动两份 SyncClipboard。
2. 安装/确认 Apple 官方分发的 Bonjour，确保 Bonjour Service 正在运行。
   本工具不包含或自动下载 Apple 安装包；不满足依赖时 Setup 会明确提示。
3. 将本 ZIP 完整解压到固定目录；不要在 ZIP 预览中运行。双击 Setup.cmd。
   选择物理 Wi-Fi/有线网卡，输入固定名称及原版 SyncClipboard.exe 的完整路径。
   默认 clipbridge-test.local。同一网络的其他电脑请使用 clipbridge-office.local 等不同名称。
   工具为当前用户创建原版客户端和名称发布器两个登录启动项。整个包与原版目录均不要移动。
4. 在原版“内置服务器”页开启服务器，端口 5033，自行设置独立的强用户名/密码。
   在原版“同步”页新增 SyncClipboard 类型账号：
   地址 http://127.0.0.1:5033，用户名/密码与上一步一致；选中该账号，开启同步、上传、下载。
   Setup 不会修改原版配置或已有 NAS 账号。本地账号被选中后，NAS 不是当前同步账号。
5. iPhone 导入原项目手动同步快捷指令：
   https://www.icloud.com/shortcuts/321944d62831416f9d2ba7917917686a
   地址设为 http://你选择的名称:5033，填写第 4 步的用户名和密码。
   iPhone 与电脑应在可互通的同一局域网；若系统询问访问权限，按实际信任情况允许。
6. iPhone 复制 → 运行快捷指令“上传” → Windows 粘贴。
   Windows 复制 → iPhone 运行快捷指令“下载” → iPhone 粘贴。

日常使用
- 登录 Windows 后两个组件自动启动；也可双击 Start.cmd 补启动。
- 首次配置需手工设置 5033 和本机同步账号；以后无需重复。
- 状态查看 state\status.txt。名称解析成功不代表 HTTP 服务已经启动。
- 电脑睡眠、离线、跨越隔离的访客 Wi-Fi 时不可访问。
- 无法连接时检查原版是否运行、5033 是否启用、Bonjour Service 和防火墙是否允许该局域网访问。
  本工具不自动修改防火墙、网络类别或 NAS。
- 本版本只验证过双向文本。没有 LAN/NAS 自动故障切换；不是 iPhone 无感后台同步。
- IP 变化重发布已实现；实际 DHCP 换址和整机重启恢复尚未实测。

停止/移除
- 在本包 state 目录创建名为 stop.request 的空文件，名称发布器会撤销公告并退出。
- Win+R 输入 shell:startup，只删除本工具的 ClipBridge Bonjour Discovery.lnk 和 ClipBridge LAN Client.lnk。
- 退出原版客户端。不要卸载其他软件使用的 Bonjour，也不要删除其他启动项。

来源及许可
SyncClipboard 原作者 JericX，MIT：https://github.com/Jeric-X/SyncClipboard
本工具源码：https://github.com/Steven0416/SyncClipboard/tree/feat/lan-first-shortcuts
本包 LICENSE 沿用项目 MIT。Apple Bonjour 为独立第三方软件，不随本包分发。
