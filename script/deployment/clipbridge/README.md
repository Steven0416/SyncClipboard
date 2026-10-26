# ClipBridge NAS kit · 0.2.0-preview.1

这是全新安装模板，不含任何现有用户的 NAS 地址、账号、密码或数据。

完整中文指南：[全新 NAS、Windows、iPhone 部署](https://github.com/Steven0416/SyncClipboard/blob/feat/lan-first-shortcuts/docs/CLIPBRIDGE_DEPLOY_ZH.md)。
从 Release 下载的 ZIP 里也包含独立副本 `部署指南.md`。

## 最短部署流程

1. 在 [Release](https://github.com/Steven0416/SyncClipboard/releases/tag/v0.2.0-preview.1)
   下载中继镜像 `ClipBridge-Relay-0.2.0-preview.1-linux-x64.tar.gz`，适用于 DS220+ 等 Linux x86-64 NAS。
   这不是 Windows ZIP，也不是旧版 SyncClipboard Server 镜像。
2. 在群晖 Container Manager 的 Image / 映像页从文件导入镜像；导入后确认标签为
   `clipbridge-relay:0.2.0-preview.1`。也可通过 SSH 执行 `sudo docker load -i 镜像文件路径`。
   文件必须完整上传，先核对 Release 提供的 SHA256。
3. 把本目录放到独立目录，例如 `/volume1/docker/clipbridge`。复制 `.env.example` 为 `.env`，
   用文本编辑器填写自己的专用用户名和新生成的强随机密码。不要保留 `REPLACE_...`，不要使用 DSM 管理员密码。
   上传时确认 `.env` 没有变成 `.env.txt`，并保留以点开头的文件。
4. 在 Container Manager → Project / 项目 → Create / 新增，选择这个目录和 `compose.yaml`。
   项目名可用 `clipbridge`。使用已导入的本地镜像启动，不要选择从远端拉取镜像。
   SSH 等价命令（在本目录执行）：

   ```sh
   sudo docker compose --env-file .env -f compose.yaml config --quiet
   sudo docker compose --env-file .env -f compose.yaml up -d --pull never
   ```

   老版环境若只有 `docker-compose`，可使用对应命令；推荐 DSM 7.2+ 的 Container Manager。
5. 配置有效证书的 HTTPS 反向代理（含 WebSocket），指向 `http://127.0.0.1:8787`。
   **不要把内部 8787 或容器 5033 映射到公网**。后续客户端统一填写该 HTTPS 基地址。
6. 按完整指南配置 Windows 和 iPhone。这里没有自动创建域名、证书、路由器端口转发或修改旧容器的脚本。

`data/` 是运行后生成的私有持久化目录。迁移和备份时一起保存 `.env` 与 `data/`；
公开分享只发原始模板。不要运行会显示完整环境变量的 `docker compose config` 并公开其输出。

## 从源码构建 / ARM64

Release 镜像仅提供 Linux x64。ARM64 NAS 需自己构建，不可导入 x64 镜像使用。
在能运行 Docker 的机器上获取本版本源码，然后以 **src 目录** 为构建上下文：

```sh
git clone --branch v0.2.0-preview.1 --depth 1 https://github.com/Steven0416/SyncClipboard.git
cd SyncClipboard
docker build --pull -t clipbridge-relay:0.2.0-preview.1 -f src/SyncClipboard.Server/Dockerfile src
docker save clipbridge-relay:0.2.0-preview.1 | gzip > ClipBridge-Relay-local.tar.gz
```

在目标架构机器构建会生成该架构镜像。在其他架构使用 Buildx 时，应安装对应构建器并明确选择
`--platform linux/arm64 --load`（或 `linux/amd64`）。ARM64 安装尚未做真机验证。
本仓库未发布 GHCR/Docker Hub 中继镜像，不能把上述镜像名当作可远端拉取的仓库。

只更新指南和模板时，现有 Release 标签中的服务源码仍可构建本协议版本；当前模板源码也可从
`feat/lan-first-shortcuts` 分支取得。
