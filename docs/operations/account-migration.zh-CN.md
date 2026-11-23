# 把其他服务器的账号和存档并入本服

[English](account-migration.md) | 简体中文

本指南面向需要把另一台 March7thHoney 服务器的玩家迁到本服的运维人员，说明迁移工具会改动什么、如何演练和执行，以及如何回滚。

## 概述

`scripts/AccountMigration` 把另一台 March7thHoney 服务器的 SQLite 数据库（账号 + 全部存档）合并进本服数据库。2026-09-27 曾用它把另外两台服务器的主实例并入一台正式服务器。

## 迁移规则

- 被封禁的账号不迁；`AuthAttempt`、`IdentityBlock`、`DeletedAccount` 不复制。
- 新 UID = 旧 UID + 该来源的偏移量；和目标库已有 UID（含已删除账号的墓碑）冲突时，改用 30000 起的空闲 UID。
- 用户名（不区分大小写）已被占用时加 `_<来源名>` 后缀；邮箱重复时，后迁的账号换成 `uid<新UID>@web-account.invalid`（仍可用用户名登录）。
- 存档二进制里引用的其他玩家 UID（好友、黑名单、申请、聊天、助战、挑战归属、蛋糕点赞）按同一映射改写；指向未迁玩家的引用删除；系统资料卡 UID 和全服聊天 UID 保持不变。
- 游戏会话令牌清空，玩家用原密码重新登录。
- 源数据里重复键的字典（旧版本写坏的行，服务端本身读不出来）会保留每个键的最后一个值后写回，并打印出是哪一行。
- 所有写入在一个事务里；写完用服务端自己的实体类型把每一行的每个 MemoryPack 列反序列化一遍，全部成功才提交。

## 步骤

1. 在来源服务器上用 SQLite 在线备份做快照（不停服），传到目标服务器。
2. 在目标库的副本上演练：
   `dotnet AccountMigration.dll --target copy.db --source other=other.db:10000 --report report.json`
   每个来源写成 `--source <来源名>=<数据库>:<UID 偏移量>`。
3. 停服务，备份 `Config/Database`，对线上库执行同一命令，恢复文件属主后启动，检查启动日志。
4. `report.json` 含每个账号的新旧 UID、用户名、邮箱和密码哈希，用于在网站上建账号；用完删除。

## 其他用法

- `--validate <db>` 只检查一个库能否被当前服务端读取。
- `--check-password <salt> <hash> <iterations> <password>` 校验密码哈希。

## 回滚

停服务，把备份的 `Config/Database` 目录内容拷回，启动。
