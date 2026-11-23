# 背包消耗回归

[English](inventory-regression.md) | 简体中文

`InventoryRegression` 检查物品移除、商店购买、物品与遗器合成以及使用物品在发放任何东西之前校验数量和余额。修改 `InventoryManager` 的移除逻辑、`ShopManager`、合成或使用物品后运行。

项目：[`scripts/InventoryRegression/`](../../scripts/InventoryRegression/)

## 运行方式

从仓库根目录执行：

```powershell
dotnet run --project scripts/InventoryRegression/InventoryRegression.csproj -c Release
```

预期输出 `Inventory regression checks: <n>, failures: 0`。`scripts/verify-security.py` 会把它作为安全入口的一部分运行。

## 前提条件

- 不需要游戏资源：程序向 `GameData` 填入合成的物品、商店、合成配方、遗器和奖励数据。
- 使用临时 SQLite 数据库和隔离配置，不启动网络服务。只接入背包和商店管理器，因为完整的玩家初始化需要真实资源表。

## 覆盖范围

- `RemoveItem` 拒绝非正数数量，且不会把货币扣成负数；`TryRemoveItems` 只有在合并后的每项消耗都为正、不超出 int 范围且付得起时才会移除。销毁 `0xC4653600` 个星琼、销毁 0 个或销毁超过持有数量都会被拒绝。
- 商店购买拒绝 `GoodsNum` 为 0 和 `0xFFFFFFFF`，拒绝余额不足的购买，付得起时扣除全额。
- 物品合成在普通配方中忽略客户端发送的消耗，特殊材料只接受配方 `SpecialMaterialCost` 列表中的物品且数量必须恰好相符，执行 `MaxCount` 上限，并拒绝没有任何消耗的配方（缺少公式字段的旧缓存）。
- 遗器合成只接受配方 `RelicList` 中的遗器、该遗器主词条组内的主词条，以及最多两个互不相同、属于其副词条组且与主属性不同的副词条。指定副词条时 `WrItemList` 中每件遗器至少需要一个树脂。按配方扣除消耗，客户端消耗不能增加货币。
- 使用物品要求持有所用数量，并在发放奖励之前先移除物品。

## 未覆盖

请求处理器本身、真实资源数据以及真实客户端的合成请求。尤其是指定副词条额外需要的树脂数量不在资源表中，服务器只要求每件遗器至少一个树脂。
