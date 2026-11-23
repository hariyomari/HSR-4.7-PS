# 旧版协议归档

[English](README.md) | 简体中文

4.4、4.5、4.5v3 和 4.6 的非活动参考文件以无损 ZIP 保存。正常 4.7 构建不需要解压；活动 `Proto/4.7` 保持原样。`Proto/4.5v3/translations.txt` 被登录回归（`LoginSceneRegression` 的同种子检查）直接读取，仍以原文件保留，不在 ZIP 中。

## 需要旧版定义时

迁移工具的历史默认值指向 4.4，旧示例可能同时需要 4.4、4.5 或 4.6。执行前解压所需版本到 **Proto/**，不要覆盖已有编辑。

Linux/macOS（仓库根目录）：

```sh
unzip -n Proto/Archives/4.4.zip -d Proto
unzip -n Proto/Archives/4.5.zip -d Proto
unzip -n Proto/Archives/4.5v3.zip -d Proto
unzip -n Proto/Archives/4.6.zip -d Proto
```

Windows PowerShell：

```powershell
Expand-Archive -LiteralPath Proto/Archives/4.6.zip -DestinationPath Proto
```

按需替换 ZIP 文件名；不要加 `-Force` 覆盖本地修改。解压目录被 Git 忽略，`4.5v3/translations.txt` 仍由 Git 跟踪。编辑历史参考资料时，显式更新对应归档并核对差异；不要误以为被忽略的编辑已经提交。回退创建这些归档的瘦身提交也可以恢复原跟踪文件。

## 完整性

每个 ZIP 保留原始版本目录和文件字节，全部文件已逐项与原 Git blob 核对。条目按路径排序、固定时间戳为 1980-01-01，保留 Unix 权限元数据，使用 Deflate level 9。

| 归档 | 文件数 | 原字节 | ZIP 字节 |
|---|---:|---:|---:|
| `4.4.zip` | 176 | 3260769 | 944490 |
| `4.5.zip` | 158 | 2211561 | 591808 |
| `4.5v3.zip` | 10 | 2227157 | 609624 |
| `4.6.zip` | 161 | 5051550 | 1353489 |

SHA-256：

```text
85948b7b5a11c6faa56af3d4f1a937d62a36937436986d9d545aecd858b26284  4.4.zip
f1e0808624798f13f48d000cd12abc396e51fe6e7843201f8eb722ae5fe15799  4.5.zip
0c8146856475dff215ba44bf449b722a1e9b050671e2685acb1b927defc7da27  4.5v3.zip
da80e364b7dbdbf1c83b9f040e8e2b83b314f45f69c71505406899f2da3f1f0a  4.6.zip
```

归档只减少默认工作区体积，不清除 Git 历史中的原始文件。
