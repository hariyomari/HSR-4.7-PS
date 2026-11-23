using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.Inventory;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Shop;
using March7thHoney.Internationalization;
using March7thHoney.Proto;
using March7thHoney.Util;

// Inventory cost regression: removal, shop purchases, synthesis and item use must validate counts and balances
// before granting anything. Resource-free: synthetic item tables, isolated configuration, temporary SQLite.
var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var scratch = Path.Combine(Path.GetTempPath(), "m7h-inventory-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
Environment.CurrentDirectory = scratch;
var config = ConfigManager.Config;
config.Path.ConfigPath = scratch;
config.Path.LanguagePath = Path.Combine(repository, "Config/Languages");
config.Path.ResourcePath = Path.Combine(scratch, "no-raw-resource-json");
config.Path.DatabasePath = scratch;
config.Path.LogPath = Path.Combine(scratch, "Logs");
config.Database.DatabaseType = "sqlite";
config.Database.DatabaseName = "regression.db";
config.ServerOption.Language = "EN";
config.ServerOption.WelcomeMail.Enabled = false;
config.ServerOption.EnableMission = false;
I18NManager.LoadLanguage();
new DatabaseHelper().Initialize(startSaveThread: false);

var failures = new List<string>();
var checks = 0;
void Check(string name, bool pass)
{
    checks++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures.Add(name);
}

// --- Synthetic tables ------------------------------------------------------------------------------------------
const int Hcoin = 1, Scoin = 2, Material = 900001, OtherMaterial = 900002, Output = 900003, Resin = 236,
    WishfulResin = 237, GiftPack = 900004, RewardMaterial = 900005, Relic = 61011, OtherRelic = 61012,
    NormalCompose = 1, SpecialCompose = 2, EmptyCompose = 3, RelicCompose = 4, Shop = 1, MaterialGoods = 1;
void Item(int id, ItemMainTypeEnum type, ItemSubTypeEnum subType = ItemSubTypeEnum.Unknown) =>
    GameData.ItemConfigData[id] = new ItemConfigExcel
    {
        ID = id, ItemMainType = type, ItemSubType = subType, PileLimit = type == ItemMainTypeEnum.Virtual ? 0 : 99999
    };
Item(Hcoin, ItemMainTypeEnum.Virtual);
Item(Scoin, ItemMainTypeEnum.Virtual);
foreach (var id in new[] { Material, OtherMaterial, Output, Resin, WishfulResin, RewardMaterial })
    Item(id, ItemMainTypeEnum.Material);
Item(GiftPack, ItemMainTypeEnum.Usable, ItemSubTypeEnum.Gift);
Item(Relic, ItemMainTypeEnum.Relic);
Item(OtherRelic, ItemMainTypeEnum.Relic);
GameData.RewardDataData[1] = new RewardDataExcel { RewardID = 1, ItemID_1 = RewardMaterial, Count_1 = 5 };
GameData.ItemUseDataData[GiftPack] = new ItemUseDataExcel { UseDataID = GiftPack, UseParam = [1] };
foreach (var relicId in new[] { Relic, OtherRelic })
    GameData.RelicConfigData[relicId] = new RelicConfigExcel { ID = relicId, MainAffixGroup = 1, SubAffixGroup = 1 };
GameData.RelicMainAffixData[1] = new()
{
    [1] = new RelicMainAffixConfigExcel { GroupID = 1, AffixID = 1, Property = AvatarPropertyTypeEnum.AttackAddedRatio }
};
GameData.RelicSubAffixData[1] = new()
{
    [1] = new RelicSubAffixConfigExcel { GroupID = 1, AffixID = 1, Property = AvatarPropertyTypeEnum.AttackAddedRatio },
    [2] = new RelicSubAffixConfigExcel { GroupID = 1, AffixID = 2, Property = AvatarPropertyTypeEnum.DefenceAddedRatio },
    [3] = new RelicSubAffixConfigExcel { GroupID = 1, AffixID = 3, Property = AvatarPropertyTypeEnum.SpeedDelta },
    [4] = new RelicSubAffixConfigExcel { GroupID = 1, AffixID = 4, Property = AvatarPropertyTypeEnum.HPAddedRatio },
    [5] = new RelicSubAffixConfigExcel { GroupID = 1, AffixID = 5, Property = AvatarPropertyTypeEnum.HPDelta }
};
GameData.ItemComposeConfigData[NormalCompose] = new ItemComposeConfigExcel
{
    ID = NormalCompose, FormulaType = "Normal", ItemID = Output, CoinCost = 10, MaxCount = 9999,
    MaterialCost = [new MaterialItem { ItemID = Material, ItemNum = 3 }]
};
GameData.ItemComposeConfigData[SpecialCompose] = new ItemComposeConfigExcel
{
    ID = SpecialCompose, FormulaType = "Sepcial", ItemID = Output, MaxCount = 9999,
    SpecialMaterialCost = [Material, OtherMaterial], SpecialMaterialCostNumber = 2
};
// A cache from before the formula columns: the special-cost recipe restores without any cost.
GameData.ItemComposeConfigData[EmptyCompose] = new ItemComposeConfigExcel { ID = EmptyCompose, ItemID = Output };
GameData.ItemComposeConfigData[RelicCompose] = new ItemComposeConfigExcel
{
    ID = RelicCompose, FormulaType = "SelectedRelic", ItemID = 81014, CoinCost = 1000, MaxCount = 20,
    RelicList = [Relic], SpecialMaterialCost = [Resin], SpecialMaterialCostNumber = 1,
    MaterialCost = [new MaterialItem { ItemID = Material, ItemNum = 100 }]
};
var goods = new ShopGoodsConfigExcel { GoodsID = MaterialGoods, ShopID = Shop, ItemID = Output, CostList = { [Hcoin] = 160 } };
GameData.ShopConfigData[Shop] = new ShopConfigExcel { ShopID = Shop, Goods = [goods] };

// Only the managers under test are wired: PlayerInstance.InitializeAsync needs the full resource tables.
var player = new PlayerInstance(70001);
var inventory = new InventoryManager(player);
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.InventoryManager))!.SetValue(player, inventory);
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.ShopManager))!.SetValue(player, new ShopManager(player));
int Owned(int id) => (int)inventory.GetRemovableCount(id);
void SetBalance(int hcoin, int scoin, params (int id, int count)[] materials)
{
    player.Data.Hcoin = hcoin;
    player.Data.Scoin = scoin;
    inventory.Data.MaterialItems.Clear();
    inventory.Data.RelicItems.Clear();
    foreach (var (id, count) in materials) inventory.Data.MaterialItems.Add(new() { ItemId = id, Count = count });
}
ItemCost Pile(int id, uint num) => new() { PileItem = new PileItem { ItemId = (uint)id, ItemNum = num } };

// --- Removal -----------------------------------------------------------------------------------------------------
SetBalance(100, 0, (Material, 5));
Check("RemoveItem rejects a negative currency count", await inventory.RemoveItem(Hcoin, -1_000_000_000) == null &&
                                                        player.Data.Hcoin == 100);
Check("RemoveItem rejects a negative material count", await inventory.RemoveItem(Material, -10) == null &&
                                                        Owned(Material) == 5);
Check("RemoveItem does not overdraw a currency", await inventory.RemoveItem(Hcoin, 101) == null &&
                                                  player.Data.Hcoin == 100);
Check("destroying ItemCount 0xC4653600 Jade is rejected", !await inventory.TryRemoveItems([(Hcoin, 0xC4653600L)]) &&
                                                         player.Data.Hcoin == 100);
Check("destroying more than owned is rejected", !await inventory.TryRemoveItems([(Material, 6)]) && Owned(Material) == 5);
Check("destroying zero is rejected", !await inventory.TryRemoveItems([(Material, 0)]));
Check("an unaffordable cost removes nothing", !await inventory.TryRemoveItems([(Material, 1), (Hcoin, 101)]) &&
                                              Owned(Material) == 5 && player.Data.Hcoin == 100);
Check("duplicate entries are checked by their total", !await inventory.TryRemoveItems([(Material, 3), (Material, 3)]) &&
                                                      Owned(Material) == 5);
Check("an affordable cost is removed", await inventory.TryRemoveItems([(Material, 2), (Hcoin, 40)]) &&
                                       Owned(Material) == 3 && player.Data.Hcoin == 60);

// --- Shop --------------------------------------------------------------------------------------------------------
SetBalance(0, 0);
var (retcode, bought) = await player.ShopManager!.BuyItem(Shop, MaterialGoods, 1);
Check("buying with zero balance is rejected", retcode == Retcode.RetItemNotEnough && bought.Count == 0 &&
                                              Owned(Output) == 0 && player.Data.Hcoin == 0);
SetBalance(1000, 0);
(retcode, bought) = await player.ShopManager.BuyItem(Shop, MaterialGoods, 0xFFFFFFFFL);
Check("GoodsNum 0xFFFFFFFF is rejected", retcode != Retcode.RetSucc && Owned(Output) == 0 && player.Data.Hcoin == 1000);
(retcode, bought) = await player.ShopManager.BuyItem(Shop, MaterialGoods, 0);
Check("GoodsNum 0 is rejected", retcode != Retcode.RetSucc && Owned(Output) == 0);
(retcode, bought) = await player.ShopManager.BuyItem(Shop, MaterialGoods, 7);
Check("buying more than the balance covers is rejected", retcode == Retcode.RetItemNotEnough && Owned(Output) == 0 &&
                                                         player.Data.Hcoin == 1000);
(retcode, bought) = await player.ShopManager.BuyItem(Shop, MaterialGoods, 6);
Check("an affordable purchase is charged in full", retcode == Retcode.RetSucc && Owned(Output) == 6 &&
                                                   player.Data.Hcoin == 40);

// --- Item synthesis ----------------------------------------------------------------------------------------------
SetBalance(0, 0);
var (composeRetcode, composed) = await inventory.ComposeItem(NormalCompose, 1, [Pile(Hcoin, 0xC4653600u)]);
Check("synthesis without materials is rejected", composeRetcode == Retcode.RetItemNotEnough && composed == null &&
                                                 Owned(Output) == 0);
SetBalance(0, 100, (Material, 3));
(composeRetcode, composed) = await inventory.ComposeItem(NormalCompose, 1, [Pile(Hcoin, 0xC4653600u)]);
Check("client cost entries are ignored for a normal recipe", composeRetcode == Retcode.RetSucc && Owned(Output) == 1 &&
                                                             Owned(Material) == 0 && player.Data.Scoin == 90 &&
                                                             player.Data.Hcoin == 0);
SetBalance(0, 100, (Material, 300));
(composeRetcode, _) = await inventory.ComposeItem(NormalCompose, -1, []);
Check("a negative synthesis count is rejected", composeRetcode != Retcode.RetSucc && Owned(Material) == 300);
(composeRetcode, _) = await inventory.ComposeItem(NormalCompose, 10_000, []);
Check("a synthesis count above MaxCount is rejected", composeRetcode != Retcode.RetSucc && Owned(Material) == 300);
(composeRetcode, _) = await inventory.ComposeItem(NormalCompose, 11, []);
Check("synthesis that the credits do not cover is rejected", composeRetcode == Retcode.RetItemNotEnough &&
                                                              Owned(Material) == 300 && player.Data.Scoin == 100);
SetBalance(0, 0, (Material, 1), (OtherMaterial, 1));
(composeRetcode, _) = await inventory.ComposeItem(SpecialCompose, 1, [Pile(Material, 1)]);
Check("a short special-material payment is rejected", composeRetcode != Retcode.RetSucc && Owned(Output) == 0 &&
                                                      Owned(Material) == 1);
(composeRetcode, _) = await inventory.ComposeItem(SpecialCompose, 1, [Pile(Material, 1), Pile(Hcoin, 1)]);
Check("special-material entries outside the recipe do not count", composeRetcode != Retcode.RetSucc && Owned(Output) == 0);
(composeRetcode, _) = await inventory.ComposeItem(SpecialCompose, 1, [Pile(Material, 1), Pile(OtherMaterial, 1)]);
Check("a special-material payment from the recipe's list is accepted", composeRetcode == Retcode.RetSucc &&
    Owned(Output) == 1 && Owned(Material) == 0 && Owned(OtherMaterial) == 0);
(composeRetcode, _) = await inventory.ComposeItem(EmptyCompose, 1, []);
Check("a recipe without any cost is rejected", composeRetcode != Retcode.RetSucc && Owned(Output) == 1);

// --- Relic synthesis ---------------------------------------------------------------------------------------------
ComposeSelectedRelicCsReq RelicReq(int relicId, uint mainAffix = 1, uint count = 1, params uint[] subAffixes)
{
    var req = new ComposeSelectedRelicCsReq
    {
        ComposeId = RelicCompose, ComposeRelicId = (uint)relicId, MainAffixId = mainAffix, Count = count,
        ComposeItemList = new ItemCostData { ItemList = { Pile(Resin, count), Pile(Hcoin, 0xC4653600u) } }
    };
    req.SubAffixIdList.AddRange(subAffixes);
    return req;
}
SetBalance(0, 0);
var (relicRetcode, relics) = await inventory.ComposeRelic(RelicReq(Relic));
Check("relic synthesis without materials is rejected", relicRetcode == Retcode.RetItemNotEnough && relics.Count == 0 &&
                                                       inventory.Data.RelicItems.Count == 0 && player.Data.Hcoin == 0);
SetBalance(0, 10000, (Material, 1000), (Resin, 10), (WishfulResin, 10));
(relicRetcode, relics) = await inventory.ComposeRelic(RelicReq(OtherRelic));
Check("a relic outside the recipe's RelicList is rejected", relicRetcode != Retcode.RetSucc &&
                                                             inventory.Data.RelicItems.Count == 0 && Owned(Resin) == 10);
(relicRetcode, relics) = await inventory.ComposeRelic(RelicReq(Relic, mainAffix: 99));
Check("a main affix outside the relic's group is rejected", relicRetcode != Retcode.RetSucc &&
                                                             inventory.Data.RelicItems.Count == 0);
(relicRetcode, relics) = await inventory.ComposeRelic(RelicReq(Relic, subAffixes: [99]));
Check("a sub affix outside the relic's group is rejected", relicRetcode != Retcode.RetSucc &&
                                                            inventory.Data.RelicItems.Count == 0);
(relicRetcode, relics) = await inventory.ComposeRelic(RelicReq(Relic, subAffixes: [1]));
Check("a sub affix repeating the main stat is rejected", relicRetcode != Retcode.RetSucc &&
                                                          inventory.Data.RelicItems.Count == 0);
(relicRetcode, relics) = await inventory.ComposeRelic(RelicReq(Relic, subAffixes: [2, 3, 4]));
Check("more than two designated sub affixes are rejected", relicRetcode != Retcode.RetSucc &&
                                                            inventory.Data.RelicItems.Count == 0);
(relicRetcode, relics) = await inventory.ComposeRelic(RelicReq(Relic, subAffixes: [2, 3]));
Check("designated sub affixes without resin are rejected", relicRetcode == Retcode.RetItemNotEnough &&
                                                            inventory.Data.RelicItems.Count == 0 && Owned(Resin) == 10);
(relicRetcode, relics) = await inventory.ComposeRelic(RelicReq(Relic, count: 21));
Check("a relic count above MaxCount is rejected", relicRetcode != Retcode.RetSucc && inventory.Data.RelicItems.Count == 0);
(relicRetcode, relics) = await inventory.ComposeRelic(RelicReq(Relic, count: 2));
Check("a valid relic synthesis charges the recipe and ignores client cost entries",
    relicRetcode == Retcode.RetSucc && relics.Count == 2 && relics.All(relic => relic.ItemId == Relic && relic.MainAffix == 1) &&
    Owned(Material) == 800 && player.Data.Scoin == 8000 && Owned(Resin) == 8 && player.Data.Hcoin == 0);
var designated = RelicReq(Relic, subAffixes: [2, 3]);
designated.WrItemList = new ItemCostData { ItemList = { Pile(WishfulResin, 1) } };
(relicRetcode, relics) = await inventory.ComposeRelic(designated);
Check("designated sub affixes paid with Wishful Resin are kept", relicRetcode == Retcode.RetSucc && relics.Count == 1 &&
    relics[0].SubAffixes.Any(sub => sub.Id == 2) && relics[0].SubAffixes.Any(sub => sub.Id == 3) &&
    Owned(WishfulResin) == 9);

// --- Item use ----------------------------------------------------------------------------------------------------
SetBalance(0, 0);
var (useRetcode, rewards) = await inventory.UseItem(GiftPack, 1);
Check("using an unowned item grants nothing", useRetcode == Retcode.RetItemNotEnough && rewards == null &&
                                              Owned(RewardMaterial) == 0);
SetBalance(0, 0, (GiftPack, 2));
(useRetcode, _) = await inventory.UseItem(GiftPack, 3);
Check("using more than owned grants nothing", useRetcode == Retcode.RetItemNotEnough && Owned(RewardMaterial) == 0 &&
                                              Owned(GiftPack) == 2);
(useRetcode, _) = await inventory.UseItem(GiftPack, -1);
Check("a negative use count grants nothing", useRetcode == Retcode.RetItemNotEnough && Owned(RewardMaterial) == 0);
(useRetcode, rewards) = await inventory.UseItem(GiftPack, 1);
Check("using an owned item consumes it and grants its reward", useRetcode == Retcode.RetSucc &&
                                                                Owned(GiftPack) == 1 && Owned(RewardMaterial) == 5);

Console.WriteLine($"Inventory regression checks: {checks}, failures: {failures.Count}");
foreach (var failure in failures) Console.WriteLine($"  FAIL {failure}");
try { Directory.Delete(scratch, true); } catch (IOException) { }
return failures.Count == 0 ? 0 : 1;
