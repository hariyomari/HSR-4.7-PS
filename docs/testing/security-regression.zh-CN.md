# 安全回归入口

[English](security-regression.md) | 简体中文

`scripts/verify-security.py` 是不需要私有游戏资源的安全回归的统一入口。提交鉴权、Web 服务、KCP 传输、协议生成或依赖相关的修改前运行；加 `--with-resources` 时还会运行需要真实资源缓存的套件。

## 运行方式

从仓库根目录执行：

```sh
python3 scripts/verify-security.py
```

选项：

- `--with-resources`：额外运行 LoginScene、GridFight、DailyActive 回归。需要 `Config/Resource.bin`；文件缺失时直接失败，不会把跳过报告为通过。
- `--skip-build`：复用已经验证过的解决方案构建。

## 前提条件

- .NET SDK 10 和 Python 3。
- 使用 `--with-resources` 时：与当前程序版本匹配的 `Config/Resource.bin`。
- Linux 部署 mock 套件只在 `bash` 和 `flock` 可用时运行，否则报告为 SKIP。

## 覆盖范围

入口按顺序执行：

- `scripts/check-comments.py`。
- 校验 `KcpSharp/CmdIds.cs` 与活动协议版本的命令号快照 `Proto/4.7/CmdIds.cs`（版本由 `Proto/Proto.csproj` 的 `ActiveProtocolVersion` 选定）逐字节一致。
- 解决方案的完整 Release 构建。
- Auth 与 Web 的鉴权和会话安全回归、KCP 来源绑定、握手分流与握手容量回归（`KcpSecurityRegression`、`KcpAdmissionRegression`）、背包消耗回归（`InventoryRegression`）以及 `SourceGenRegression`。
- 协议生成回归 `scripts/test-proto-generation.py`。
- Linux 更新脚本的失败与回滚场景（`scripts/test-update-server.py`），使用隔离的 mock 命令测试，绝不操作真实的 systemd 服务。

测试只使用临时数据库、内存资源和各回归程序自己的隔离配置，不部署任何服务。

### UDP 会话边界

KCP conversation ID 只用于路由，其他来源 IP 或端口不能再向既有会话注入数据。两种通道都在解析和入队前核对创建时记录的 endpoint。测试只向内存接收队列注入合成数据，不发出网络流量。客户端 IP 或端口变化后必须重新握手，这与通道固定的发送目的地址一致。

### 依赖补丁

WebServer 显式固定 `System.Security.Cryptography.Pkcs` 10.0.13，覆盖 MimeKit 传递依赖引入的旧最低版本。Microsoft 公告列出的修复线为 10.0.13：

- https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7
- https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx

项目没有已确认可达、会处理攻击者 CMS 数据的入口。该补丁消除已知的依赖风险，不把它描述为本项目已验证的远程漏洞。

## 预期结果

入口最后输出 `PASS: requested security gate; skipped resource suites are NOT validated`，退出码为 0。不带 `--with-resources` 时还会输出 `SKIP: LoginScene/GridFight/DailyActive resource suites (use --with-resources)`；被跳过的套件并未经过验证。

## 已知缺口

- 不加 `--with-resources` 时，依赖资源的 LoginScene、GridFight、DailyActive 回归会被跳过。
- 客户端实机、Windows/macOS、NativeAOT 及真实业务数据升级仍须单独验收。
- UDP endpoint 检查不是加密身份认证，无法抵御能伪造同一来源地址的网络攻击；公网部署仍需要防火墙或上游流量限制。
