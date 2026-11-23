using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.Avatar;
using March7thHoney.GameServer.Game.Inventory;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Shop;
using March7thHoney.GameServer.Server.Packet.Send.Player;
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
const int Hcoin = 1, Scoin = 2, Mcoin = 3, Hertareum = 120000, Starlight = 252, AvatarCardId = 1001, AvatarEidolonId = 11001,
    AvatarHeadIconId = 201001, Material = 900001, OtherMaterial = 900002, Output = 900003, Resin = 236,
    WishfulResin = 237, GiftPack = 900004, RewardMaterial = 900005, Relic = 61011, OtherRelic = 61012,
    NormalCompose = 1, SpecialCompose = 2, EmptyCompose = 3, RelicCompose = 4,
    Shop = 1, MaterialGoods = 1, EmbersShop = 102, EmbersPassGoods = 102001, EmbersCreditGoods = 102002,
    StarlightAvatarGoods = 101001, StarlightShop = 101,
    NpcShop = 301, DailyGoods = 301001, WeeklyGoods = 301002,
    CityShop = 401, CityShopGoods = 401001;
void Item(int id, ItemMainTypeEnum type, ItemSubTypeEnum subType = ItemSubTypeEnum.Unknown) =>
    GameData.ItemConfigData[id] = new ItemConfigExcel
    {
        ID = id, ItemMainType = type, ItemSubType = subType, PileLimit = type == ItemMainTypeEnum.Virtual ? 0 : 99999
    };
Item(Hcoin, ItemMainTypeEnum.Virtual);
Item(Scoin, ItemMainTypeEnum.Virtual);
Item(Mcoin, ItemMainTypeEnum.Virtual);
foreach (var id in new[] { Hertareum, Starlight, AvatarEidolonId, Material, OtherMaterial, Output, Resin, WishfulResin, RewardMaterial })
    Item(id, ItemMainTypeEnum.Material);
Item(AvatarCardId, ItemMainTypeEnum.AvatarCard);
Item(AvatarHeadIconId, ItemMainTypeEnum.Usable, ItemSubTypeEnum.HeadIcon);
Item(GiftPack, ItemMainTypeEnum.Usable, ItemSubTypeEnum.Gift);
Item(Relic, ItemMainTypeEnum.Relic);
Item(OtherRelic, ItemMainTypeEnum.Relic);
GameData.AvatarConfigData[AvatarCardId] = new AvatarConfigExcel
{
    AvatarID = AvatarCardId, MaxRank = 6, Rarity = RarityEnum.CombatPowerAvatarRarityType4
};
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
var goods = new ShopGoodsConfigExcel { GoodsID = MaterialGoods, ShopID = Shop, ItemID = Output, ItemCount = 1, CostList = { [Hcoin] = 160 } };
GameData.ShopConfigData[Shop] = new ShopConfigExcel { ShopID = Shop, ShopType = 1, Goods = [goods] };
var embersPass = new ShopGoodsConfigExcel { GoodsID = EmbersPassGoods, ShopID = EmbersShop, ItemID = Output, ItemCount = 1, LimitTimes = 5, RefreshType = "MONTH", CostList = { [Hcoin] = 90 } };
var embersCredits = new ShopGoodsConfigExcel { GoodsID = EmbersCreditGoods, ShopID = EmbersShop, ItemID = Scoin, ItemCount = 10000, RefreshType = "MONTH", CostList = { [Hcoin] = 10 } };
GameData.ShopConfigData[EmbersShop] = new ShopConfigExcel { ShopID = EmbersShop, ShopType = 2, Goods = [embersPass, embersCredits] };
var starlightAvatar = new ShopGoodsConfigExcel { GoodsID = StarlightAvatarGoods, ShopID = StarlightShop, ItemID = AvatarCardId, ItemCount = 1, CostList = { [Hcoin] = 140 } };
GameData.ShopConfigData[StarlightShop] = new ShopConfigExcel { ShopID = StarlightShop, ShopType = 2, Goods = [starlightAvatar] };
var dailyGoods = new ShopGoodsConfigExcel { GoodsID = DailyGoods, ShopID = NpcShop, ItemID = Output, ItemCount = 1, LimitTimes = 3, RefreshType = "DAILY", CostList = { [Hcoin] = 10 } };
var weeklyGoods = new ShopGoodsConfigExcel { GoodsID = WeeklyGoods, ShopID = NpcShop, ItemID = Output, ItemCount = 1, LimitTimes = 3, RefreshType = "WEEK", CostList = { [Hcoin] = 10 } };
GameData.ShopConfigData[NpcShop] = new ShopConfigExcel { ShopID = NpcShop, ShopType = 3, Goods = [dailyGoods, weeklyGoods] };
var cityGoods = new ShopGoodsConfigExcel { GoodsID = CityShopGoods, ShopID = CityShop, ItemID = Output, ItemCount = 1, CostList = { [Hertareum] = 10 } };
GameData.ShopConfigData[CityShop] = new ShopConfigExcel { ShopID = CityShop, ShopType = 4, Goods = [cityGoods] };
GameData.CityShopConfigData[CityShop] = new CityShopConfigExcel { ShopID = CityShop, RewardListGroupID = CityShop, ItemID = Hertareum, MaxLevel = 4, WorldID = 101 };
GameData.CityShopRewardListData[CityShop] = new()
{
    [1] = new CityShopRewardListExcel { GroupID = CityShop, Level = 1, ItemNeed = 10, TotalItem = 10 },
    [2] = new CityShopRewardListExcel { GroupID = CityShop, Level = 2, RewardID = 1, ItemNeed = 80, TotalItem = 90 },
    [3] = new CityShopRewardListExcel { GroupID = CityShop, Level = 3, RewardID = 1, ItemNeed = 150, TotalItem = 240 },
    [4] = new CityShopRewardListExcel { GroupID = CityShop, Level = 4, RewardID = 1, ItemNeed = 0, TotalItem = 390 }
};

// Only the managers under test are wired: PlayerInstance.InitializeAsync needs the full resource tables.
var player = new PlayerInstance(70001) { Uid = 70001 };
player.Data.FakeTimeDate = 20261122;
var inventory = new InventoryManager(player);
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.InventoryManager))!.SetValue(player, inventory);
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.AvatarManager))!.SetValue(player, new AvatarManager(player));
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.PlayerUnlockData))!.SetValue(player, player.InitializeDatabase<March7thHoney.Database.Player.PlayerUnlockData>());
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
var (retcode, bought, buyTimes) = await player.ShopManager!.BuyItem(Shop, MaterialGoods, 1);
Check("buying with zero balance is rejected", retcode == Retcode.RetItemNotEnough && bought.Count == 0 &&
                                              buyTimes == 0 && Owned(Output) == 0 && player.Data.Hcoin == 0);
SetBalance(1000, 0);
(retcode, bought, buyTimes) = await player.ShopManager.BuyItem(Shop, MaterialGoods, 0xFFFFFFFFL);
Check("GoodsNum 0xFFFFFFFF is rejected", retcode != Retcode.RetSucc && buyTimes == 0 && Owned(Output) == 0 && player.Data.Hcoin == 1000);
(retcode, bought, buyTimes) = await player.ShopManager.BuyItem(Shop, MaterialGoods, 0);
Check("GoodsNum 0 is rejected", retcode != Retcode.RetSucc && buyTimes == 0 && Owned(Output) == 0);
(retcode, bought, buyTimes) = await player.ShopManager.BuyItem(Shop, MaterialGoods, 7);
Check("buying more than the balance covers is rejected", retcode == Retcode.RetItemNotEnough && buyTimes == 0 &&
                                                         Owned(Output) == 0 && player.Data.Hcoin == 1000);
(retcode, bought, buyTimes) = await player.ShopManager.BuyItem(Shop, MaterialGoods, 6);
Check("an affordable purchase is charged in full", retcode == Retcode.RetSucc && buyTimes == 6 && Owned(Output) == 6 &&
                                                   player.Data.Hcoin == 40);

// Cumulative buy times across repeated purchases, LimitTimes enforcement, monthly reset rollover, and ItemCount > 1 stacks.
SetBalance(1000, 0);
(retcode, bought, buyTimes) = await player.ShopManager.BuyItem(EmbersShop, EmbersPassGoods, 2);
var (ret2, _, buyTimes2) = await player.ShopManager.BuyItem(EmbersShop, EmbersPassGoods, 3);
var (retLimit, _, buyTimesAfterLimit) = await player.ShopManager.BuyItem(EmbersShop, EmbersPassGoods, 1);
var embersProto = player.ShopManager.BuildShopList(2).Single(s => s.ShopId == EmbersShop);
var embersPassProto = embersProto.GoodsList.Single(g => g.GoodsId == EmbersPassGoods);
Check("monthly shop buy times accumulate, enforce LimitTimes, and survive rejected buys",
    retcode == Retcode.RetSucc && buyTimes == 2 && ret2 == Retcode.RetSucc && buyTimes2 == 5 &&
    retLimit == Retcode.RetBuyTimesLimit && buyTimesAfterLimit == 5 && embersPassProto.BuyTimes == 5 &&
    embersProto.BeginTime > 0 && embersProto.EndTime > embersProto.BeginTime &&
    embersProto.EndTime < uint.MaxValue);

(retcode, bought, buyTimes) = await player.ShopManager.BuyItem(EmbersShop, EmbersCreditGoods, 2);
Check("stacked shop goods multiply count by ItemCount",
    retcode == Retcode.RetSucc && buyTimes == 2 && player.Data.Scoin == 20000 &&
    bought.Single() is { ItemId: Scoin, Count: 20000 });

// Daily (2026-11-22 Sun -> 2026-11-23 Mon) and Weekly (Monday reset) rollovers.
SetBalance(1000, 0);
await player.ShopManager.BuyItem(NpcShop, DailyGoods, 2);
await player.ShopManager.BuyItem(NpcShop, WeeklyGoods, 2);
player.Data.FakeTimeDate = 20261123; // Monday: resets both DAILY and WEEK, keeps MONTH
Check("Monday rollover resets DAILY and WEEK goods while preserving MONTH goods",
    player.ShopManager.GetGoodsBuyTimes(DailyGoods) == 0 &&
    player.ShopManager.GetGoodsBuyTimes(WeeklyGoods) == 0 &&
    player.ShopManager.GetGoodsBuyTimes(EmbersPassGoods) == 5);
await player.ShopManager.BuyItem(NpcShop, DailyGoods, 1);
await player.ShopManager.BuyItem(NpcShop, WeeklyGoods, 1);
player.Data.FakeTimeDate = 20261124; // Tuesday: resets DAILY only, keeps WEEK and MONTH
Check("mid-week rollover resets DAILY goods while preserving WEEK and MONTH goods",
    player.ShopManager.GetGoodsBuyTimes(DailyGoods) == 0 &&
    player.ShopManager.GetGoodsBuyTimes(WeeklyGoods) == 1 &&
    player.ShopManager.GetGoodsBuyTimes(EmbersPassGoods) == 5);

player.Data.FakeTimeDate = 20261201;
var afterRollover = player.ShopManager.BuildShopList(2).Single(s => s.ShopId == EmbersShop);
Check("monthly rollover resets Embers Exchange counts while keeping permanent shop counts",
    afterRollover.GoodsList.Single(g => g.GoodsId == EmbersPassGoods).BuyTimes == 0 &&
    afterRollover.GoodsList.Single(g => g.GoodsId == EmbersCreditGoods).BuyTimes == 0 &&
    player.ShopManager.GetGoodsBuyTimes(MaterialGoods) == 6);

// World / City Shop spending exp, multi-level up, and reward claims.
SetBalance(0, 0, (Hertareum, 300));
await player.ShopManager.BuyItem(CityShop, CityShopGoods, 1); // 10 Hertareum -> Level 2, Exp 0
var cityProto1 = player.ShopManager.BuildShopList(4).Single(s => s.ShopId == CityShop);
var (tooHighRet, _) = await player.ShopManager.TakeCityShopReward(CityShop, 3);
var (lv2Ret, lv2Rewards) = await player.ShopManager.TakeCityShopReward(CityShop, 2);
var (lv2DupRet, _) = await player.ShopManager.TakeCityShopReward(CityShop, 2);
var cityProto2 = player.ShopManager.BuildShopList(4).Single(s => s.ShopId == CityShop);
await player.ShopManager.BuyItem(CityShop, CityShopGoods, 23); // +230 (240 total) -> Level 4, Exp 0
var cityProto3 = player.ShopManager.BuildShopList(4).Single(s => s.ShopId == CityShop);
Check("city shop spending levels up, validates level/duplicate claims, and updates bitmask",
    cityProto1 is { CityLevel: 2, CityExp: 0, CityTakenLevelReward: 0 } &&
    tooHighRet == Retcode.RetCityLevelNotMeet &&
    lv2Ret == Retcode.RetSucc && lv2Rewards.Single() is { ItemId: RewardMaterial, Count: 5 } &&
    lv2DupRet == Retcode.RetCityLevelRewardTaken &&
    cityProto2.CityTakenLevelReward == (1UL << 2) &&
    cityProto3 is { CityLevel: 4, CityExp: 0 });

// Legacy Resource.bin compatibility: 7-member ShopGoodsConfigExcel (before LimitTimes/RefreshType were appended).
var legacyGoodsBuf = new System.Buffers.ArrayBufferWriter<byte>();
var legacyWriterState = MemoryPack.MemoryPackWriterOptionalStatePool.Rent(MemoryPack.MemoryPackSerializerOptions.Default);
try
{
    var writer = new MemoryPack.MemoryPackWriter<System.Buffers.ArrayBufferWriter<byte>>(ref legacyGoodsBuf, legacyWriterState);
    writer.WriteCollectionHeader(2);
    for (var i = 0; i < 2; i++)
    {
        writer.WriteObjectHeader(7);
        writer.WriteUnmanaged(102001 + i, 102, 102 - i, 1);
        writer.WriteValue(new List<int> { 251 });
        writer.WriteValue(new List<int> { 90 });
        writer.WriteValue(new Dictionary<int, int> { [251] = 90 });
    }
    writer.Flush();
}
finally
{
    legacyWriterState.Reset();
}
var legacyDecoded = MemoryPack.MemoryPackSerializer.Deserialize<List<ShopGoodsConfigExcel>>(legacyGoodsBuf.WrittenSpan);
Check("legacy 7-member ShopGoodsConfigExcel binaries deserialize without shifting CostList",
    legacyDecoded is { Count: 2 } &&
    legacyDecoded[0] is { GoodsID: 102001, ShopID: 102, LimitTimes: 0 } &&
    legacyDecoded[0].CostList.GetValueOrDefault(251) == 90 &&
    string.IsNullOrEmpty(legacyDecoded[0].RefreshType) &&
    legacyDecoded[1] is { GoodsID: 102002, ShopID: 102, LimitTimes: 0 } &&
    legacyDecoded[1].CostList.GetValueOrDefault(251) == 90 &&
    string.IsNullOrEmpty(legacyDecoded[1].RefreshType));

// AvatarCard shop purchases: new unlock, duplicate eidolon, and E6 starlight conversion.
SetBalance(1000, 0);
(retcode, bought, _) = await player.ShopManager.BuyItem(StarlightShop, StarlightAvatarGoods, 1);
Check("buying an unowned AvatarCard unlocks the character and populates ReturnItemList",
    retcode == Retcode.RetSucc && player.AvatarManager!.GetFormalAvatar(AvatarCardId) != null &&
    bought.Single() is { ItemId: AvatarCardId, Count: 1 });
(retcode, bought, _) = await player.ShopManager.BuyItem(StarlightShop, StarlightAvatarGoods, 1);
Check("buying an owned AvatarCard below E6 grants its eidolon in ReturnItemList",
    retcode == Retcode.RetSucc && Owned(AvatarEidolonId) == 1 &&
    bought.Single() is { ItemId: AvatarEidolonId, Count: 1 });
inventory.Data.MaterialItems.RemoveAll(x => x.ItemId == AvatarEidolonId);
player.AvatarManager!.GetFormalAvatar(AvatarCardId)!.PathInfos[AvatarCardId].Rank = 6;
(retcode, bought, _) = await player.ShopManager.BuyItem(StarlightShop, StarlightAvatarGoods, 1);
Check("buying an E6 AvatarCard with zero held eidolons converts to Starlight instead of a 7th eidolon",
    retcode == Retcode.RetSucc && Owned(AvatarEidolonId) == 0 && Owned(Starlight) == 20 &&
    bought.Single() is { ItemId: Starlight, Count: 20 });

// Oneiric Pouch first-time 2x bonus persisted per player in ShopData.
const string CoinProductId = "rpgglbcoin300tier5";
GameData.QueryProductInfoConfig = new QueryProductInfoConfig
{
    ProductList =
    [
        new QueryProductInfoItem
        {
            ProductId = CoinProductId,
            PriceTier = "Tier_5",
            GiftType = "PRODUCT_GIFT_COIN",
            BeginTime = 1577854800,
            EndTime = 4070926800
        }
    ]
};
var coinProduct = new RechargeConfigExcel
{
    TierID = "Tier_5",
    ProductID = CoinProductId,
    GiftType = (int)ProductGiftType.ProductGiftCoin,
    FirstCharge = 300,
    NormalCharge = 30
};
config.ServerOption.EnableFakeRecharge = true;
config.ServerOption.RechargeDoubleBonusResetDate = "04-26";
player.Data.FakeTimeDate = 20260425;
player.Data.Mcoin = 0;
var beforeQuery = QueryProductInfoScRsp.Parser.ParseFrom(new PacketQueryProductInfoScRsp(player.Data).Data);
var firstCoin = await RechargeService.Purchase(player, coinProduct);
var afterFirstQuery = QueryProductInfoScRsp.Parser.ParseFrom(new PacketQueryProductInfoScRsp(player.Data).Data);
var secondCoin = await RechargeService.Purchase(player, coinProduct);
var afterSecondQuery = QueryProductInfoScRsp.Parser.ParseFrom(new PacketQueryProductInfoScRsp(player.Data).Data);
Check("Oneiric Pouch grants 2x on first purchase, clears DoubleReward, and pays normal bonus afterwards",
    beforeQuery.ProductList.Single() is { BuyTimes: 0, DoubleReward: true } &&
    firstCoin == RechargeResult.Granted &&
    afterFirstQuery.ProductList.Single() is { BuyTimes: 1, DoubleReward: false } &&
    secondCoin == RechargeResult.Granted &&
    afterSecondQuery.ProductList.Single() is { BuyTimes: 2, DoubleReward: false } &&
    player.Data.Mcoin == 300 * 2 + 300 + 30);

player.Data.FakeTimeDate = 20260426;
var afterAnnivQuery = QueryProductInfoScRsp.Parser.ParseFrom(new PacketQueryProductInfoScRsp(player.Data).Data);
var mcoinBeforeAnniv = player.Data.Mcoin;
var annivCoin = await RechargeService.Purchase(player, coinProduct);
Check("Oneiric Pouch 2x bonus resets on the configured anniversary date (04-26)",
    afterAnnivQuery.ProductList.Single() is { BuyTimes: 0, DoubleReward: true } &&
    annivCoin == RechargeResult.Granted &&
    player.Data.Mcoin == mcoinBeforeAnniv + 300 * 2);

config.ServerOption.RechargeDoubleBonusResetDate = "";
player.Data.FakeTimeDate = 20270426;
var disabledResetQuery = QueryProductInfoScRsp.Parser.ParseFrom(new PacketQueryProductInfoScRsp(player.Data).Data);
Check("clearing RechargeDoubleBonusResetDate disables automatic annual resets",
    disabledResetQuery.ProductList.Single() is { BuyTimes: 1, DoubleReward: false });

config.ServerOption.RechargeDoubleBonusResetDate = "10-01";
player.Data.FakeTimeDate = 20271001;
var customResetQuery = QueryProductInfoScRsp.Parser.ParseFrom(new PacketQueryProductInfoScRsp(player.Data).Data);
Check("a custom RechargeDoubleBonusResetDate resets on that date",
    customResetQuery.ProductList.Single() is { BuyTimes: 0, DoubleReward: true });
config.ServerOption.RechargeDoubleBonusResetDate = "04-26";

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
SetBalance(0, 0, (GiftPack, 2));
(useRetcode, rewards) = await inventory.UseItem(GiftPack, 2);
Check("using multiple reward items grants the reward once per item", useRetcode == Retcode.RetSucc &&
                                                                     Owned(GiftPack) == 0 && Owned(RewardMaterial) == 10);

Console.WriteLine($"Inventory regression checks: {checks}, failures: {failures.Count}");
foreach (var failure in failures) Console.WriteLine($"  FAIL {failure}");
try { Directory.Delete(scratch, true); } catch (IOException) { }
return failures.Count == 0 ? 0 : 1;
