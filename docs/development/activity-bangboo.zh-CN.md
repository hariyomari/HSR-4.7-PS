# 邦布联动活动「Planarcadia Reverie」（ActivityBangboo）

[English](activity-bangboo.md) | 简体中文

> **本分支不编译此功能。** 由于其命令与消息在当前使用的 4.7 协议中缺失或不一致，ActivityBangboo 已从构建中排除（[`GameServer/GameServer.csproj`](../../GameServer/GameServer.csproj) 中的 `<Compile Remove>`）。其 `Config/Custom/ActivityBangboo.json` 仍随服务器发布并会被加载。

本文说明 4.8 新增的邦布联动活动在服务端的实现：未命名的协议、配置来源、服务端执行的规则、活动任务，以及每项对应关系的证据。修改该活动或为新客户端重新导出其配置之前请先阅读。

## 概览

该活动于 4.8（OSBETAWin4.7.51 客户端）新增。客户端模块 `RPG.Client.ActivityBangboo`，活动面板 `50147`，活动模块 `5014701`（活动）与 `5014702`（常驻）。关卡战斗是客户端里的邦布对战玩法（GameCore 中的 `BangbooVsChenling`），整局在客户端运行。服务端只保存进度：已解锁 / 已通关的关卡；无尽洞穴（Infinity）的开放状态、各洞穴最佳波次与可继续的一局（下一波及客户端保存的棋盘存档）；帖子的已读 / 点赞；以及看过的演出组。

| 内容 | 路径 |
| --- | --- |
| 玩法逻辑（玩家组件） | `GameServer/Game/Activity/ActivityBangboo/ActivityBangbooManager.cs` |
| 任务事件 | `GameServer/Game/Activity/ActivityBangboo/ActivityBangbooEvents.cs` |
| 请求处理器 | `GameServer/Server/Packet/Recv/ActivityBangboo/HandlerActivityBangboo.cs` |
| 响应与通知包 | `GameServer/Server/Packet/Send/ActivityBangboo/PacketActivityBangboo.cs` |
| 存档表 `PlayerActivityBangboo` | `Common/Database/ActivityBangboo/PlayerActivityBangbooData.cs` |
| 配置 | `Config/Custom/ActivityBangboo.json`，模型 `Common/Data/Custom/ActivityBangbooConfig.cs`，启动时由 `Common/Data/CustomConfigFiles.cs` 读取 |
| 活动任务 | `GameServer/Game/Mission/FinishType/Handler/MissionHandlerActivityBangboo.cs` |
| 排期 | `Config/Custom/ActivityConfig.json` 中 `5014701` / `5014702`，面板 `50147` |
| 回归 | `scripts/LoginSceneRegression/ActivityBangbooRegression.cs` |

## 协议

4.8 dump 中命令目录 `MFNJLCNMBON`（10602–10640，15 条）整组未命名，消息与字段保持混淆名，没有写进 4.8 master。方向、配对与字段含义全部来自客户端代码，证据见本节末尾。所有响应中 `JKCOAIGJMMM` 都是 retcode。

| cmdId | 消息 | 方向 | 客户端发送方 / 读取方 | 字段 |
| --- | --- | --- | --- | --- |
| 10604 | `FDFNDGOOCGJ` | 请求：获取数据 | `CBAPNPOMOII.RefreshAsync` | 无 |
| 10627 | `BGCOEFMFEHA` | 响应 | `CBAPNPOMOII.CHNCBFKGKGM` 等 | `FJCAKOIDONG` 数据（`ABCMHMIHJBO`） |
| 10634 | `MGJEKPCMNDD` | 请求：阅读帖子 | `ReadAsync` | `CBGODLHCLPK` 帖子 ID |
| 10632 | `MHJNBHCDPHK` | 响应 | `CBAPNPOMOII.CMEEJACADNG` | `AMFEFIDDMDA` 帖子（`IHNJGNPKKOA`），帖子 ID 必须与请求一致 |
| 10616 | `HBHPDMLDLBL` | 请求：点赞 / 取消 | `SetLikeAsync` | `CBGODLHCLPK` 帖子 ID，`IAJAOMAKAHA` 是否点赞 |
| 10637 | `NIIFNPFEKIG` | 响应 | `CBAPNPOMOII.NGOIFHEHHAJ` | `AMFEFIDDMDA` 帖子 |
| 10606 | `HKIFIOMLLCE` | 请求：开始关卡 | `MAOBJJGOCJB._StartPreparedGameAsync` | `IJAPHCOHMIN` 关卡，`OEDLOIPDGKG` 选择的邦布 |
| 10640 | `BOBEICIPCNK` | 响应 | 同上，`DMHBBCKDLKA.KIGKKCNBNAN` | `IJAPHCOHMIN` 关卡（必须回显且非 0），`OJHNAJCAKNB` GameTransNo（不能为空白），`IFDOJGKJEBN` 无尽一局（`JDKBCJPLNHF`，仅无尽关卡） |
| 10638 | `BNMEGDLCIKO` | 请求：演出看完 | `OOOOHFOLEIA._FinishPerformanceAsync` | `CIGGAEJNEFM` 演出组 ID |
| 10602 | `PPHGPJDEHDA` | 响应 | `CBAPNPOMOII.PPHLLOLDENN` | `CIGGAEJNEFM` 回显演出组 |
| 10620 | `MBCCKEIJAGF` | 请求：结算 | `NFFAPNKNMBF.LPCDOLBHIKN`（`_SettleAsync`、`SaveAsync`、`_ExitAsync`、`AbandonInfinityRunAsync`） | `IJAPHCOHMIN` 关卡，`OJHNAJCAKNB` GameTransNo，`PKKMAAIJEOD` 原因（`FDHNDGJCOAC`），`FFCBOJAACMD` 统计（`MPAJNPPLKMP`），`BBEMIGELGJC` 棋盘存档（`JDCCDGMJMII`） |
| 10633 | `EBHKOMEILOJ` | 响应 | `DMHBBCKDLKA._SettleAsync`、`NNIFKNCJELA.JKGONFAKGLB` | `IJAPHCOHMIN` 回显关卡（不一致时客户端跳过结算界面），`ENGHMIAMPLG` 奖励（`ItemList`），`HPKEGPGPPOK` 该洞穴历史最高波次 |
| 10635 | `FFPHBNAGJNP` | 请求：结束无尽一局 | `_SettlePreparationAsync`、`_EnsureEndlessRunClearedAsync` | `IDCGICECPHO` 最终统计（`IHFFJHNEFMF`） |
| 10611 | `EEJOPBDAHNJ` | 响应 | 同上 | 仅 retcode |
| 10622 | `LIEHHGLAEHG` | 通知 | `CBAPNPOMOII.BOEOIIPLFDA` | `DGNGMJMPFGK` 变化列表（`CAGDONKMOGB`） |

### 数据 `ABCMHMIHJBO`

| 字段 | 含义 | 客户端去向 |
| --- | --- | --- |
| `EEJKJMCMJBA`(2) | 已解锁关卡 | `OEOKJAHHFOJ.OIGDABFJCHJ(unlocked, finished)`：`IsUnlocked = 在任一列表中` |
| `ICHOFAHCIIF`(6) | 已通关关卡 | 同上，`IsFinished = 在此列表中`；故事洞穴的解锁 / 完成由其关卡推导 |
| `IEKHMACAFMK`(3) | 已看完的演出组 | `BDPNKGJPIPK.ADFFOCBJIII`：各分组 `IsRead` |
| `GILOPNKGNAA`(8) | 无尽洞穴已开放 | `CPMDJHNLEMG.LLEPNCCEILB`：所有 `HoleType=Infinity` 洞穴的 `IsUnlocked` |
| `LOBHIIILPCH`(11) | 无尽最佳 `MJMPBCBGJHH {IKJPIFBFEAG 洞穴, EMMGMLIBHGP MaxWave}` | 同上，洞穴 `MaxWave` |
| `IFDOJGKJEBN`(4) | 可继续的无尽一局 `JDKBCJPLNHF {IKJPIFBFEAG 洞穴, OHIDKMKBIGJ 波次, BBEMIGELGJC 存档}` | 同上，洞穴 `CurrentWave`；存档经 `JCLOJAFLJPN` 转成 `AHKCPHPIMHB`（Board、Statistics、SelectedBangbooIDs、HoleID、CurrentLevelID、CurrentEnergy）。为空时客户端按 0 处理 |
| `AAHPCIOOLHB`(15) | 帖子 `IHNJGNPKKOA {CBGODLHCLPK 帖子, BNBCGFOBIPH 已读, IAJAOMAKAHA 已点赞}` | 帖子 store |

### 变化通知 `CAGDONKMOGB`

这是一个 oneof：`CEMIOADEMHA`(9) 新解锁关卡（`NPFMFIKIDAC.LNMPJKPEHAE`），`FADGOOJBAKE`(6) 新解锁邦布（`FKDJGPFCDJK.LNMPJKPEHAE`），`GILOPNKGNAA`(10) 无尽洞穴开放，`BPJABGNNHDO`(14) 帖子。客户端逐项打上「新」标记（`GAKPNLDHION`、`ADOLOAABELD`、`EGKDNJKPDFL`、`GBNMBAMHPJF`），之后无条件调用 `RefreshAsync` 重新获取数据。

### 结算原因 `FDHNDGJCOAC`

`SaveAsync` 固定传 1，`_ExitAsync` 与 `AbandonInfinityRunAsync` 固定传 2，`_SettleAsync` 透传游戏结束原因。`NNIFKNCJELA.JKGONFAKGLB` 中非无尽关卡 `Kind = reason == 1 ? Victory : Failure`，无尽关卡一律 `EndlessFinished`，并用 `HPKEGPGPPOK` 作为 `EndlessScore.HistoricalMaxWave`。

### 统计 `MPAJNPPLKMP`

由 `NNIFKNCJELA.ENGIEAAAFDO` 从 `DEAHBFLFLED` / `HKICNHMIMAG` 的真名属性逐一填入：`CPFCKNMOKDL`(1) CartTriggerCount，`HPEODGIDKAN`(2) EnergyFromBangboo，`BJPIBINDNLF`(5) BangbooTotalValue，`ACGMHLIGFBA`(7) EnergyAtEnd，`LALKFADHJJL`(8) CostTime，`CBBALOIKCPC`(10) CurrentWave，`CBIBKFKHLMI`(11) TacticsBangbooKillCount，`IBJNHACMKLE`(12) EnergyFromLevel，`FPOCOJHBLPI`(14) MaxSimultaneousBangbooIdCount，`BEKNMJFKHHN`(15) / `PIIKPCFHGFI`(1219) CostPower，`EJCLFCEKFDB`(778) EvolveBangbooCount，`JMHAFKMFLII`(900) CombineBangbooCount，`LGJGNFBJKIH`(2036) PlaceBangbooCount，`CLICEAKBDJK`(173) BangbooPlaceCountList，`AECMCJCCMFO`(251) BangbooDeathCountList，`FDNLKGOOHHF`(1078) BangbooCountAtEndList，`FLLNGJGFFAF`(1596) BangbooCombineCountList，`IBAGICECKBG`(178) StartBangbooIdList。列表项 `MMPEJCJEEHO`：`HCDEOKNIGKG` Count，`BDCOFIOCJFO` Star，`FFPMBJPCAGG` 推定为邦布 ID。

### 无关的消息

`KKNAAFIBLLH`（10234）与本活动无关：它与 `DrawScRsp`、`EIEOFMBKAHM` 同属命令目录 `MCDALMNBOPN`，在 `DepletablePoolModule.OnLoginFinish` 一侧使用。

### 证据与方法

- 用 Demiurge `dump` 生成 dump.cs，再用引用 Demiurge 的反汇编小工具（PeImage + Metadata.Usages + Iced）按调用、类型与方法引用做注释。
- 请求方向：七条请求只在网络服务类 `NFFAPNKNMBF` 中构造；七条响应在包注册表 `<>c::<.cctor>b__2_137..143` 中注册；`LIEHHGLAEHG` 由 `CBAPNPOMOII.BOEOIIPLFDA` 按类型分派，是通知。配对来自 `NFFAPNKNMBF` 各方法的签名（如 `UniTask<BOBEICIPCNK> OFHMHPKEECI(uint, IReadOnlyList<uint>)`）。
- 方法真名来自调用方的异步状态机与 lambda（`<RefreshAsync>d__16`、`<ReadAsync>d__7`、`<SetLikeAsync>d__8`、`<_StartPreparedGameAsync>d__83`、`<_FinishPerformanceAsync>d__12`、`<_SettleAsync>d__76`、`<SaveAsync>d__3`、`<_ExitAsync>d__74`、`<AbandonInfinityRunAsync>b__15_0`、`<_EnsureEndlessRunClearedAsync>d__88`、`<_SettlePreparationAsync>d__75`）；状态机字段保留参数真名（`levelID`、`gameTransNo`、`reason`、`completedLevelID`、`groupID` 等）。
- 字段名：混淆器对同一原始标识符生成同一混淆名。对 dump 中所有带 `<X>k__BackingField` 的类，读取各属性 getter 的存储偏移，得到「混淆属性名 → 真名」表（约 5000 项），例如 `MNBBCAMDGDI` = IsUnlocked、`HCOFMFKFMDK` = IsFinished、`EMMGMLIBHGP` = MaxWave、`NBMAGDOJDFD` = CurrentWave、`BNBCGFOBIPH` = IsRead、`KGEJAOIMOBK` = IsLiked、`OJHNAJCAKNB` = GameTransNo、`FFCBOJAACMD` = Stt、`BBEMIGELGJC` = Archive。名字对不上的字段（`EEJKJMCMJBA` 等）由反汇编数据分发代码、比对 proto 属性的存储偏移确定。
- 校验条件全部出自反汇编：开始响应检查 retcode、`IJAPHCOHMIN` 与请求一致、`OJHNAJCAKNB` 非空白；无尽关卡（`BangbooPreparationKind.Infinity`）再由 `ODAGFOCMOBJ.KJPFECFJECA` 检查一局的洞穴与关卡所在洞穴一致、波次非 0 且等于关卡在洞穴中的序号 + 1、存档（非空时）的 `CurrentLevelID` 等于本关；帖子 / 演出响应检查回显 ID。

## 配置来源

关卡、洞穴、邦布与演出表都在客户端 ExcelOutput 中，但路径不在社区名单里，资源导出不会产出，行类型也被混淆。`Config/Custom/ActivityBangboo.json` 由 Demiurge 按行类型（数组、起始偏移 1）解码这些资源，再用 ID 交叉确认得到，文件内 `_source` 记录了资产哈希：

| 表 | 资产哈希 | 行类型 | 服务端使用的列 |
| --- | --- | --- | --- |
| `BangbooLevel` | `d01b5c63dfe31e77` | `NJPNGGOMFIG` | ID；`ENGHMIAMPLG` 奖励（客户端加载为 `RewardID`）；`BDGPDCKLFNF` 解锁子任务 |
| `BangbooHole` | `8a1a75b7512b217b` | `PLCFNJFELLE` | ID；`CFPOHAONOLM` HoleType；`ENIDMPHBPKF` LevelIDs |
| `BangbooConfig` | `71578b04fa76bb45` | `OHIEDOGFAEM` | ID；`GMPGDEINODK` Type；`AAMKNOOMEOO` UnlockLevel |
| `ActivityBangbooPerformance` | `9046f1b03d347d04` | `JPGKFBDAPLM` | `LLDCHLHNADA` GroupID |

交叉确认：关卡的初始邦布列（`ANKIMCODPOE`）只包含此前关卡已解锁的邦布（例如邦布 4 的 UnlockLevel 为 101，首次出现在 102）。`BDGPDCKLFNF` 在帖子行与演出分组行中被客户端加载为 `UnlockSubMissionID`（行偏移 0x34 → 模型 0x80、0x1C → 0x24），关卡模型不读取这一列，因此它只供服务端判断。`BangbooPost` / `BangbooPostComment`（`5b5accf53acf3ba2` / `e01022c2094c0e57`）在此版本中为空表，帖子 ID 取自 `BangbooReadPost` 任务的 ParamIntList。

该文件不进入 `Resource.bin`，每次启动从 `Config/Custom` 读取；客户端更新后需按同样方法重新导出。

## 规则

- 关卡解锁：故事洞穴（1、2、3）的关卡按洞穴 ID、洞穴内顺序组成一条链（101 → … → 304），每关需要前一关通关。不在任何洞穴中的关卡（100 教学、305/306 首领阶段）始终开放。无尽洞穴（4、5、6）的关卡随无尽开放。`requireUnlockSubMission` 为 true 时还要求关卡的 `unlockSubMissionId` 已完成；默认关闭，因为活动任务线（主线任务 8048117）在本服务端不完整，开启会使关卡无法解锁。
- 无尽开放：`infinityUnlockLevelId`（默认 304）首次通关后开放全部无尽洞穴（客户端只有一个开关）。
- 开始关卡：拒绝未解锁的关卡与未解锁 / 未知的邦布；生成 GameTransNo 并记录当前对局。无尽洞穴第 1 波开新的一局（丢弃旧局并记入最佳），之后的波次必须与保存的一局衔接，响应带上这局及其存档。
- 结算：必须对应当前对局（关卡与 GameTransNo），否则失败；同一对局不能重复结算。原因 1 视为通关：首次通关发放关卡奖励（RewardData），计算新解锁的关卡、以该关为 UnlockLevel 的邦布、无尽开放，在响应之后发送变化通知。
- 无尽一局：原因 1 记录已通过的波次为最佳，保存客户端发来的存档并进入下一波（第 12 波后本局结束）；原因 2（离开）保留上次保存的进度；其他原因结束本局。`FFPHBNAGJNP` 结束当前一局（无局时也返回成功）。
- 帖子：ID 须在 `postIds` 中；阅读记已读，点赞 / 取消点赞独立记录。演出组：ID 须在 `performanceGroupIds` 中，重复上报只记一次。

## 活动任务

FinishType 取客户端 `MissionFinishType` 枚举中的数值，加入 `Common/Enums/Mission/MissionFinishTypeEnum.cs`：

| 值 | FinishType | 服务端触发时机 |
| --- | --- | --- |
| 480500 | `BangbooLevelFinish` | 关卡首次通关，ParamIntList 为关卡（无尽洞穴中第 n 关即第 n 波） |
| 480501 | `BangbooReadPost` | 帖子首次阅读 |
| 480502 | `BangbooDeployCount` | 每次结算累加：BangbooPlaceCountList 中类型为 ParamInt1（1 战斗、2 战术）、星级在 ParamIntList（空为任意）中的放置次数 |
| 480503 | `BangbooMaxDeployCountInLevel` | 同上的单局计数，取最大 |
| 480504 | `BangbooFinishNpcGroupPerformance` | 演出组首次看完 |
| 480505 | `BangbooTacticsKillCount` | 每次结算累加 TacticsBangbooKillCount |
| 480506 | `BangbooMaxEnergyAtWin` | 通关时的 EnergyAtEnd，取最大 |
| 480507 | `BangbooMaxSimultaneousIdCount` | ParamIntList 中关卡的 MaxSimultaneousBangbooIdCount，取最大 |

使用这些类型的有活动任务 6094001–6094105、成就 4055801–4055805、任务 2000498 / 2000499。任务文本（如 “Upgrade Combat Bangboo #4 time(s) in total”、“Summon 4-star Combat Bangboo”、“Defeat the 3rd wave of enemies in any area in Infinite Mode”）用于确认参数含义。新增枚举值只有在用当前代码重新生成 `Resource.bin` 后才会出现在缓存的 `FinishWay` 行上；旧缓存里这些行的 FinishType 为 `Unknown`，任务不会推进。

## 回归

`scripts/LoginSceneRegression` 的 `ActivityBangbooRegression` 段经真实处理器覆盖：配置与排期、处理器与 FinishType 注册；新账号数据；锁定关卡、未知关卡、未解锁邦布被拒；GameTransNo 与关卡不符、重复结算被拒；离开不通关；首次通关的奖励与变化通知（新关卡、新邦布）及重玩无奖励；故事链解锁到 304 后开放无尽并通知；无尽第 1 波存档、第 2 波带存档开始、离开保留、失败结束、三波通关更新最佳、`FFPHBNAGJNP` 结束一局；帖子阅读 / 点赞与未知帖子；演出组；统计驱动的任务进度；存档表写入 SQLite 后读回。仓库中的 `Resource.bin` 早于新枚举，回归在内存中把相关 FinishWay 行设为对应类型后再验证，结束时恢复。

## 限制

- 推断而非抓包确认的部分：`FDHNDGJCOAC` 3、4 的含义（按「其他原因」处理，无尽一局结束）；无尽中原因 2 保留进度（`_ExitAsync` 与 `AbandonInfinityRunAsync` 都传 2，无法区分，放弃一局依赖客户端随后的 `FFPHBNAGJNP`）；最佳波次按已通过的波数计；故事链解锁顺序与开放无尽的关卡（304）；离开 / 失败的结算中统计同样计入任务；`BangbooDeployCount` 按放置列表而非合成列表计数；`MMPEJCJEEHO.FFPMBJPCAGG` 为邦布 ID。
- 未实现：变化通知的帖子分支（服务端没有会主动变化的帖子）；按活动排期开关关闭玩法（与骰子收容一致，未检查活动是否开放）。
- 帖子表在此版本为空，客户端可能不显示帖子。
- 未经客户端实机验证。
