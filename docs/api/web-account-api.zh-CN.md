# 网站游戏账号接口（WebAccountApi）

[English](web-account-api.md) | 简体中文

本接口供受信任的网站在本服务器上开通和管理游戏账号，面向在服务器旁运行这类网站的运维人员和对接网站的开发者。

## 概述

网站负责注册、邮箱验证和邀请码，本接口只把「已验证的网站用户」变成一个能登录游戏的账号。实现见 `WebServer/Controllers/WebAccountRoutes.cs`，回归见 [账号安全回归](../testing/auth-regression.zh-CN.md)。

## 配置

`Config.json` → `ServerOption.Auth`：

```json
"Auth": {
  "EnableRegistration": false,
  "ExternalRegistrationUrl": "https://example.com/register",
  "WebAccountApi": {
    "Enabled": true,
    "Secret": "<随机长字符串，与网站上配置的共享密钥一致>",
    "AllowedAddresses": ["127.0.0.1", "::1"]
  }
}
```

- `Enabled` 为 false 或 `Secret` 为空时，所有接口返回 404。
- `AllowedAddresses` 按**经过受信代理还原后的**客户端地址判断。同机反向代理（Caddy/nginx）转发来的公网请求带 `X-Forwarded-For`，还原后不是 127.0.0.1，因此即使拿到密钥也会被拒绝。列表为空表示不限地址，此时密钥是唯一防线。
- `EnableRegistration: false` 关闭游戏内置注册（含 AutoCreateUser），网站开通的账号不受影响。配置了 `ExternalRegistrationUrl` 时，`/login-platform/register.html` 直接 302 到该地址。
- 建议反向代理层额外拒绝公网访问 `/external/*`。

## 鉴权

每个请求带请求头 `X-March7thHoney-Web-Secret: <Secret>`（常量时间比较）。失败返回 401 `{"retcode":-1,"message":"Unauthorized","error":"unauthorized"}`。

## 响应格式

成功响应统一为：

```json
{"retcode":0,"message":"OK","data":{"account":{
  "uid":10001,"username":"WebPlayer","email":"a@b.c","nickname":null,"level":null,
  "banned":false,"ban_reason":"","online":false,"created_at":1790000000,"last_seen_at":0}}}
```

错误响应为 `{"retcode":-1,"message":"...","error":"<code>"}`，`error` 是稳定的错误码。

## 接口

前缀 `/external/web-account/v1`。

| 方法与路径 | 请求体 | 说明 / 错误码 |
| --- | --- | --- |
| `GET /status` | — | `data: {server_name, version, accounts, online, ready}`，用于健康检查 |
| `POST /accounts` | `{username, password, email?}` | 创建账号，邮箱标记为已验证。`invalid_username`（3–32 位 `A-Z a-z 0-9 _ . -`）、`invalid_password`（服务器密码策略）、`invalid_email`、`username_taken`（409，**不区分大小写**）、`email_taken`（409）、`not_ready`（503，数据库仍在加载） |
| `GET /accounts/{uid}` | — | 查询账号；`not_found`（404） |
| `PUT /accounts/{uid}/password` | `{password, revoke_tokens?=true}` | 改密码，默认同时吊销所有登录令牌；`invalid_password` |
| `POST /accounts/{uid}/ban` | `{reason?}` | 永久封禁并踢下线 |
| `DELETE /accounts/{uid}/ban` | — | 解封 |

`email` 省略时服务器生成占位地址 `<username 小写>@web-account.invalid`（服务器要求邮箱唯一；同一个网站用户的第二个游戏账号可以用这种方式）。UID 由服务器按现有规则分配，玩家数据在首次进游戏时创建。
