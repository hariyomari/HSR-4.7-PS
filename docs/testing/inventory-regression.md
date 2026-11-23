# Inventory cost regression

English | [简体中文](inventory-regression.zh-CN.md)

`InventoryRegression` checks that item removal, shop purchases, item and relic synthesis and item use validate counts and balances before they grant anything. Run it after changing `InventoryManager` removal, `ShopManager`, synthesis or item use.

Project: [`scripts/InventoryRegression/`](../../scripts/InventoryRegression/)

## How to run

From the repository root:

```powershell
dotnet run --project scripts/InventoryRegression/InventoryRegression.csproj -c Release
```

Expect `Inventory regression checks: <n>, failures: 0`. `scripts/verify-security.py` runs it as part of the security gate.

## Prerequisites

- Needs no game resources: the program fills synthetic item, shop, compose, relic and reward rows into `GameData`.
- It uses a temporary SQLite database and an isolated configuration and starts no network service. Only the inventory and shop managers are wired, because a full player initialisation needs the real resource tables.

## What it covers

- `RemoveItem` rejects non-positive counts and never overdraws a currency; `TryRemoveItems` removes nothing unless every merged cost is positive, fits an int and is affordable. A destroy of `0xC4653600` Jade, of zero items or of more than owned is rejected.
- Shop purchases reject `GoodsNum` 0 and `0xFFFFFFFF`, reject a purchase the balance does not cover, and charge the full price of an affordable one.
- Item synthesis ignores client cost entries for normal recipes, accepts special-material payments only from the recipe's `SpecialMaterialCost` list and in the exact amount, enforces `MaxCount`, and rejects a recipe without any cost (an older cache without the formula columns).
- Relic synthesis accepts only a relic from the recipe's `RelicList`, a main affix from the relic's main-affix group, and at most two distinct sub affixes from its sub-affix group that differ from the main stat. Designated sub affixes cost at least one resin per relic in `WrItemList`. The recipe cost is charged; client cost entries cannot add currency.
- Using an item requires owning the used count; the items are removed before any reward is granted.

## Not covered

The packet handlers themselves, real resource data and the real client's synthesis requests. In particular, the extra resin price of designated sub affixes is not in the resource tables; the server only requires at least one resin per relic.
