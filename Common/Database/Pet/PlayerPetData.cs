using MemoryPack;

namespace March7thHoney.Database.Pet;

/// <summary>
///     Instances and lineup of the 4.6 blind box pets (盲盒宠物). The client refers to each pet instance by a uint64
///     unique id and keeps using that id in the lineup across sessions, so instance ids must be persisted. The pet
///     items themselves still stack as materials in the inventory; the instance list is reconciled from the material
///     counts. This is a new table: an older database gets it created empty on first start, so existing players
///     read empty data.
/// </summary>
[DbTable("PlayerPet")]
public class PlayerPetData : BaseDatabaseDataHelper
{
    /// <summary>Every owned blind box pet instance, in the order obtained.</summary>
    public List<PetInstanceData> Instances { get; set; } = [];

    /// <summary>The blind box pet lineup, i.e. the companion squad (随行小队); at most 3 slots.</summary>
    public List<PetLineupSlotData> Lineup { get; set; } = [];

    /// <summary>
    ///     Current summon state (the EHDNPMEKPCA.LLGIIPALELD enum value). -1 means the player has never summoned or
    ///     recalled a pet; the state is then derived from whether any instance is owned: 3 with instances, 0 without,
    ///     matching official captures of an account with pets and of a new account.
    /// </summary>
    public int CurPetRefType { get; set; } = -1;

    /// <summary>Instance serial number. It only ever increases, so a unique id is never reused.</summary>
    public ulong NextSerial { get; set; }
}

[MemoryPackable]
public partial class PetInstanceData
{
    public ulong UniqueId { get; set; }
    public int PetId { get; set; }
    public int ChromaLevel { get; set; }
    public bool ChromaActive { get; set; }
}

[MemoryPackable]
public partial class PetLineupSlotData
{
    public ulong UniqueId { get; set; }
    public int Slot { get; set; }
}
