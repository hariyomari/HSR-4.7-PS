namespace March7thHoney.Enums.GridFight;

public enum GridFightBonusTypeEnum
{
    Gold = 0,
    Refresh = 2,
    SpecificAvatar = 5,
    RandomAvatar = 6,
    Item = 7,
    Exp = 8,
    Orb = 9,
    RandomEquipByCategory = 13,
    RandomEquipByFunc = 14,
    RandomSameAvatar = 15,
    SpecificAvatarWithRandomEquip = 16,
    SpecificAvatarWithEquip = 17,
    // 4.6.51 names read from the dumped tables; the numbers below are server-side placeholders, not client values.
    GlobalHp = 18,
    TrailblazeShopCoin = 19,
    SpotLightReplaceCnt = 20,
    ElationGoods = 21,
    ElationItem = 22,
    SpotLightFreeRefreshCnt = 23,
    Augment = 24,
    // New in the OSBETAWin4.7.51 tables; 25 is also its value in the client (RPG.GameCore.GridFightBonusType).
    AllAvatarByRarity = 25
}
