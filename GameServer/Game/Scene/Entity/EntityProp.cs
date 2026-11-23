using March7thHoney.Data.Config.Scene;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Scene;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Scene.Entity;

public class EntityProp(SceneInstance scene, MazePropExcel excel, GroupInfo group, PropInfo prop) : BaseGameEntity
{
    public Position Position { get; set; } = prop.ToPositionProto();
    public Position Rotation { get; set; } = prop.ToRotationProto();
    public SceneInstance Scene { get; set; } = scene;
    public PropStateEnum State { get; set; } = PropStateEnum.Closed;
    public int InstId { get; set; } = prop.ID;
    public MazePropExcel Excel { get; set; } = excel;
    public PropInfo PropInfo { get; set; } = prop;
    public GroupInfo Group { get; set; } = group;
    public ScenePropTimelineData? PropTimelineData { get; set; }

    /// <summary>
    ///     Created at runtime by the /spawn GM command. It borrows a real prop's group/instance ID, so its
    ///     state is never persisted (that would overwrite the real prop's saved state).
    /// </summary>
    public bool IsGmSpawned { get; set; }

    // Simulated Universe exit portal: the room it leads to, sent as extra_info.rogue_game_info.
    public RogueDoorTarget? RogueDoorTarget { get; set; }

    public override int EntityId { get; set; }
    public override int GroupId { get; set; } = group.Id;

    public override SceneEntityInfo ToProto()
    {
        var prop = new ScenePropInfo
        {
            PropId = (uint)Excel.ID,
            PropState = (uint)State
        };

        if (PropTimelineData != null)
            prop.ExtraInfo = new PropExtraInfo
            {
                TimelineInfo = PropTimelineData.ToProto()
            };

        if (RogueDoorTarget is { } door)
        {
            prop.ExtraInfo ??= new PropExtraInfo();
            prop.ExtraInfo.RogueGameInfo = new PropRogueInfo
            {
                RoomId = (uint)door.RoomId,
                MAJKFIMPOOF = (uint)door.SiteId
            };
        }

        return new SceneEntityInfo
        {
            EntityId = (uint)EntityId,
            GroupId = (uint)GroupId,
            Motion = new MotionInfo
            {
                Pos = Position.ToProto(),
                Rot = Rotation.ToProto()
            },
            InstId = (uint)InstId,
            Prop = prop
        };
    }

    public async ValueTask SetState(PropStateEnum state)
    {
        if (state == State) return;
        await SetState(state, Scene.IsLoaded);
    }

    public async ValueTask SetState(PropStateEnum state, bool sendPacket)
    {
        State = state;
        if (sendPacket)
            await Scene.Player.SendPacket(new PacketSceneGroupRefreshScNotify(Scene.Player, this, null,
                SceneGroupRefreshType.Afibfmafncc));

        if (IsGmSpawned) return;

        // save
        if (Group.SaveType != SaveTypeEnum.Reset && Scene.PersistsPropState)
            Scene.Player.SetScenePropData(Scene.FloorId, Group.Id, PropInfo.ID, state);

        // A checkpoint that just became enabled (reached on foot / by map teleport, or a mission's
        // ChangePropState) is a newly unlocked map waypoint: official SceneGroupRefresh → UnlockTeleportNotify.
        if (state == PropStateEnum.CheckPointEnable && PropInfo.MappingInfoID != 0)
            await Scene.Player.RecordTeleportUnlock(Scene.FloorId, PropInfo.MappingInfoID, sendPacket);
    }
}

/// <summary>The next room a Simulated Universe exit portal leads to (RogueRoom id + map site id).</summary>
public readonly record struct RogueDoorTarget(int RoomId, int SiteId);
