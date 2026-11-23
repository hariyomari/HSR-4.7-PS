namespace March7thHoney.Enums.GridFight;

public enum GridFightTraitEffectTypeEnum
{
    None = 0,
    TempEquip = 1,
    TraitBonus = 2,
    Bonus = 3,
    MazeBuffEnhance = 4,
    CoreRoleChoose = 5,
    SPSlot = 6,
    CoreRoleByEquipNum = 7,
    TraitEffectByJson = 8,
    SelectEnhance = 9,
    GainFrontTrait = 10,
    BackStrengthenFront = 11,
    Elation = 12,
    HolyGrailPray = 13,
    PartnerRoleChoose = 14,
    ChooseTraitByModule = 15,
    // 4.6.51 names read from the dumped tables; the numbers below are server-side placeholders, not client values.
    TempEquipFromTemplate = 16,
    TraitMemberLink = 17,
    PhanionUnique = 18,
    AdditionalTraitRoleChoose = 19,
    Astropolis = 20,
    UpgradeBasicEquipBeforeBattle = 21,
    ComboSlot = 22,
    Trailblaze = 23,
    MoonGameSpotLight = 24,
    MoonGame = 25,
    ElationLaughPoint = 26,
    Chimera = 27,
    JadeShop = 28,
    ClaraEnhance = 29,
    ChooseTraitByManual = 30,
    DanHengTwoInOne = 31,
    AhaUnique = 32,
    BandRoleChoose = 33,
    // New in the OSBETAWin4.7.51 tables; 34 is also its value in the client (RPG.GameCore.GridFightTraitEffectType).
    GainOtherRoleTrait = 34
}
