using System;
using March7thHoney.GameServer.Server.Packet;

namespace March7thHoney.GameServer.Server.Packet
{
    [AttributeUsage(AttributeTargets.Class)]
    public class Opcode(int cmdId) : Attribute
    {
        public int CmdId { get; } = cmdId;
    }

    public abstract class Handler;
}

namespace Sample.Handlers
{
    public static class Ids
    {
        public const int Login = 12;
        public const int Ping = 3;
    }

    [Opcode(Ids.Login)]
    public class HandlerLogin : Handler;

    [Opcode(Ids.Ping)]
    public class HandlerPing : Handler;

    // An abstract handler cannot be constructed and is left out.
    [Opcode(99)]
    public abstract class HandlerShared : Handler;

    public class NotAHandler;
}
