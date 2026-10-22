# 群晖独立中继预览部署

本文件描述 DS220+（Linux x64）独立试用方案，不替换现有官方容器。

## 构建

```powershell
dotnet publish src/SyncClipboard.Server -c Release -r linux-x64 -p:PublishProfile=LanRelayContainer -t:PublishContainer -p:ContainerArchiveOutputPath=E:/ClipBridge/artifacts/clipbridge-relay-0.2.0-dev.1.tar
```

使用 .NET SDK 的容器归档功能，无需本地 Docker daemon。基础镜像是微软 `aspnet:10.0-noble`；entrypoint 保持 `dotnet SyncClipboard.Server.dll --contentRoot /app/data`。用户 root 与现有 Dockerfile 保持一致；不使用 privileged、host network 或 Docker socket。新目录需要写权限。现有官方容器和数据目录不会被本方案重用。

构建后检查 Docker manifest、entrypoint、5033 端口及 SHA-256。此镜像尚未在 Linux NAS 运行前，不能仅凭 Windows 单元测试称部署成功。

## 计划中的实际配置

- 名称：clipbridge-relay-preview。
- 镜像：clipbridge-relay:0.2.0-dev.1（本地导入归档，无需镜像仓库凭据）。
- 主机监听：127.0.0.1:8787 → 容器 5033，只由已有 DSM HTTPS 代理访问。
- 独立数据：/volume1/docker/clipbridge-relay-preview/data，禁止挂载原生产目录。
- 独立的高强度试用认证；不把凭据提交 Git，不在聊天或截图中展示。
- 保持原 5033 官方容器运行。现有 HTTPS 代理配置已查看，包含 WebSocket 升级头；当前后端不可用返回 502。

导入、创建目录/容器并使原代理后端可用，是首次部署动作；准备好可审查的镜像及 compose 后再确认。部署前不更改任何现有代理、证书、路由器端口或生产容器。

## 验收与回退

1. 不带认证的根路径及 capabilities 拒绝访问；带独立认证的 capabilities 返回 v1 textRelay。
2. Windows 连接测试服务后注册设备；同一请求可经 SignalR 到达实时剪贴板桥接。
3. iPhone LAN 优先、故障 NAS、恢复 LAN 的验收须在独立环境进行；还需确认 x-error 在用户当前 iOS 上实际执行。
4. 移除试用仅停止此独立容器/项目；生产容器保持可用，原数据目录不会迁移。删除数据或配置需单独处理，不自动执行。


## 2026-10-22 现场检查与准备结果

- 原官方容器仍在运行，5033 映射及持久化数据目录已只读确认；没有重建或停止。
- 已存在 ClipBridge HTTPS 反向代理入口，配置了 WebSocket 升级头；TLS 校验通过，后端不可用返回 502。计划复用该入口，不新增路由器映射。
- 用户在 iOS 27 / Actions 实测：扩展 URL 动作超时直接终止；用 x-callback-url 的 x-error 能触发另一条回退快捷指令并显示提示。只验证了控制流，尚未在回退中传输剪贴板。
- Linux amd64 Docker 归档已生成，entrypoint 与内容目录正确，7 个镜像层按解压后 diff-id 校验通过。
- Windows self-contained 预览客户端已发布；配置和 appdata 使用预览目录，内置 5033 服务器关闭，不覆盖原版配置。LAN 5034 限制为选定物理网卡；网卡消失时不退到其他接口。
- Windows 适用回归 833 项通过、2 项因环境前提跳过；跳过项是 UNC 测试和 NativeAOT 窗口烟雾测试，不是已验证通过。
- 试用认证已独立随机生成，仅保存在忽略的本机目录及隔离客户端配置中，不纳入源码或镜像。
- 当前物理 Wi-Fi 被 Windows 分类为公共网络。预览配置保持公共网络监听关闭；自动审批拒绝过开启该开关的准备命令，未执行该命令。准备测试方案时不绕过该限制，需用户明确允许仅在该可信网卡上监听后才能进行本机 LAN 真机验收。

镜像归档不包含原版 NAS 数据。尚未导入或运行 NAS 试用容器，尚未启动 Windows 预览客户端；部署需要确认新服务将通过现有 HTTPS 入口可达。
