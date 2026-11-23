# 账号清理与设备 / IP 封锁

[English](account-security.md) | 简体中文

本指南面向需要批量删除账号、封锁设备或 IP，或者在反向代理后、以 Dispatch 与 GameServer 分离方式部署服务器的管理员，同时说明升级后的认证行为以及这些功能背后的管理接口。

后台入口为 `/admin/index.html`，使用管理密钥登录。

## 批量删除

在「账号」中按用户名、UID、IP、设备 ID 等搜索，再勾选账号。表头可选择本页，「选择筛选结果」可选择全部匹配账号；翻页保留选择，更换搜索词或筛选条件会清空选择。

点击「批量删除」后，核对 UID 与用户名名单并输入 `DELETE`。每批最多 500 个账号。操作会撤销令牌、等待在线会话断开，然后在事务内删除每个账号及其游戏数据。结果分别显示成功、已不存在和失败的 UID；失败项可重试。整个批次不是一个事务，一个账号失败不会回滚其他账号的成功删除。

删除不可恢复。系统保留不含账号资料的 UID 占用记录，避免旧会话延迟写入重建数据或把已删除 UID 分配给其他人。独立设备/IP 封锁和全局限流计数不随账号删除。单账号删除使用同一套流程。

## IP 与设备记录

账号列表显示最近 IP；资料页显示注册 IP、最近 IP、当前游戏连接 IP 和注册时间。安全页列出近期 IP、设备 ID 以及已有的设备指纹，可直接选择要封锁的值。CSV 导出包含 IP 和时间字段。

IP 来自服务器连接或可信反向代理；成功注册和认证通过的请求才会补充可读历史。近期 IP 和设备 ID 各保留最多 16 条。旧数据只有哈希，不能还原历史 IP，后续登录会逐步补充；旧设备哈希仍可用于封锁。

## 封锁规则

账号封禁只作用于指定 UID，不会因为另一个账号使用过相同设备、指纹或 IP 而扩散。需要限制某台设备或某个网络出口时，单独创建下面的设备 / IP 封锁规则。

在「设备」中输入 IP 或设备 ID，填写原因以及可选时长（如 `1h`、`7d`）。留空为永久。支持 IPv4、IPv6 精确地址，IPv4 映射 IPv6 会归一化；不支持 CIDR 网段或通配符。

规则独立存入数据库，重启及删除相关账号后仍然有效。添加规则会撤销已知匹配账号的令牌并断开其会话；注册、密码登录、令牌验证、Combo 换票和游戏入服都会检查封锁。已知设备被封锁的账号无法通过省略设备 ID 绕过检查。到期规则停止生效，管理员也可手动解除。

IP 封锁会影响同一公网出口的其他玩家。设备 ID 由客户端提供，能够被修改；这些限制不构成不可伪造的物理设备认证。

`testdevicefp`、`asd9123cfa767`、`38d7ecd67b187` 等已确认的公共占位指纹，以及常见空值、全零 ID，不参与设备匹配、注册身份限制或登录限流。其已知历史哈希也会被忽略，不能用这些值或哈希新建封锁规则；数据库中遗留的此类规则显示为无效。其他设备和 IP 规则继续生效。

拒绝认证时，`Auth` 模块以 WARN 记录入口、UID（认证前未知时为 `unknown`）、来源 IP、判定类型（`account_ban` / `device_block` / `ip_block`）、命中范围（`uid` / `current` / `known_device`）、独立规则 ID、命中的身份哈希及到期时间。日志不包含密码、令牌或原始设备 ID。密码验证失败不会被记录成账号封禁。

此次行为调整不需要修改数据库。部署前备份程序和配置；分离部署同时更新 Dispatch 与 GameServer 后重启。回滚可恢复原程序和配置，但旧版会重新启用跨账号关联封禁及占位指纹匹配。

## 升级后的认证行为

- 自动建号执行与注册接口相同的注册开关、设备要求、身份数量限制和注册限流检查。并发注册检查与建号处于同一个临界区。
- Combo 登录只接受本服签发的有效令牌，已移除仅凭客户端外部 UID 自动建立 `os_*` 账号并免验证登录的路径。以前依赖该路径的客户端需要先完成本服账号认证。
- 无密码账号不能再由首次登录者自行设置密码，需要管理员设置。自动建号也不能跳过已启用的邮箱验证要求；要求验证邮箱时，应通过正式注册页面提交真实邮箱。
- 普通机型、设备名称、应用 ID 不再被当作唯一设备标识。
- 数据库启动时自动补充新增字段和表，无需手工迁移。更新程序后重启生效。
- 玩家控制台（`/player/*`、`/console/*`）、`/sr-tools` 与 `/sr-tools-export` 通过同一个共享校验器检查 UID 和密码。没有密码的账号被拒绝；处于登录限流封锁或 IP / 设备封锁中的客户端在检查密码之前即被拒绝；每次错误密码都计入登录限流；被封禁的账号被拒绝。
- `/sr-tools-export` 改为 `POST`，使用 JSON 请求体 `{"username":"<uid>","password":"<password>"}`。旧的 `GET ?uid=&password=` 形式返回 405，密码不再出现在 URL、代理日志或浏览器历史中。用 `GET` 下载导出数据的脚本或工具需要相应更新。

### 防滥用限制

`ServerOption.Auth.AntiAbuse` 下的开关（`RateLimitEnabled`、`LimitRegistrationPerIdentity`、`LoginRateLimitEnabled`、`EmailRateLimitEnabled`；`RequireRegistrationIdentity` 原本就是关闭的）默认关闭，自建服务器的玩家注册和登录不受限流。公开服务器在 `Config.json` 或后台 **设置 → antiabuse** 中打开需要的开关；次数、时间窗口和封锁时长保持原值。

- **已有安装保留原值。** `Config.json` 会在启动时写回，因此旧版本生成的文件中这些开关仍是打开的。
- **回退：** 把开关重新设为 `true` 即可，没有其他依赖。

### DefaultPermissions

`ServerOption.DefaultPermissions` 会在角色权限之上授予每个账号。其内置默认值现为空；以前为 `["*"]`，使每个注册玩家都成为管理员（包括 `command.account` 与 `command.others`）。

- **已有部署保留原值。** 启动时会读取并回写 `Config.json`，因此旧版本生成的文件仍包含 `"DefaultPermissions": ["*"]`，仍授予全部权限。启动时以及在后台修改该值时，若其中包含 `*` 或内置 User 角色没有的任何权限（例如 `command.*`、`command.account`、`command.others`），服务器会输出警告日志。
- **迁移：** 先备份 `Config.json`，将其设为 `"DefaultPermissions": []`（或只保留低风险权限），并通过 `PermissionRoles` / 账号角色为管理者授予权限。重启生效，或在后台访问控制页面修改后立即生效。
- **回滚：** 恢复备份或重新设为 `"DefaultPermissions": ["*"]` 即恢复旧行为，启动警告也会重新出现。将程序回退到旧版本不会改变已有的 `Config.json`；由新版本首次生成的文件在旧程序下仍保持 `[]`。

## 反向代理与分离部署

`HttpServer.TrustedProxies` 只填写直接连接本服务的反向代理 IP，默认是 `127.0.0.1` 和 `::1`。配置为 `[]` 时完全忽略转发头；外部代理需要显式添加，可在后台「设置 → 网络」修改，重启后生效。

仅可信代理的 `X-Forwarded-For` 和 `X-Forwarded-Proto` 会被接受，最多处理最右侧一跳。反向代理须正确覆盖或追加客户端实际 IP，不能原样透传客户端伪造的单一转发值。多级代理应由直接相连的可信代理提供最终客户端地址。

Dispatch 与 GameServer 分开运行时，两端必须设置一致且非空的 `ServerOption.ServerConfig.ServerExchangeSecret`。GameServer 在受共享密钥认证的 `/validate_account_token` 请求中附带实际玩家 `clientIp`，Dispatch 据此检查 IP 封锁。未配置共享密钥的跨服查询与令牌交换返回 401。分离部署应同时更新两端。

## 管理接口

以下接口均要求管理员 Bearer 会话或 `X-Admin-Session`。

| 方法与路径 | 请求 / 结果 |
|---|---|
| `POST /admin/api/accounts/bulk-delete` | `{"uids":[10001,10002],"confirm":"DELETE"}`；返回 `deleted_uids`、`missing_uids`、`failed` |
| `GET /admin/api/identity-blocks` | 返回封锁列表，包含类型、值、原因、到期时间及是否有效 |
| `POST /admin/api/identity-blocks` | `{"kind":"ip","value":"192.0.2.1","reason":"滥用","permanent":true}`；设备类型为 `device` |
| `POST /admin/api/identity-blocks` | 定时示例：`{"kind":"device","value":"device-id","permanent":false,"duration":"7d"}` |
| `DELETE /admin/api/identity-blocks/{id}` | 解除指定封锁，`id` 使用列表返回的规则 ID |

封锁已有哈希时，以 `identity_key` 替换 `value`，两者不能同时提供。API 同样验证类型、地址格式和时长；重复封锁同一规范化标识会更新该规则。

隔离回归测试及浏览器测试夹具见 [账号安全回归](../testing/auth-regression.zh-CN.md)。
