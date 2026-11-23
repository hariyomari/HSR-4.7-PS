using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.GridFight;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.GridFight;

[Opcode(CmdIds.GridFightEquipDressCsReq)]
public sealed class HandlerGridFightEquipDressCsReq : GridFightHandler<GridFightEquipDressCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightEquipDressCsReq request)
    {
        var manager = player.GridFightManager!;
        var result = manager.Session?.DressEquipment(request.RoleUniqueId, request.EquipmentUniqueId) ??
                     GridFightEquipDressResult.Failed;
        if (result.Success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildEquipDressSync(result)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightEquipDressScRsp,
            new GridFightEquipDressScRsp
            {
                Retcode = result.Success ? 0 : (uint)Retcode.RetFail,
            }));
    }
}

[Opcode(CmdIds.GridFightEquipCraftCsReq)]
public sealed class HandlerGridFightEquipCraftCsReq : GridFightHandler<GridFightEquipCraftCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightEquipCraftCsReq request)
    {
        var manager = player.GridFightManager!;
        var result = manager.Session?.CraftEquipment(
                         request.TargetEquipmentId,
                         request.MaterialUniqueIdList.ToList(),
                         request.Source,
                         request.TrackId) ??
                     GridFightEquipCraftResult.Failed;
        if (result.Success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildEquipCraftSync(result)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightEquipCraftScRsp,
            new GridFightEquipCraftScRsp
            {
                Retcode = result.Success ? 0 : (uint)Retcode.RetFail,
            }));
    }
}

[Opcode(CmdIds.GridFightUseConsumableCsReq)]
public sealed class HandlerGridFightUseConsumableCsReq : GridFightHandler<GridFightUseConsumableCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightUseConsumableCsReq request)
    {
        var manager = player.GridFightManager!;
        var target = request.Target;
        if (target?.DECONIENEJG != null && manager.Session != null)
        {
            // Spotlight (主角光环) consumable: consume, then open the granted spotlight selection for the target role.
            var spot = manager.Session.UseSpotLightConsumable(request.GroupId, target.DECONIENEJG.MDNAGANGNKK);
            if (spot.Success)
            {
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildConsumableUseSync(spot)));
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildSpotLightSync(null, 0)));
            }
            await connection.SendPacket(BasePacket.Create(CmdIds.GridFightUseConsumableScRsp,
                new GridFightUseConsumableScRsp { Retcode = spot.Success ? 0 : (uint)Retcode.RetFail }));
            return;
        }
        var roleUniqueId = target?.RemoveTarget?.RoleUniqueId ??
                           target?.RollTarget?.RoleUniqueId ??
                           target?.CopyTarget?.RoleUniqueId ??
                           target?.RecommendTarget?.RoleUniqueId ?? 0;
        var equipmentUniqueId = target?.RollTarget?.EquipmentUniqueId ??
                                target?.UpgradeTarget?.EquipmentUniqueId ?? 0;
        var chimeraUniqueId = target?.CEBGLMPKPDC?.FJLIFIIJGPH ?? 0;
        var result = manager.Session?.UseConsumable(
                         request.GroupId,
                         roleUniqueId,
                         equipmentUniqueId,
                         chimeraUniqueId) ??
                     GridFightConsumableUseResult.Failed;
        if (result.Success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildConsumableUseSync(result)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightUseConsumableScRsp,
            new GridFightUseConsumableScRsp
            {
                Retcode = result.Success ? 0 : (uint)Retcode.RetFail,
            }));
    }
}

[Opcode(CmdIds.GridFightUpdateEquipTrackCsReq)]
public sealed class HandlerGridFightUpdateEquipTrackCsReq : GridFightHandler<GridFightUpdateEquipTrackCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightUpdateEquipTrackCsReq request)
    {
        var manager = player.GridFightManager!;
        var retcode = manager.Session?.UpdateEquipmentTracks(request.DKEIDONNDCJ, request.Source) ??
                      (uint)Retcode.RetFail;
        if (retcode == 0)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildEquipmentTrackSync()));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightUpdateEquipTrackScRsp,
            new GridFightUpdateEquipTrackScRsp { Retcode = retcode }));
    }
}
