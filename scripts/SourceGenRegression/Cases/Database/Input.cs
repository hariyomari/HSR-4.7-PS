using System;
using System.Collections.Generic;
using System.Data.Common;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using March7thHoney.Database;

namespace March7thHoney.Database
{
    public abstract class BaseDatabaseDataHelper
    {
        public int Uid { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DbTableAttribute(string name) : Attribute
    {
        public string Name { get; } = name;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class DbIgnoreAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class NotPlayerDataAttribute : Attribute;

    public sealed class Logger
    {
        public void Info(string message)
        {
        }
    }

    public static class DatabaseHelper
    {
        public static Logger logger = new();
    }

    // The two hand-written types of Common/Database that every generated store uses.
    public sealed record EntityStore(
        Type Type,
        string Table,
        Action<DbConnection> EnsureTable,
        Func<DbConnection, List<BaseDatabaseDataHelper>> LoadAll,
        Action<DbConnection, BaseDatabaseDataHelper> Insert,
        Action<DbConnection, BaseDatabaseDataHelper> Update,
        Action<DbConnection, int> Delete,
        Func<BaseDatabaseDataHelper, BaseDatabaseDataHelper> DeepCopy,
        Func<BaseDatabaseDataHelper, Dictionary<string, string?>> Export,
        Func<IReadOnlyDictionary<string, string?>, BaseDatabaseDataHelper> Import,
        bool IsPlayerData);

    internal static class DbSchema
    {
        public static void EnsureColumns(DbConnection connection, string table, (string Name, string Sql)[] columns)
        {
        }

        public static string? Text(object? value) => null;
    }
}

namespace Sample.Entities
{
    public enum Mood
    {
        Calm,
        Loud,
    }

    public sealed class Loadout
    {
        public int Slot { get; set; }
    }

    // Stands in for a generated protobuf message.
    public sealed class SampleMessage : IMessage<SampleMessage>
    {
        public static MessageParser<SampleMessage> Parser { get; } = new(() => new SampleMessage());
        public MessageDescriptor Descriptor => null!;
        public void MergeFrom(SampleMessage message) { }
        public void MergeFrom(CodedInputStream input) { }
        public void WriteTo(CodedOutputStream output) { }
        public int CalculateSize() => 0;
        public bool Equals(SampleMessage? other) => ReferenceEquals(this, other);
        public SampleMessage Clone() => new();
    }

    // One column of every scalar kind, in nullable and non-nullable form where both exist.
    [DbTable("sample_scalars")]
    public class ScalarEntity : BaseDatabaseDataHelper
    {
        public string Name { get; set; } = "";
        public string? Note { get; set; }
        public bool Flag { get; set; }
        public sbyte Tiny { get; set; }
        public byte Small { get; set; }
        public short Short { get; set; }
        public ushort UShort { get; set; }
        public int Count { get; set; }
        public int? MaybeCount { get; set; }
        public uint UCount { get; set; }
        public long Big { get; set; }
        public ulong UBig { get; set; }
        public float Ratio { get; set; }
        public double Precise { get; set; }
        public decimal Money { get; set; }
        public Mood Mood { get; set; }
        public Mood? MaybeMood { get; set; }
        public byte[]? Blob { get; set; }

        // None of these is a column: ignored, read-only, static.
        [DbIgnore] public int Scratch { get; set; }
        public int Computed => Count * 2;
        public static int Shared { get; set; }
    }

    // Columns stored as MemoryPack or protobuf blobs; the table takes the class name.
    public class ComplexEntity : BaseDatabaseDataHelper
    {
        public List<int> Numbers { get; set; } = [];
        public Dictionary<int, List<string>> Groups { get; set; } = [];
        public HashSet<Mood> Moods { get; set; } = [];
        public Loadout Loadout { get; set; } = new();
        public Loadout? SpareLoadout { get; set; }
        public SampleMessage Message { get; set; } = new();
        public SampleMessage? SpareMessage { get; set; }
    }

    // Inherits the columns of its base entity and adds one.
    public class DerivedEntity : ScalarEntity
    {
        public int Extra { get; set; }
    }

    // Not player save data; the marker also covers subclasses.
    [NotPlayerData]
    public class AuditEntity : BaseDatabaseDataHelper
    {
        public long At { get; set; }
    }

    public class DerivedAuditEntity : AuditEntity;

    // An abstract entity cannot be constructed and gets no store.
    public abstract class SharedEntity : BaseDatabaseDataHelper
    {
        public int Value { get; set; }
    }
}
