using System.Collections.Frozen;
using System.Reflection;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace March7thHoney.GameServer.Server;

/// <summary>
///     Answers requests that no handler answered, so the client is not left waiting for a reply: a RetFail response
///     when the handler threw, and an empty response when the request has no handler at all. The response is found by
///     name (XxxCsReq to XxxScRsp); notifies and requests without a response get nothing.
/// </summary>
internal static class UnhandledRequestResponder
{
    private static readonly Logger Logger = new("GameServer");

    // An empty response to a tutorial request makes the client pop up a tutorial screen that should not appear. The
    // handlers under Packet/Recv/Tutorial answer these requests; this set only keeps the fallback silent when one of
    // them is excluded from the build or not registered.
    private static readonly FrozenSet<int> SilentRequests = new[]
    {
        CmdIds.GetTutorialCsReq, CmdIds.GetTutorialGuideCsReq, CmdIds.UnlockTutorialCsReq
    }.ToFrozenSet();

    // Name → proto message type, built once so the RetFail fallback does not enumerate the Proto assembly on
    // every handler exception.
    private static readonly Lazy<Dictionary<string, Type>> ProtoTypeMap = new(() =>
    {
        try
        {
            return typeof(PlayerGetTokenCsReq).Assembly.GetTypes()
                .GroupBy(t => t.Name)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }
        catch (Exception e)
        {
            // A type that fails to load (trimmed publish) must not take down the receive loop; the fallback
            // replies are disabled instead.
            Logger.Debug($"Proto type map unavailable, fallback replies disabled: {e.Message}");
            return new Dictionary<string, Type>(StringComparer.Ordinal);
        }
    });

    /// <summary>
    ///     After the handler of <paramref name="opcode" /> threw: sends its response with Retcode RetFail and every
    ///     field the response shares by name with the request copied from <paramref name="payload" />.
    /// </summary>
    public static async Task SendRetFailAsync(Connection connection, ushort opcode, byte[] payload)
    {
        var curPacket = March7thHoneyConnection.LogMap.GetValueOrDefault(opcode);
        if (curPacket == null || !TryDeriveResponseName(curPacket, out var rspName)) return;
        if (!March7thHoneyConnection.TryGetCmdIdByName(rspName, out var rspOpcode)) return;

        // Because of the cmdid aliases (cmdid-aliases.txt), a name in LogMap may not be a message that really
        // exists in the proto. If a type is not found, give up on the fallback reply: another exception thrown
        // here would take down the receive loop.
        var protoTypes = ProtoTypeMap.Value;
        if (!protoTypes.TryGetValue(rspName, out var typ) ||
            !protoTypes.TryGetValue(curPacket, out var curTyp)) return;

        if (Activator.CreateInstance(typ) is not IMessage rsp) return;

        var retCode = typ.GetProperty("Retcode");
        retCode?.SetValue(rsp, (uint)Retcode.RetFail);

        // Copy the fields the request and the response have in common.
        var descriptor =
            curTyp.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)?.GetValue(
                null, null) as MessageDescriptor;
        var reqPacket = descriptor?.Parser.ParseFrom(payload);

        foreach (var propertyInfo in curTyp.GetProperties())
        {
            var prop = typ.GetProperty(propertyInfo.Name);
            if (prop != null && prop.CanWrite)
            {
                var value = propertyInfo.GetValue(reqPacket);
                if (value != null)
                    prop.SetValue(rsp, value);
            }
        }

        var packet = BasePacket.Create(rspOpcode, rsp);
        await connection.SendPacket(packet);
    }

    /// <summary>For a request without a handler: sends its response with an empty body.</summary>
    public static async Task SendEmptyResponseAsync(Connection connection, ushort opcode)
    {
        if (SilentRequests.Contains(opcode)) return;

        var packetName = March7thHoneyConnection.LogMap.GetValueOrDefault(opcode);
        if (packetName == null || !TryDeriveResponseName(packetName, out var respName)) return;
        if (!March7thHoneyConnection.TryGetCmdIdByName(respName, out var respOpcode)) return;

        await connection.SendPacket(respOpcode);
    }

    /// <summary>GetXxxCsReq → GetXxxScRsp; false when the name carries no Cs/Req markers (notifies etc.).</summary>
    private static bool TryDeriveResponseName(string requestName, out string responseName)
    {
        responseName = requestName.Replace("Cs", "Sc").Replace("Req", "Rsp");
        return responseName != requestName;
    }
}
