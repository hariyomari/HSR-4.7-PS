# GridFight regression

English | [简体中文](grid-fight-regression.zh-CN.md)

`GridFightRegression` plays Currency Wars (GridFight) runs in memory against the current resource cache and checks the server state and the messages built for the client. Run it after changing anything under `GameServer/Game/GridFight/`, the GridFight request handlers, the GridFight resource models or the run save.

Project: [`scripts/GridFightRegression/`](../../scripts/GridFightRegression/)

## How to run

From the repository root:

```powershell
dotnet run --project scripts/GridFightRegression/GridFightRegression.csproj
dotnet run --project scripts/GridFightRegression/GridFightRegression.csproj -- --cache <Resource.bin>
```

- Without options the regression reads the current `Config/Resource.bin`.
- `--cache` first copies the given cache to `tmp/GridFightRegression/Cache/` in the repository and reads that copy. Logs go to `tmp/GridFightRegression/Logs`.

Changes to shared code should also build the whole solution:

```powershell
dotnet build March7thHoney.sln -c Release --no-restore
```

## Prerequisites

- A resource cache with GridFight tables. The season comes from `GridFight_Season_ID` in the cache, and the test roles come from that season's role pool.
- Season-specific checks run only with a matching cache: the Silver Wolf / Fate Diviner selection queues and the damage-over-time contract only in season 1; the season 2 trait checks only in season 2. When the cache uses the new native skill layout, `BetaTableRegression` runs first, followed by the full regression; older caches still run the original equipment, wealth diamond, investment and selection queue tests.
- The regression uses no player database and never writes the resource cache back. Test state and extra test equipment live only in the process. The run-save checks at the end create a SQLite database in the system temporary directory (`m7h-gridfight-regression-*`) and delete it afterwards.
- **Battle buff rows.** `MazeBuffExcel` also loads `GridFightTraitMazebuffS2.json` and `GridFightSpotLightMazebuff.json` (both optional). Caches built before these tables were registered lack 93 battle buff records used by Black Hole Gravity and the Clara modules. Upgrade such a cache offline into a file that does not exist yet:

  ```powershell
  dotnet run --project scripts/GridFightCacheUpgrade/GridFightCacheUpgrade.csproj -c Release -- Config/Resource.bin <resources directory> <new output file>
  ```

  The tool re-reads its output and checks that everything except the added buff rows is unchanged. Run the regression with `--cache <new output file>` before copying it over `Config/Resource.bin`, back up the original first, and roll back the program and cache together.
- **Building a cache.** When missing tables and rows are filled from an older cache, GridFight tables always come from the new dump and are never filled from the old cache (an older cache contained 14 season 1 affixes that must not be mixed in). The `PetConfig.json` of 4.6.52 has obfuscated field names, so the 4.6.51 version was used when building that cache. See [Rebuilding the resource cache from a client export](../operations/resource-cache-from-client-export.md).

## What it covers

### Core run flow

- Every selectable portal opening and its protobuf round trip; equipment tracking; wealth diamonds; dice; the selection queue; a complete route of 20 battles; periodic rewards; repeated requests; aborted settlement.
- Portals and contracts, investment rewards, Galaxy Scholar, Fate Diviner, the Silver Wolf upgrade choice, equipment and trait tracking, shop locking and the related sync messages.
- Auto-equip checks equipment locks, wear conditions and slots in one place; an invalid explicit target is not moved to another character.
- Batch equipment-tracking updates validate before committing; the completion state is derived from what is actually worn, so forged or stale client state cannot affect auto-equip.
- NPC equipment is locked at creation, so the tracking system cannot first put it on a player character.
- Manual and automatic wealth diamond crafting share one recipe set; the shop filters for max-star characters and switched forms are kept; an empty candidate pool is handled.
- Dice binding and post-battle rerolls are kept; temporary equipment deletion is synced; temporary equipment cannot be moved on its own and is reclaimed when its character is sold or merged.
- Equipment produces gold per preparation phase; a repeated preparation request does not pay twice.
- Silver Wolf and Fate Diviner choices are published one at a time so they never occupy the same queue position; during a choice the target character cannot be moved or sold and no battle can start.
- Periodic investments are hooked to node callbacks, and the interval counter resets after Gold Futures triggers.
- Handlers use the current `CmdIds` constants.

### Seasons and season isolation

The 4.6.51 resources (4.7 test server) set `GridFight_Season_ID` to 2, and season 1 and season 2 rows share the same tables: `GridFightRoleBasicInfo` (season 2 role ID = avatar ID × 100 + 2, plus cost-tier variants), `GridFightTraitBasicInfo`, `GridFightEquipment` / `Consumables` / `Forge`, and `GridFightCraftConfig` (the same pair of basic equipment crafts different items per season, for example 350201+350201 → 35030101 / 47030101). `GridFightSeasonTalent` reuses the same IDs (2011–4061) for the season 2 talents.

- `GridFightSeasonItem.json` and `GridFightSeasonCraft.json` (item/craft → season) are loaded and evaluated by `GridFightSeasonScope`; old caches without these two tables treat everything as shared.
- The shop, opening, supply, orbs and rewards (random role, random equipment, specific role), forge, dice, crafting and the handbook use only current-season content. `SpecificAvatar` and the `101000+ID` role orbs resolve the avatar ID to the current season's role.
- 4.6.51 removed `GridFightCamp.SeasonID` and `RoleBasicInfo.RoleSavedValueList`: camps without a season are shared; without the SavedValue column the role pool uses the season 2 rules (excluding `IsFakeAvatar` and the Aha cost tiers 151112/151122/151132; the seven-cost tier is priced from row 7 of `ShopPrice`).
- Season 1 mechanics apply only in season 1: the Cyrene/Silver Wolf LV999 check, the restricted role seeds, the 4.5 shop rarity table, the end-of-round healing of season talent 2011 and the 3560xxxx talent battle buffs, and the orb reward of talent 4032 (`SeasonIds` in `GridFightRewardRules.json`).
- `GridFightFormationWave` has an extra 5-member wave with the ID omitted (0); battles still use wave 5.
- Season 2 rolls one companion per run (`GridFightPetPal.json`, skipping companion 1, which has no effect) and one of its effects, sent through the companion sync in `GridFightGameInfo` (field 1984; entry field 9 is the companion, 8 the effect) and in the settlement `GridFightFinishInfo` (field 1836). The client's opening flow reads the first companion effect directly; without it a null reference makes "start run" do nothing.
- Run settlement (win or settle on quit) advances the `GridFightFinishGamePlay` mission, parameter 1 for the standard game and 2 for the overclocked game; the overclocked game is unlocked by `GridFight_OC_UnlockQuest` (7302115).
- The map console (`Prop_Common_GridFightConsole_01`) branches on `ByCompareMainMissionState(8037100)`; an omitted state takes the class default `Finish`. When `GridFight_Tutorial_Main_Mission_Id` is finished it opens the new entrance, otherwise the old season 1 page. The server reports that mission state faithfully; coverage is in `LoginSceneRegression`.
- The standard route depends on the season: `GridFightStageRoute` has no season column, and season N's division routes start at N×1000 (season 1: 1000–1800 and 1801, 23 nodes; season 2: 2000–2800 and 2801, 16 nodes). Routes below 1000 are tutorial / first-play routes.
- The 4.6.51 client builds stage nodes only from the route nodes sent in field 12 of `GridFightLevelInfo` (chapter 5, section 1; in the params, 2 is the stage, 7 the penalty rules, 8 the buff node, 12 the basic gold, 14 the node type). Without them the current node is empty, the progress bar shows 0-0 and the preparation page cannot start a battle.
- In season 2 routes an elite branch can follow a supply node directly; after the supply round begins the run enters the elite encounter choice.
- Consumable add/remove syncs must carry the nested `GridFightConsumableInfo` (field 13 of `GridFightConsumableUpdateInfo`); the client reads only that field.
- The season 2 level table adds level 11, reachable only through effects. Ordinary EXP (buying, battles) only reaches `GridFight_DefaultPlayerLevelMax` (10); level 10 has no level-up EXP and cannot roll over into 11.
- Supply cards (`GridFightSupplyRoleInfo`) must carry the role drop in field 13 (`GridFightDropEntry`, role / 1 star), with equipment in the drop list of field 6. The client's `GridFightDraftItemData.Create` reads field 13 unconditionally; without it a null reference leaves the run stuck in the settlement of a supply node (for example 1-3).
- Season isolation checks: role pool, portal and investment pools, 300 shop refreshes, supply, 8 openings of every resolvable orb, forge, random equipment, crafting, the handbook, dice filling, and the roles, equipment and offers that appear on a complete route.

### Season 2 traits

The protocol structures come from disassembling the 4.6.51 client, not from captures. Rules the client does not contain (goods pools, trigger nodes, thresholds) follow the trait descriptions and are marked as guesses in the code. Tables that are not in `Resource.bin` (template equipment, Phainon's flames, Clara modifications, Chimera, the Trailblaze shop, the Jade black market, Elation templates) are generated into `GridFightSeasonTwoTables.cs` by a script kept outside the repository.

- Interactions the client starts go through `GridFightTraitUpdateCsReq` (8657). The client locks input until it receives `GridFightTraitUpdateScRsp`, so a failure must also reply. Server prompts go through pending actions.
- After each Currency Wars request `GridFightHandler<T>` refreshes the season 2 state and sends only the effect snapshots (`GridFightTraitSyncInfo`) that changed, and only for activated traits; a sync for a trait the client does not know causes a null reference.
- Five-cost unique traits:
  - Dan Heng trait (双龙吟): when Imbibitor Lunae (饮月) enters the field, Permansor Terrae (腾荒) of the same star level is summoned in the background; it takes no population slot and cannot be sold or moved forward. One of summon / heal / combo / counter is chosen and granted to Imbibitor Lunae.
  - Phainon trait (盗火行者): dragging a character to Phainon converts it into flames, stored in the character save value `GP_Avatar_S2_Phainon_00`. The list of Chrysos Heirs (黄金裔) uses the official list in `GridFightTraitConstValueS2.json` (including 141302), read optionally.
  - Ranger trait (巡海游侠): while 不死途 is on the field it appoints one front-line character; at 3 stars, the whole front line.
  - Jade trait (黑市主理人): the signature item in slot 1 never sells out; rotating goods are generated by Jade's star level.
  - Clara (机心巧匠): modifies immediately on entering the field, then once per node for the first 3 times and once every 2 nodes afterwards.
  - Aha trait (幻乐之主): see [Aha](#aha) below.
- Other traits: Trailblaze (开拓银轨: Trailblaze Power earned per battle, the Trailblaze shop, train upgrades as battle MazeBuffs); 千星商会 (tier 1 refreshes give EXP, tier 2 gives a free refresh per node); Chimera (奇兽花园: 1 energy per node, a free recruit the first time each tier is reached); Moon Game (幻月游戏: a random rule per run, wish power accumulating with battles); Stellaron Hunter (星核猎手) and 黄金大饭店 (a template chosen by weight per run, template equipment granted by layer); Member Link (梦境交织); Counter King (反击王); Xianzhou (仙舟舰队: basic equipment upgrades in battle); Elation (欢愉: the Aha equipment bar, dragging equipment in to merge with simple equipment).
- Trait table changes in OSBETAWin4.7.51 that take effect from the tables: Elation `200702`'s top tier changes from 4 to 5 members; tier 4 of Xianzhou `1001021` replaces 3 instead of 2; `101202` gains effect `1012022` (TraitEffectByJson).
- Stellaron Hunter Script Equipment (`TraitEquipmentRegression`) is granted and taken back with the trait level (the hotel's dishes keep their behaviour). It is marked as temporary equipment (`IODIJBEHEGF`): wearable, but it cannot be crafted, rerolled, upgraded or consumed by Aha or Clara. Script Equipment granted in old saves gets the mark at the next refresh.
- Season 2 trait checks (season 2 caches only): Permansor Terrae summoning, population, sell and move restrictions and leaving with Imbibitor Lunae; the Dan Heng choice and trait ownership; Phainon devouring and the flame save value; Ranger appointment; Jade black market slots and the signature item; Trailblaze shop goods and post-battle Trailblaze Power; Clara's first modification; Chimera free recruits; the GetData protocol round trip and ContextValue with all five-cost unique traits on the field at once.

### Aha

- Aha tiers are read from tables. The OSBETAWin4.7.51 role table removes the 3-cost `151102`; `GridFight_Aha_No_Star_Role_IDList` is `151112`, `151122`; `GridFight_SevenPriceRoleIdList` is `151132`; `GridFight_InitialExceptRoleIdList` no longer contains `151112`, so the 4-cost `151112` becomes the base form sold in the shop. The server derives the tiers from these constant tables (`GridFightResourceCatalog.AhaGrowthRoleIds`, `AhaSevenCostRoleId`) instead of hard-coding role IDs; the three-tier layout of 4.6.5x still follows its original rules.
- Growth: in the 4.7.51 client `GridFightAhaRarity`, `_GetFormRarity` shows 4-cost below growth 5 and 5-cost otherwise, and both `_SevenCostGrowthLevel` and `MaxGrowthLevel` are 5. The two-tier layout therefore becomes 5-cost at growth 5, growth caps at 5, and 7-cost still needs the 5-cost form plus the star condition of trait `300602` (parameters `[10, 12]`, unchanged). In the three-tier layout, the 3/4/5-cost tiers do not gain stars; surplus copies become growth and raise the cost, and level 9 plus the combined star level of Elation / Moon Game raises it to 7-cost.
- When a 7-cost Aha already exists, other Aha copies become 1-star 7-cost and merge and star up normally.
- With the Elation trait active, Aha items appear in the shop at the 3-cost tier probability even without Aha on the field.
- Growth is also written to the character-level battle SavedValue; absorbing a copy without a tier change still syncs that character (`EconomyEffectsRegression`).
- Aha goods: `GridFightSeasonTwoTables.AhaGoods` is regenerated from the 4.7.51 `GridFightSpecialGoods` (GroupID 2 and 3), 122 rows. 开拓之旅 (347) and 哈基维利 (447) grant 150 and 500 Trailblaze Power as in the table.
- Elation equipment (`ElationUiRegression`): config ID sync, unlock state, battle consistency and save restore for Aha's initial equipment and for equipment after merging.

### Investment strategies and environments

Investment strategies are augments (投资策略); investment environments are portals (投资环境).

- Portals allow 1 free reroll: `free_reroll_count` in the pending info is fixed at 1 and `reroll_count` records the rerolls used (as for investments); a second reroll fails and leaves the candidates unchanged.
- An investment rerolled away does not come back in another slot of the same choice.
- After a supply round begins the run moves to the next node by the general rule (elite encounter, investment, Gold Futures, supply), not only the elite branch; Gold Futures due right after a supply node used to be postponed.
- When the remaining investments cannot fill an offer, the quality draw returns `None` and the investment node goes straight back to preparation instead of throwing.
- Lucky Token · Silver (130901) grants 350107 at the third node; it is a `GridFightConsumables` consumable, so it is granted as a consumable and synced through `UpdateGridFightConsumableList` (it used to be granted as equipment and failed silently).
- Lobby effects (`GridFightAugmentEffects.cs`, `GridFightPortalEffects.cs`) are based on the descriptions found by looking up the `HexDesc` / `PortalBuffDesc` hashes of the 4.6.51 `GridFightAugment` / `GridFightPortalBuff` in the 4.6.0 live TextMapCHS (a hit means the text did not change), with values from the 4.6.51 `EffectParamList`:
  - On-gain rewards (gold, EXP, free refreshes, squad HP and its cap, random N-cost or star-level roles, random simple/advanced/radiant equipment, random emblems, specific items and arms crates) are granted one by one; if one fails (for example a full bench) only that one is skipped and the rest are synced.
  - Node triggers: every node entered, after N nodes, for the next N nodes, every N nodes, entering the boss node, after the boss battle, start of a plane.
  - Free refreshes are counted on the server and used before gold; while free refreshes remain, the shop's `refresh_cost` is sent as 0 (the protocol has no separate free-refresh field).
  - Portals: 红钻/蓝钻贵族, 战力提升, 战力飙升 (except attribute growth), 人才储备 opening roles, 特权阶级, 二手市场, 长线利好, 彩虹/黄金/白银时代, 头彩/尾彩, 成功经验, 策略大师 (except the extra investment choice).
- Season 2 investments (`GridFightSeasonTwoAugmentEffects.cs`) have no public text; their descriptions were read from the in-game investment handbook and matched to `GridFightAugment` rows by handbook order and `EffectParamList`.
- In OSBETAWin4.7.51 the season 2 investment list shrinks from 307 to 298 entries. 103601, 204001, 204101, 304001, 304101, 304201 and 304301 remain only in the season 1 list, and their effect configuration is kept. 212001 and 312001 (emblem set `355100600`) are deleted entirely and removed from `EmblemSetAugments`. The effect table is shared by both seasons, so the regression asserts that every configured investment is in some season's list, and that investments are offered only from the current season's list.
- Invitation portals 2101–2119 grant their trait's emblem; in 4.7.51 their parameter changed from the emblem group to the emblem item ID, with the same result. Concept-stock portals 2201–2214 are implemented in `GridFightConceptStockPortal.cs`.
- New season 2 portals 149–152 and 1015 (4.7.51) enter the portal pool and pass the opening and message round-trip checks.
- Economy strategies (`EconomyEffectsRegression`), checked with real settlement, successful and failed spending, free refreshes, extra investment choices and their reroll counts, purchase messages, character battle fields, reward rollback and old-save restore:
  - 利息上调 (300801): the interest cap at battle settlement rises to 10.
  - 开源节流 (300101): the interest cap rises to 9; if no gold was spent in the previous node, the new node pays the maximum interest; the spending state is saved. Settlement and the basic info share `CurrentMaxInterestGold`, and a change of the cap sends the full basic info: 85 gold gives interest 8, 90 gold or more gives 9.
  - 采购专员·金 (201201): every 7 manual refreshes produce a full row of characters with the same cost as the leftmost bench character; it shares the candidate filter with the 彩 version. Its counter sync key follows the naming of the 彩 version.
  - 采购专员·彩 (303101): every 5 accumulated manual refreshes (including free ones) produce 5 characters with the cost of the leftmost bench character at trigger time; with an empty bench or no sellable character of that cost the normal shop is used. Automatic refreshes and failed requests do not count; the count survives re-login and is synced to the client as `Augment303101_SavedValue01`. The triggered full-shop offer does not mix in Aha special goods and still respects role limits and the max-star filter (`ThreeEffectsRegression`).
  - 市场干预 (102201): the first and every 4th, 8th, … manual refresh produces a full row of 3-cost characters.
  - 四费晋升 (202201): the next purchased 1-star 4-cost character becomes 2-star at the original price; once only.
  - 返利 (101201): 3 gold back for every 3 five-cost characters bought; progress is saved.
  - 淘金客 (301601): 2 EXP whenever gold is actually spent on a shop refresh; free, automatic and failed refreshes give nothing.
  - 商业间谍 (300201): buying EXP costs 1 gold less; on level-up the shop refreshes for free and the 3 most expensive characters are granted.
  - 成长基金 (101701): 40 gold on reaching level 9, once per save.
  - 白银投资 (203201): opens a silver 3-choose-1 immediately and again after 3 nodes, then returns to the original flow. Each card has 3 rerolls: the client treats `free_reroll_count`/`reroll_count` as each card's limit and used count (MaxRollTimes/RollTimes of the 4.6.51 `GridFightAugmentSelectItem`). Ordinary investments have 1 reroll per card.
  - 晋升名额 (301301): after each successful EXP purchase, one unsold promotable character offer is replaced by a character one cost higher, keeping the slot and star level and repricing for the new character/star. The role pool, max-star exclusion and Silver Wolf form restrictions apply. The existing purchase response already carries the shop sync, so no new protocol is needed. No replacement happens without the strategy, without enough gold, during battle, at the level cap, with an empty shop, when everything is already top cost or with no suitable candidate; the EXP purchase itself is still charged and applied normally.
- 战力提升 (136) (`ThreeEffectsRegression`): the opening character it grants is marked; on each normal entry into a new node it gains one random permanent attribute, synced to preparation and battle. Star-ups keep the mark and the accumulated value; buying another copy does not copy the effect. Attribute pool: attack, HP, damage, front-line strength, back-line strength; each step adds 5% (permille value 50).
- 战力飙升 (137): the opening characters use the same growth mark; star-ups and saves keep the growth.
- 苦战寻宝 (120202): only the bound character gets 20% healing and shield strength, the same in preparation and battle. When an encounter meets its original settlement condition, one extra reward of the same tier is drawn, keeping the original card reward and preview. Combined with 免费升舱 the extra reward also uses the upgraded tier; if the effect is lost or the target no longer exists there is no extra reward.
- Trailblaze property goods: the purchase counts of the 22 `Property` goods apply to preparation and battle attributes, stack across repeated purchases and are saved. The battle goods 110000021–110000027 have their resource rows, so purchased battle goods pass the existing checks and are sent to battle.

### Spotlights

Spotlights (主角光环) are implemented in `GridFightSeasonTwoSpotLight.cs` and `GridFightSpotLightEffects.cs`; the spotlight table is one of the 4.6.51 tables hard-coded in `GridFightSeasonTwoTables.cs`.

- 刷新视界 (100100) and 刷新视界·进阶 (100106) (`SpotLightEffectsRegression`): when the holder fought in the last battle, entering a new node gives 1 free refresh; each manual shop refresh adds front/back-line strength, 1.5% for the normal version and 2% for the advanced one. Free refreshes count; automatic shop filling does not. The two versions save their counts separately.
- 纳米改件 (100101): in the battle equipment list, the holder's first advanced equipment that appears in the upgrade table is temporarily replaced by its privileged version. The inventory, instance IDs and preparation equipment do not change; without eligible equipment nothing is generated.
- Black Hole Gravity (黑洞引力, 100900, `BattleEffectsRegression`): for a fielded spotlight character the real battle buff 131000002 is sent, bound to `role_id`, using the original resource parameters (10 action value, 3 times per battle). It is not sent after the spotlight is lost, the character leaves the field or is sold. Evidence: the ability `Config/ConfigAbility/BattleEvent/GridFight/4.7/SeasonAbility/GridFight_SpotLight_01.json` in the resource dump selects the character by `RoleID`, checks the ultimate and the use count in `OnAfterSkillUse`, then delays battle event 62200; the binding key `role_id` corresponds to the original script hash 1535408766.
- New trait effect type `GainOtherRoleTrait` (4.7.51): trait `400502` (type Spotlight, effect `4005021`) loads.
- Spotlights that grant a trait count their role as a member of that trait; the client learns this only from `GridFightUnlockTraitMember` items (`GridFightGameItemInfo` oneof 1763).

### Owlbert

`OwlbertRegression`. Owlbert NPCs, their equipment and claim progress are saved with the run. If an old save already chose an Owlbert but has no NPC, restoring builds it and grants once; restoring again does not grant twice. The back row gains one slot, still within the total limit of 9.

- 金币大使叽米 (303201): NPC 901. The server accrues 1 gold every 500 ms, at most 200 gold per node. It reuses the existing heartbeat and gameplay requests for sync, so the UI may jump in batches; offline time does not accrue.
- 星徽大使叽米 (303301): NPC 902, carrying 3 locked wealth diamonds. Selecting it grants 9 copies of one random emblem, not repeated per node.
- 环保大使叽米 (332401): NPC 903, carrying 3 locked gold garbage bags, and also grants 9 usable gold garbage bags.

Rule references: [HoYoWiki overview](https://wiki.hoyolab.com/pc/hsr/entry/4646?lang=zh-cn), [community wiki investment strategies](https://honkai-star-rail.fandom.com/wiki/Currency_Wars%3A_Zero-Sum_Game/Investment_Strategy), [BWIKI investment strategy list](https://wiki.biligame.com/sr/%E6%8A%95%E8%B5%84%E7%AD%96%E7%95%A5%E4%B8%80%E8%A7%88). The community wiki gives the gold pace and the emblem count.

### Clara modules and battle buffs

`BattleEffectsRegression`.

- `MazeBuffExcel` registers `GridFightSpotLightMazebuff.json` and `GridFightTraitMazebuffS2.json` (see Prerequisites); the 93 records they hold are separate from the main `MazeBuff.json`.
- The 21 Clara modifications with a non-zero BuffID are wired into battle. For the counter, missile and summon upgrade chains only the highest current level is sent, so an older level never overrides it; independent add-on modules are kept. The Clara ability is in `OriginAbility/GridFight_Origin_300502_StageAbility.json` next to the spotlight ability. The cache contains Clara's original level 1 and level 2 data; battle uses the normal level 1.
- Covered: the independent MazeBuff table, the 21 Clara modules, upgrade replacement, Black Hole Gravity role binding, and leaving the field, losing the spotlight, selling and save restore.
- The season 2 module selection of the current season and the basic info protocol round trip.

### Rewards and orbs

- `AllAvatarByRarity` (new in 4.7.51): `GridFightBasicBonusPoolV2` 116–130, parameters `[rarity, star]`, laid out like the neighbouring RandomAvatar 101–115. It grants every role in the pool of that rarity.
- Random role reward pool: `RandomAvatar` and `AllAvatarByRarity` exclude the roles in `GridFight_InitialExceptRoleIdList`, as the shop does (season 1 is unchanged). Before, they took roles only by `IsInPool`, cost and star rows, so a random 5-cost reward could hand out Dan Heng • Permansor Terrae `141402` (which exists only as the 双龙吟 summon) or Aha's upgraded forms (`151122`, `151132` in 4.7.51; also `151112` in 4.6.52). The regression draws 80 times per RandomAvatar reward row: `141402`, `151122` and `151132` used to appear, now none do; it also checks that every upgraded form is in that list. Fake roles (for example season 2's Pom-Pom king 604202) do not enter the shop pool but can still be granted as rewards, and `TryAddRole` in the native layout no longer rejects a fake role without an AvatarConfig.
- Orbs: 121006/121007/121008 dropped by burst orbs map to rewards 21006–21008, which are in no table; they are opened as the general orb of the same colour (199/204/305). "Open all" skips an orb that cannot be opened instead of failing the whole batch.
- Rewards generated in battle (reported by the client in field 5 of `GridFightBattleResultStatistics`, for example the scavenger of season talent 4061) grant orbs as reported: only orbs the server can open, at most one per kill, minus orbs of the same kind already granted by plan in that battle. The server logs the reported content at Debug level.

### Encounters, 免费升舱 and Pepe Cameo

- Encounter (EliteBranch) cards used to show their drops without granting them. Now a perfect clear, or progress reaching the penalty rule threshold, grants the card's reward through the investment effect queue at the start of the next round.
- The official encounter reward table is server-side and not public. The wiki only says that low tiers give a little gold, EXP or free refreshes, higher tiers give more and may give basic equipment or 4/5-cost characters, and difficulty 4 gives a wealth diamond and an advanced equipment of choice. Each quality 1–4 has a random pool built on that basis (the values are estimates), and quality 3, previously excluded, is allowed. Rewards are drawn when the encounter is generated, so the card shows what will be granted.
- 免费升舱 (103901): reward tier +1; at difficulty 4 one extra difficulty 4 reward is drawn.
- Pepe Cameo (佩佩客串, 331001): reuses the season 1 Pepe NPC (positions 7 and 11). For each battle node cleared perfectly it wears one locked advanced equipment (at most 3; overflow goes to the player); after 5 nodes it leaves and unlocks the equipment. Join, update and leave follow the capture sequence of the 狸智手续 NPC and are synced with the next round start.

### Bonfire

The bonfire (幕后茶歇) table has 84 option rows. Following the actual dispatch of `InitializeBonfireNodes` and `BuildBonfireOptions`, 63 rows can appear as options; the other 21 are parent-event placeholders, undispatched child events or unused configuration, so they are not options a player meets. Calling the real `SelectBonfireOption` for every row and comparing the full run snapshot before and after (ignoring the separate "already chosen" flag) gives:

| Dispatchable options | Count | Observed |
| --- | ---: | --- |
| Special-effect placeholders | 15 | Succeed and lock the chosen state, but change no reward, attribute or other run state; the branch is not implemented. |
| Coffee pool option 5 | 2 | Also succeed without effect. Their `OptionShowType` is `None`, so they bypass the existing random-coffee branch. |
| Commissions | 18 | Whichever commission is chosen, only the same orb 203 is generated; their own quest parameters are not used. Partially implemented. |
| Copy a character | 9 | A character is copied, picked by highest star level, highest cost or most equipment. |
| Coffees with explicit parameters | 8 | The reward and the HP cost are applied. |
| Other basic rewards | 11 | Equipment, consumables, gold or healing are applied. |

The ordinary rest button goes through `HealAtBonfire` separately and is not one of the 84 rows. The implementation is `ApplyBonfireOption` in `GameServer/Game/GridFight/GridFightSeasonTwoBonfire.cs`; the source tables are `GridFightBonfireEventOption.json`, `GridFightPrayQuest.json` and `GridFightPrayQuestFinishWay.json`.

### Highest division

The highest season 2 division is benchmarked against the season 1 A8 Wealth Creator 50 (财富造物主50) tier (`EconomyEffectsRegression`). `GridFightDivisionStage.json` contains that row as `10950`; the highest season 2 division in the cache is `20920`.

| Parameter | Season 2 highest, 20920 | Season 1 target, 10950 |
| --- | ---: | ---: |
| Base enemy difficulty | 89 | 120 |
| Base HP multiplier | 1.4 | 1.6 |
| Base attack multiplier | 24 | 48 |

Only the battle data of the highest division uses row 10950; the division, route, rewards and save outside battle keep their season 2 identity. Difficulty added by investments and encounters stacks on top of 120; other divisions are not affected. On the base curve of the same resources, without investments or encounters, chapter 1/2/3 HP is about 3.41/3.92/4.49 times and attack about 2.24/2.25/2.26 times that of the old highest division. Because the whole difficulty curve of that tier is used, speed also rises by about 14%/16%/18%; this is not a separate speed change. Final monster values also depend on templates, star level, waves and extra effects.

### Sync order

When one grant adds several roles (Talent Gift Pack (人才大礼包), orbs, Aha goods), a later role can land in a bench cell that an earlier merge in the same grant has just freed. The sync used to send every `AddRoleInfo` before the merges, so the cell was still occupied when the client received it: the cell looked empty while the server still had the role, and dragging into it returned "message fetch failed". Now each grant's new roles are sent right after their merge, and the season 2 follow-up sync, which used to leave out the merge steps of grants, includes them. The attribute refresh of `AppendRewardApplication` no longer updates roles of this grant that have not been sent yet. `SyncOrderRegression` replays the syncs in order on a client board model; the old order reports "role added on taken cell".

### Run persistence and quitting

- A run in progress is stored as a snapshot (`GridFightRunSnapshot`, MemoryPack, Base64) in `RunInstance` of the `GridFight` table, with the next run number in `NextSessionUniqueId`. The table is created automatically at startup and deleted with the other gameplay data when an account is reset.
- Saved at the end of every Currency Wars request (`GridFightHandler<T>`, also on failure), at battle settlement, on `QuitBattleCsReq`, on the `/grid` command and at logout; restored at login by `GridFightManager`. A snapshot that does not match the current season, refers to roles or monsters no longer in the resources, or cannot be parsed is discarded with a warning without affecting login. This includes boards, bench roles or shop cards whose role is not in the loaded role table (for example a run that stored `151102` under a 4.6.52 cache).
- If the restored run is still in battle (disconnected or restarted before the battle settled), it settles as a loss with `BATTLE_END_QUIT`: the win streak resets, the battle count goes up by 1, no gold or EXP, no route progress. The client then gets the unlocked full state through GetData.
- `QuitBattleCsReq` clearing a Currency Wars battle also settles as a loss and sends the unlock sync (as `BATTLE_END_QUIT` in `PVEBattleResultCsReq`); before, the run stayed in the battle phase and could never start another battle. In observed client logs, quitting always went through `PVEBattleResultCsReq` and `QuitBattleCsReq` was never sent.
- Retreat: after `BATTLE_END_QUIT` the official client immediately sends `GridFightQuitLeaveGamePlayCsReq` to return to the overworld. The server still settles as a loss (no free retry) but sends only the unlock sync, not the settlement page, route sync and settlement notification, which avoids a black screen; the client gets the result through GetData when it re-enters.
- Snapshot members are only appended, never reordered or removed. The 15 notification buffers used only for the next sync (`GridFightSession.PersistenceTransientFields`) are not saved; their state changes are already in other fields.
- Checks: writing a sample value into every field and comparing after save/restore (an unmapped new field fails); comparing after save/restore at every step of a complete route (including shared objects, such as the chosen encounter and the current battle plan being the same instance); playing a complete route on a copy restored at each step; rejecting snapshots of another season; and, on a temporary SQLite database, re-login, restoring and settling an unfinished battle after a restart, leaving a run clearing the save, and discarding a corrupt save.

### OSBETAWin4.7.51 table compatibility

The tables were exported from the OSBETAWin4.7.51 client together with a gap-filled cache (see [Rebuilding the resource cache from a client export](../operations/resource-cache-from-client-export.md)) and compared table by table with the 4.6.52 cache.

- When the new native skill layout is detected, `BetaTableRegression` checks the current tables' roles, camps and seasons instead of reusing old role ID assertions, then the full regression continues. Covered: every portal opening and message round trip, merging of split tables, atomic rejection of missing monsters and duplicate seasons, keeping old tables, cross-season reward isolation, and a complete battle and settlement route from the opening to the final boss.
- After the new base tables and GridFightCampSeason are merged, current-season camps keep their original ID index and other seasons are stored under the internal key `(SeasonID << 20) | CampID`; row IDs and protocol IDs are unchanged. New roles resolve skills through GridFightRoleStar and the cache stores the layout flag; an old cache lacking this adaptation is rebuilt automatically when the raw tables are available. Back up before updating the program and cache, and restore both together to roll back.
- Season 2 no longer reuses talent 2011 for end-of-round healing.
- Dynamic values in the new export format are bare JSON values; `GridFightDynamicValueConverter` accepts both the old format and bare values. The field names of `UpgradeAvatarSubType/Equipment/SubRelic` are the actual 4.6.51/4.6.52 names (they used to load as one invalid row per table).

## Expected result

The run prints `PASS`/`FAIL` per check and ends with `GridFight regression: <n>/<n> passed.`, exiting with code 0. The number of checks depends on the cache: the season 2 route is shorter than the season 1 route, so fewer battle-related checks run.

## Known gaps

None of the checks have been verified on a real client: UI, animations and real battles are untested.

### Not implemented

- Season 2 companion effects themselves; only the companion and its effect are rolled and synced.
- Clara: the default enhancement 4001 and the special rules around 5001; only the 21 modules with a real BuffID are wired in, so the full modification ruleset is not restored. The basis for switching between Clara's normal and special level 2 is still missing.
- Aha consumables; Elation recast / template-switch pending actions.
- 千星商会 tiers 3–6: the server lacks complete lobby handling. Part of the tier 6 battle logic exists in the client ability files, so "no server branch" does not mean the tier has no effect; this still needs checking against the actual display and battles.
- Moon Game naming rules.
- Season 2 investment mechanics 换角开演, 刷新连锁, 片场解散, 奇美成长记 and 全速开拓; pure battle effects of investments and portals are executed by the client by ID.
- Investment entries whose gain alone would unbalance the run (max level 7, doubled damage taken, no interest, trait population cap, win-streak / difficulty costs and similar) are not in the effect table and are only recorded.
- Spotlight node choice: the client marks spotlight nodes only through route param 4. Which nodes the official server flags, the candidate weighting and the reroll price are not in the client and follow guesses (nodes 1-2 and 2-1, two candidates, 2 gold, 2 rerolls). Per-spotlight lobby and battle effects live in server JSON that is not shipped; implemented entries follow the in-game description and `EffectParamList`.
- The spotlight that grants `GainOtherRoleTrait` in 4.7.51 is in the `GridFightSpotLight` table, which the 4.7.51 export does not contain.
- 4.7.51 Aha goods 307, 314, 326, 335, 338 and 458–466: their effects are in the server configuration; not implemented and not offered. "球球了!" (349/449): its parameter changed from empty to `[1]` and 466 `[1, 3, 151132]` was added; what 349 grants now cannot be confirmed, so it is no longer offered. (In 4.6.52, 449 read "get 1 gold orb, which opens into a 3-star Aha", which needs an orb ID the client knows.)
- Effects of the new 4.7.51 portals 149–152 and 1015 are in the server configuration and not implemented.
- Elation consumables, 350110, 350118 and other partial season 2 rewards still lack complete target/conversion rules or protocol evidence; no values or behaviour were guessed.
- The bonfire: the 17 dispatchable options below succeed without doing anything. The local Chinese TextMap lacks their titles, so they are identified by event/option ID only. Restoring them needs their real rules first.

  | Event ID | Option ID | Current behaviour |
  | --- | --- | --- |
  | 10600301, 10600401, 10600801, 10600901 | 1 | No parameters; returns success. |
  | 10300501, 10300601, 10300701 | 1 | No parameters; returns success. |
  | 10300801 | 1 | Parameters `[201001, 8009, 1213]`, not handled. |
  | 10200401 | 1 | No parameters; returns success. |
  | 10200501 | 1 | Parameters `[12]`, not handled. |
  | 10200601 | 1 | Parameters `[50]`, not handled. |
  | 11100101 | 1 | Parameters `[200]`, not handled. |
  | 11100301 | 1 | Parameters `[27971503.953125, 0]`; the reward parser produces no reward. |
  | 11100601, 11100701 | 1 | No parameters; returns success. |
  | 10400102, 10400202 | 5 | No parameters and show type None; bypasses the random-coffee branch. |

- Bonfire commissions `10800402/1–9` and `10800502/1–9`: `GridFightPrayQuest.json` / `GridFightPrayQuestFinishWay.json` give each one's quest ID and conditions, but `ApplyBonfireOption` does not read them and always calls `TryAddOrb(203)`. The commission lifecycle (accept, progress, completion, claim) still needs to be wired in; granting the orb is not the commission's effect.
- The "current increase" text of the spotlight card: the client's `GetExtraDescText` call was found, but its value source and encoding are not confirmed. Nothing is written to field 14 of `FMBACDOFCAL`; its `repeated uint64` type is not enough to tell what it means. The strength bonus and free refreshes themselves work on the server.
- The battle-side "purple text" damage bonus of Stellaron Hunter Script Equipment needs the battle equipment source type (`PMAEBPKJCLL`), whose value could not be confirmed from the client.
- "The Aha in the shop is 3-star" could not be reproduced on the server (shop cards have the same star levels as ordinary roles).

### Not confirmed

- Tables still hard-coded from 4.6.51 content cannot be checked against 4.7.51 because their source tables are not in the 4.7.51 export: TraitEquipTemplate, PhanionUniqueTrait, ClaraEnhance, ChimeraInfo, TrailblazeGoods, JadeUniqueTrait, ElationGoods, the spotlight, bonfire and companion effect tables. `GFTraitElationTemplate` (272 rows) is unchanged. In the spotlight adaptation list `151102` became `151112` and matching is by AvatarID so every Aha tier counts; whether the 4.7.51 list was updated cannot be confirmed.
- The 4.7.51 client's TextMap has no season 2 text (all 102 text keys of season 2 traits are missing), so everything marked as unconfirmed here had no description to check against.
- How much growth each Aha copy provides is in the server configuration, not in the tables; "1 point for a lowest-tier copy" is kept. Shop peek probabilities are self-chosen per cost (20% / 30% / 40%); with the new tables and Aha present the minimum is the 4-cost 30%.
- What triggers `AllAvatarByRarity`: no combined reward or orb in the tables points at rows 116–130.
- The 3rd and 4th parameters `[3, 6]` added to each tier of Xianzhou `1001021` are not used; their meaning is unknown.
- In a season 2 route, entering the elite encounter choice right after a supply node is not confirmed by captures; it follows from the client needing an encounter choice before it can start a battle. The current season 2 route has no investment node directly after a supply node.
- Season 2 trait rules not in the client (goods pools, trigger nodes, thresholds) follow the trait descriptions; the 黄金裔 list was a guess until the official list in `GridFightTraitConstValueS2.json` replaced it, and the Aha growth thresholds 3/6 are guesses.
- Investment and portal lobby effects follow description text only; there is no official server logic or capture to compare with. Pepe Cameo has no official capture. Encounter reward values are estimates.
- Tentative values that are not official: 战力提升 uses 5% per step on the attribute pool listed above ([BWIKI's season 1 entry](https://wiki.biligame.com/sr/%E6%88%98%E5%8A%9B%E6%8F%90%E5%8D%87) only describes the random growth mechanism, not the pool or the values). 环保大使叽米's 9 gold garbage bags are tentative and not checked against an official value.
- Battle-generated rewards in field 5 of `GridFightBattleResultStatistics` have no capture yet. Bonus orbs 21006–21008 are opened with guessed contents.
- Economy strategies follow the current resource parameters and were checked against the season 1 mechanics in [BWIKI's investment strategy list](https://wiki.biligame.com/sr/%E6%8A%95%E8%B5%84%E7%AD%96%E7%95%A5%E4%B8%80%E8%A7%88). The client text for the 采购专员·金 counter still needs checking.
- Capture conclusions quoted in comments of the imported GridFight implementation have not been re-verified with new captures.
- With a 4.6.0 season 1 cache, three checks were reported failing: the spotlight reward 23156 and two season 1 investment-pool checks.

### Save compatibility

- New snapshot fields are appended at the end, and lists missing from old snapshots restore as empty. Spending history that an old run never recorded cannot be recovered; recording starts from the first new node. Likewise, old character snapshots read but carry no growth-target mark, so only 战力提升 runs started after the update have the full growth effect, and Advanced Refresh Vision counts start from refreshes made after the update.
- Back up the player database before upgrading; to roll back, restore the program together with the matching pre-upgrade database copy.

### Needs checking on a real client

- Clara modification effects in battle and Black Hole Gravity behaviour.
- Blank spotlight and Aha texts: no client text resources were changed. An empty `JsonPath` in the main resource table is not by itself evidence that an effect does not exist. Aha skill descriptions are still blank.
- The client avatar display after the season 2 module selection.
- 开源节流: the interest preview shows 8/9 at 85/90 gold.
- The three Owlbert strategies: the NPC, its equipment and the usable rewards appear, and gold updates with heartbeats or actions.
- 纳米改件: with advanced equipment, one piece becomes privileged in battle and is back to the original in preparation.
- 刷新视界·进阶: after the holder fights, the next node adds 1 free refresh, and two refreshes in a row raise front/back-line strength by 4%. The card's "current increase" text is not fixed.
- 战力提升's attribute panel, the 采购专员·彩 counter text and the fifth-refresh full-shop offer, the actual healing/shield amounts of 苦战寻宝, and the encounter reward display.
- The 晋升名额 shop display.
- Results of the copy, explicit-coffee and basic-reward bonfire options (only the reward logic is confirmed; animations are unverified).
- Monster values at the highest division in actual battles.
