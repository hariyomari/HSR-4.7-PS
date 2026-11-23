using March7thHoney.Data;
using March7thHoney.Data.Config.Scene;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Avatar;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Activity.Loaders;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Scene.Component;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Scene;

public partial class SceneInstance
{
    #region Entity Management

    /// <summary>
    ///     Replaces a lineup avatar's field actor with a fresh one. The actor is built from the avatar's current path,
    ///     so a path change keeps the previous model until the actor is rebuilt.
    /// </summary>
    public async ValueTask RefreshAvatarActor(int baseAvatarId)
    {
        var existing = AvatarInfo.Values.FirstOrDefault(x => x.AvatarInfo.BaseAvatarId == baseAvatarId);
        if (existing == null) return;

        await existing.ClearAllBuff();
        AvatarInfo.Remove(existing.EntityId);
        Entities.Remove(existing.EntityId);
        await existing.OnDestroyInstance();

        var fresh = new AvatarSceneInfo(existing.AvatarInfo, existing.AvatarType, Player)
        {
            EntityId = ++LastEntityId
        };
        AvatarInfo[fresh.EntityId] = fresh;
        Entities[fresh.EntityId] = fresh;
        if (LeaderEntityId == existing.EntityId)
            LeaderEntityId = fresh.EntityId;

        await Player.SendPacket(new PacketSceneGroupRefreshScNotify(Player,
            new List<BaseGameEntity> { fresh }, new List<BaseGameEntity> { existing }));
    }

    public async ValueTask AddEntity(BaseGameEntity entity)
    {
        await AddEntity(entity, IsLoaded);
    }

    public async ValueTask AddEntity(BaseGameEntity entity, bool sendPacket)
    {
        if (entity.EntityId != 0) return;
        entity.EntityId = ++LastEntityId;

        Entities.Add(entity.EntityId, entity);
        if (sendPacket) await Player.SendPacket(new PacketSceneGroupRefreshScNotify(Player, entity));
    }

    public async ValueTask<Retcode> AddSummonUnitEntity(EntitySummonUnit entity)
    {
        if (entity.EntityId != 0) return Retcode.RetServerInternalError;
        entity.EntityId = ++LastEntityId;
        // get summon unit excel
        if (!GameData.SummonUnitDataData.TryGetValue(entity.SummonUnitId, out var summonUnitExcel))
            return Retcode.RetMonsterConfigNotExist;

        // get old summon unit
        var summonUnitKey = summonUnitExcel.UniqueGroup == SummonUnitUniqueGroupEnum.None ? summonUnitExcel.ID : 1;
        if (SummonUnit.TryGetValue(summonUnitKey, out var oldSummonUnit))
        {
            await RemoveSummonDeliveredBuffs(oldSummonUnit);

            await Player.SendPacket(new PacketSceneGroupRefreshScNotify(Player, null, oldSummonUnit));
            SummonUnit.Remove(summonUnitKey);
            Entities.Remove(oldSummonUnit.EntityId);
        }

        await Player.SendPacket(new PacketSceneGroupRefreshScNotify(Player, entity, null));
        Entities[entity.EntityId] = entity;
        SummonUnit[summonUnitKey] = entity;

        return Retcode.RetSucc;
    }

    public async ValueTask RemoveEntity(BaseGameEntity monster)
    {
        await RemoveEntity(monster, IsLoaded);
    }

    public async ValueTask RemoveEntity(BaseGameEntity monster, bool sendPacket)
    {
        Entities.Remove(monster.EntityId);

        if (sendPacket) await Player.SendPacket(new PacketSceneGroupRefreshScNotify(Player, null, monster));
    }

    public List<T> GetEntitiesInGroup<T>(int groupId)
    {
        List<T> entities = [];
        foreach (var entity in Entities)
            if (entity.Value.GroupId == groupId && entity.Value is T t)
                entities.Add(t);
        return entities;
    }

    #endregion
}
