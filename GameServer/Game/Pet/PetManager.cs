using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Pet;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.Player.Components;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Pet;

/// <summary>
///     Blind box pets (盲盒宠物) of 4.6, ported from yuukisr_go c4f5979 / 24e7083.
///     <para>
///         Pet items still stack in the inventory as materials (grant paths such as /giveall and rewards are
///         unchanged), but the official server never puts pet items into the inventory sync, and the client builds its
///         pet page entirely from the instance list in <c>GetPetDataScRsp</c>. Instances are therefore reconciled
///         against the material counts: surplus copies append instances, missing copies remove instances and clear
///         their lineup references. Instance ids are persisted in <see cref="PlayerPetData" />.
///     </para>
///     <para>
///         Only blind box pets (new in 4.6, from 251501 on) become instances. A first-generation summoned pet
///         (251001–251005 in PetConfig, ItemSubType NormalPet) in pet_list makes the client's BlindBoxPetRepository
///         throw ArgumentException, and the whole pet module fails to initialize.
///     </para>
/// </summary>
public class PetManager(PlayerInstance player) : BasePlayerComponent(player)
{
    /// <summary>
    ///     Lineups in official captures carry slots 1..3, and the types in use in BlindBoxPetType also have 3 slots.
    ///     The BlindBox* tables are not in the resource cache, so the value is hard-coded for now.
    /// </summary>
    public const int MaxBlindBoxLineupSlots = 3;

    // Official enum values (yuukisr_go design_pet_system.md §4.5): an account with pets has cur_pet.ref_type=3 and
    // lineup source=2; a new account has 0 for both.
    public const int CurPetRefTypeBlindBox = (int)EOGNOEJHAGN.Lcfkgcjchbo;
    public const HENPKNCKGGI LineupSourceSet = HENPKNCKGGI.Oalpdgkgpbj;

    // Pet unique ids are taken from the high range: the same magnitude as the official server's large ids, and
    // impossible to confuse with other small integer ids.
    private const ulong UniqueIdBase = 1UL << 62;

    public PlayerPetData Data { get; } = DatabaseHelper.Instance!.GetInstanceOrCreateNew<PlayerPetData>(player.Uid);

    private void MarkDirty() => DatabaseHelper.MarkDirty(Player.Uid);

    /// <summary>
    ///     Whether the item is a blind box pet. The ItemSubType enum in the cache has no BlindBoxPet (it parses as
    ///     Unknown), so the check works by exclusion: the main type is Pet, and the item is neither of the NormalPet
    ///     subtype nor the item of a first-generation pet in PetConfig.
    /// </summary>
    public static bool IsBlindBoxPetItem(int itemId)
    {
        if (!GameData.ItemConfigData.TryGetValue(itemId, out var config)) return false;
        if (config.ItemMainType != ItemMainTypeEnum.Pet) return false;
        if (config.ItemSubType == ItemSubTypeEnum.NormalPet) return false;
        return GameData.PetData.Values.All(pet => pet.PetItemID != itemId);
    }

    /// <summary>
    ///     Reconciles the instances and the lineup against the blind box pet materials in the inventory and returns the
    ///     instances created by this call. Existing instances keep their order and ids.
    /// </summary>
    public List<PetInstanceData> Reconcile()
    {
        var owned = new Dictionary<int, int>();
        var order = new List<int>();
        foreach (var item in Player.InventoryManager!.Data.MaterialItems)
        {
            if (item.Count <= 0 || !IsBlindBoxPetItem(item.ItemId)) continue;
            if (!owned.ContainsKey(item.ItemId)) order.Add(item.ItemId);
            owned[item.ItemId] = owned.GetValueOrDefault(item.ItemId) + item.Count;
        }

        var kept = new Dictionary<int, int>();
        var instances = new List<PetInstanceData>(Data.Instances.Count);
        foreach (var instance in Data.Instances)
        {
            if (kept.GetValueOrDefault(instance.PetId) >= owned.GetValueOrDefault(instance.PetId)) continue;
            kept[instance.PetId] = kept.GetValueOrDefault(instance.PetId) + 1;
            instances.Add(instance);
        }

        var changed = instances.Count != Data.Instances.Count;
        var added = new List<PetInstanceData>();
        foreach (var petId in order)
            for (var have = kept.GetValueOrDefault(petId); have < owned[petId]; have++)
            {
                Data.NextSerial++;
                var instance = new PetInstanceData { UniqueId = UniqueIdBase | Data.NextSerial, PetId = petId };
                instances.Add(instance);
                added.Add(instance);
                kept[petId] = have + 1;
                changed = true;
            }

        Data.Instances = instances;

        var exists = instances.Select(x => x.UniqueId).ToHashSet();
        var lineup = Data.Lineup.Where(slot => exists.Remove(slot.UniqueId)).ToList();
        if (lineup.Count != Data.Lineup.Count)
        {
            Data.Lineup = lineup;
            changed = true;
        }

        if (changed) MarkDirty();
        return added;
    }

    /// <summary>
    ///     Called after the pet materials in the inventory change: reconciles and, following the official box-opening
    ///     flow, pushes PetDataChangeScNotify with one add_pet entry per new pet, from which the client adds the new
    ///     instances to its pet page. There is no capture evidence of how the official server reports removals, so
    ///     nothing is pushed for them; the next GetPetData brings the client back in sync.
    /// </summary>
    public async ValueTask OnPetItemsChanged()
    {
        var added = Reconcile();
        if (added.Count == 0) return;

        var notify = new PetDataChangeScNotify();
        foreach (var instance in added)
            notify.DGNGMJMPFGK.Add(new CBHJJCBDJHA { AHBNBNFABBO = ToInstanceProto(instance) });
        await Player.SendPacket(new Server.Packet.Send.Pet.PacketPetDataChangeScNotify(notify));
    }

    /// <summary>
    ///     Replaces the blind box pet lineup. Every instance must be owned and listed once; at most 3 slots are
    ///     allowed, with slot numbers unique and no greater than 3. An invalid request is rejected without replacing
    ///     the lineup.
    /// </summary>
    public bool SetBlindBoxLineup(IEnumerable<EPFJLNKPEPK> slots)
    {
        var requested = slots.ToList();
        if (requested.Count > MaxBlindBoxLineupSlots) return false;

        Reconcile();
        var owned = Data.Instances.Select(x => x.UniqueId).ToHashSet();
        var seenIds = new HashSet<ulong>();
        var seenSlots = new HashSet<uint>();
        foreach (var slot in requested)
        {
            if (slot.EBAAFDHEPLO == 0 || !owned.Contains(slot.EBAAFDHEPLO)) return false;
            if (!seenIds.Add(slot.EBAAFDHEPLO)) return false;
            if (slot.Slot > MaxBlindBoxLineupSlots || !seenSlots.Add(slot.Slot)) return false;
        }

        Data.Lineup = [.. requested.Select(x => new PetLineupSlotData { UniqueId = x.EBAAFDHEPLO, Slot = (int)x.Slot })];
        MarkDirty();
        return true;
    }

    /// <summary>
    ///     Summon: records the summon mode from the request; a first-generation pet id is also written back to the
    ///     legacy PlayerData.Pet. Returns the cur_pet for the response.
    /// </summary>
    public EHDNPMEKPCA Summon(SummonPetCsReq req)
    {
        Data.CurPetRefType = (int)req.LLGIIPALELD;
        Player.Data.Pet = req.HGEOKHHJAIECase == SummonPetCsReq.HGEOKHHJAIEOneofCase.SummonedPetId
            ? (int)req.SummonedPetId
            : 0;
        MarkDirty();

        var cur = new EHDNPMEKPCA { LLGIIPALELD = req.LLGIIPALELD };
        switch (req.HGEOKHHJAIECase)
        {
            case SummonPetCsReq.HGEOKHHJAIEOneofCase.SummonedPetId:
                cur.ODNGMIJLKKA = req.SummonedPetId;
                break;
            case SummonPetCsReq.HGEOKHHJAIEOneofCase.EBAAFDHEPLO:
                cur.EBAAFDHEPLO = req.EBAAFDHEPLO;
                break;
        }

        return cur;
    }

    /// <summary>Recall: the official response carries an empty cur_pet.</summary>
    public void Recall()
    {
        Data.CurPetRefType = 0;
        Player.Data.Pet = 0;
        MarkDirty();
    }

    public GetPetDataScRsp ToGetPetDataProto()
    {
        Reconcile();
        var proto = new GetPetDataScRsp
        {
            // The official server always includes both sub-messages, even when empty (capture of a new account).
            PLKBLNMOOPH = new EHDNPMEKPCA
            {
                LLGIIPALELD = (EOGNOEJHAGN)(Data.CurPetRefType >= 0
                    ? Data.CurPetRefType
                    : Data.Instances.Count > 0
                        ? CurPetRefTypeBlindBox
                        : 0)
            },
            // Lineup source: the official server sends 2 when a lineup exists and 0 when there is none.
            LCFKGCJCHBO = ToLineupProto(Data.Lineup.Count > 0 ? LineupSourceSet : HENPKNCKGGI.Cdcciofmcgn)
        };
        // unlocked_pet_id is a legacy field for first-generation pets. Captures of official 4.6 accounts that own pets
        // do not carry it, so it is not sent here either.
        proto.MPJNPINHCJL.AddRange(Data.Instances.Select(ToInstanceProto));
        return proto;
    }

    /// <summary>
    ///     The set-lineup response and notify echo the source enum from the request unchanged (official requests do not
    ///     carry it, so neither does the echo).
    /// </summary>
    public CMBGLKEMOGF ToLineupProto(HENPKNCKGGI sourceType)
    {
        var proto = new CMBGLKEMOGF { CGBJOAJEMKN = sourceType };
        proto.POJGKJENENA.AddRange(Data.Lineup.Select(slot => new EPFJLNKPEPK
        {
            EBAAFDHEPLO = slot.UniqueId,
            Slot = (uint)slot.Slot
        }));
        return proto;
    }

    public static JGDOHOFOFOF ToInstanceProto(PetInstanceData instance)
    {
        return new JGDOHOFOFOF
        {
            EBAAFDHEPLO = instance.UniqueId,
            PetId = (uint)instance.PetId,
            HEOHMOFNOIH = (uint)instance.ChromaLevel,
            IEBIKNPNPNO = instance.ChromaActive
        };
    }
}
