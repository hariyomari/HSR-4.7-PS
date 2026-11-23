# 账号安全回归

[English](auth-regression.md) | 简体中文

`AuthRegression` 在一次性数据库上检查账号注册、登录、管理员鉴权、封禁、令牌和账号删除。修改账号、鉴权、封禁、令牌、删除或数据库并发相关代码后运行。

项目：[`scripts/AuthRegression/`](../../scripts/AuthRegression/)

## 运行方式

从仓库根目录执行：

```powershell
dotnet run --project scripts/AuthRegression/AuthRegression.csproj
```

需要在浏览器中检查后台页面时，追加 `--serve`：

```powershell
dotnet run --project scripts/AuthRegression/AuthRegression.csproj -- --serve
```

程序先执行回归，通过后在 `http://127.0.0.1:21001/admin/index.html` 启动隔离的后台测试夹具，使用测试密钥 `local-auth-regression-only` 登录。夹具带有超过一页的合成账号与 IP/设备记录，全部可以丢弃。完成后按 Ctrl+C 停止，不要将此实例暴露到本机以外。

## 前提条件

只需要 .NET SDK。回归会新建临时 SQLite 数据库和独立的测试配置，不读取项目的 `Config.json` 或任何真实玩家数据库。

## 覆盖范围

### 账号与登录

- 旧表迁移。
- 注册与登录绕过尝试；并发注册。
- 管理员鉴权。
- IP 归一化与转发头；分离部署下的玩家 IP 检查。

### 封锁、令牌与删除

- 独立的设备/IP 封锁、令牌撤销、批量删除和事务回滚。
- 断开会话、延迟写入，以及存库与删除并发。
- 重载后的封锁和删除记录。

### 账号封禁

- 账号封禁只影响该 UID。共享同一 PC 设备或公网 IP 的其他账号仍可通过密码登录、验证令牌、Combo 换票和注册。
- 被封禁的 UID 更换设备后仍被拒绝。
- 独立的设备/IP 规则继续拦截，包括省略设备 ID 时的历史设备检查。
- 公共占位指纹及其旧哈希会被过滤。
- 拒绝日志能区分账号封禁与独立规则，且不包含密码、令牌或原始设备值。

### 账号邮件链接

- `Email.PublicBaseUrl` 为空或不是绝对 http(s) 地址时，拒绝发送密码重置邮件。
- 设置后，即使请求带有伪造的 `Host` 头，重置链接也只使用该地址。

### 网站游戏账号接口

- 开关与密钥；代理转发后的来源地址。
- 用户名唯一且不区分大小写；并发创建。
- 改密会吊销令牌；封禁与解封。
- 关闭注册后的外部注册跳转。

## 预期结果

运行结束时输出：

```text
Auth regression failures: 0
```

模拟的游戏会话没有真实 KCP 传输，断开测试会记录预期的发送失败日志。

## 已知缺口

- 所有检查都使用合成账号和临时数据库，不能替代 PC 或安卓客户端实机测试。
