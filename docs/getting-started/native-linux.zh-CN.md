# 原生 Linux 部署

[English](native-linux.md) | 简体中文

这是最简单的部署方式：一份检出、一个服务器进程，不用 Docker，也不拆分 dispatch/游戏节点。安装 .NET 之后，Linux 可以像 Windows 一样直接从源码运行。

## Debian 12/13

一次性安装服务器依赖：

```bash
sudo apt-get update
sudo apt-get install -y git
git clone <repo-url> March7thHoney
cd March7thHoney
bash scripts/setup-debian.sh
```

项目面向 .NET 10。在 Debian 12 和 13 上，Microsoft 支持的软件包源为 x64 和 Arm64 提供 `dotnet-sdk-10.0`。

## 运行

直接从检出目录启动服务器：

```bash
dotnet run --project Program/Program.csproj
```

Linux 路径区分大小写，所以 `Program` 可用而 `program` 不行。也可以使用根目录的启动脚本：

```bash
bash Start.sh
```

首次启动时，如果 `Config.json` 不存在，程序会写出该文件。除非之后有意拆分服务，否则保持如下单服务器设置：

```json
{
  "ServerOption": {
    "ServerConfig": {
      "RunDispatch": true,
      "RunGateway": true,
      "Regions": []
    }
  }
}
```

## 在 systemd 下运行

从检出目录安装服务：

```bash
sudo bash scripts/install-systemd-service.sh
sudo systemctl start march7thhoney
```

常用命令：

```bash
journalctl -u march7thhoney -f
sudo systemctl restart march7thhoney
sudo systemctl stop march7thhoney
```

需要交互式命令控制台时，在 `tmux` 或 `screen` 里运行 `bash scripts/run-server.sh`，而不是使用 systemd。

## 更新

在检出目录中执行：

```bash
git pull --ff-only
dotnet run --project Program/Program.csproj
```

对于由 `install-systemd-service.sh` 安装的 systemd 单元，请使用更新助手，而不是直接拉取到正在运行的检出目录：

```bash
bash scripts/update-server.sh --restart
```

以检出目录/服务的所有者身份运行该助手，不要用 sudo 运行整个脚本（它只在控制服务时请求 sudo）。助手要求检出目录干净且有上游分支。首次使用这种托管发布流程之前，先把 `update-server.sh` 和 `run-server.sh` 作为同一个已提交的版本一起更新。当前提交和候选提交都必须带有带版本号的已发布运行器约定；较旧的运行器会在触碰服务之前被拒绝，而不是尝试一次需要重新编译源码的不安全回滚。自定义服务单元必须像随附的安装脚本那样调用本检出目录的 `scripts/run-server.sh`。

它会获取一个可快进的候选版本，并**在停止服务或修改已部署的检出目录之前**，在临时的分离 worktree 中发布它。因此还原或构建失败不会影响正在运行的版本。第一次托管更新时，它还会把旧提交发布为回滚快照。暂存期间需要额外的磁盘空间以及 SDK/网络访问，但不会运行游戏服务器。

发布产物保存在检出目录的 Git 目录下的 `march7thhoney-deploy/releases/` 中，其中的 `current` 符号链接以原子方式切换。`run-server.sh` 直接执行这个预先构建的 DLL，不再还原或编译；工作目录仍是检出目录根目录，因此数据库、日志、配置和外部资源保持原有位置。直接使用 `dotnet run` / `Start.sh` 仍是从源码构建的流程，**不会**使用选定的已发布版本。

如果检出目录激活或紧接着的 systemd 启动失败，助手会恢复之前的发布版本，并尝试用 `git reset --keep` 回到旧源码；它不会丢弃部署期间做出的编辑。之前正在运行的服务随后会从旧的发布产物重新启动。失败的更新保留非零退出状态，回滚失败会被明确报告。这不是健康检查：systemd 处于 active 状态并不能证明游戏服务器已就绪，也捕获不到检查之后才发生的崩溃。

更新之前，备份数据库、`Config.json`、自定义配置和资源。可执行文件回滚**不会**撤销数据库/资源迁移，也不会撤销短暂启动的候选版本所做的更改。对于这类版本，请遵循其迁移专用的恢复步骤。如果恢复源码被并发编辑阻挡，请检查这些编辑，而不是使用 `reset --hard`。保留的发布快照会占用磁盘空间；某次更新被确认可用之后，可以手动删除较旧的未使用快照，保留 `current` 和一个已知可用的回滚版本。

`--no-restart` 准备/选定下一个发布版本，但从不停止或启动 systemd。`--no-build` 只作用于源码，**仅在与 `--no-restart` 一起使用时**才被接受；它不会替换选定的已发布版本。更新暂存期间避免编辑检出目录。更新锁会阻止助手被并发调用。

回归检查使用临时仓库和模拟的 .NET/systemd 命令，从不触碰你真实的服务：

```bash
python3 scripts/test-update-server.py
```

## 网络

开放 `Config.json` 中的 HTTP dispatch/网关端口：

```json
"HttpServer": {
  "BindAddress": "0.0.0.0",
  "PublicAddress": "example.com",
  "Port": 21000,
  "UseSSL": false
}
```

开放 `Config.json` 中的 UDP 游戏端口：

```json
"GameServer": {
  "BindAddress": "0.0.0.0",
  "PublicAddress": "example.com",
  "Port": 23300
}
```

如果由反向代理终止 HTTPS，在程序中保持 `UseSSL` 为 false，并代理到 `http://127.0.0.1:21000`。UDP 游戏流量仍需要单独的转发规则。

## UDP 握手准入限制

未认证的 KCP 握手会在账号登录之前分配传输队列。监听器现在在注册锁内、分配这些队列之前检查准入。不会为了给握手腾出空间而驱逐现有会话。适用以下 `GameServer` 设置（所示为默认值）：

```json
{
  "MaxConnections": 4096,
  "MaxPendingConnections": 256,
  "MaxPendingConnectionsPerIp": 16
}
```

`MaxConnections` 包含所有已注册的连接，无论处于活动还是待定状态。另外两个限制统计尚未进入已认证 ACTIVE 状态的连接。登录成功会释放其待定配额；正常断开/注销会释放所有相应容量。重复端点会以原子方式检查，包括 IPv4 映射的 IPv6 地址。非正数设置会回退到默认值，而不是取消限制。被拒绝的握手不会获得新会话，可以稍后重试；不要期望在登录之前收到经过认证的错误响应。

对于学校、共享网络或运营商 NAT，如果有超过 16 个用户需要同时登录，请提高单 IP 待定上限。它**不会**限制共享该 IP 的已活动玩家总数。根据主机内存和预期负载调整全局限制；这些上限防止无限制的分配，但不能替代上游的 UDP 速率限制，也不能保证洪泛期间的可用性。回归使用模拟的连接快照和并行的加锁/检查/注册序列，而不是真实套接字：

```bash
dotnet run --project scripts/KcpAdmissionRegression/KcpAdmissionRegression.csproj -c Release
```
