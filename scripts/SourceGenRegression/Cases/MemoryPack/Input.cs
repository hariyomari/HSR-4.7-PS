using MemoryPack;

namespace Sample.Models
{
    [MemoryPackable]
    public partial class PublicModel
    {
        public int Value { get; set; }
    }

    [MemoryPackable]
    internal partial struct InternalStruct
    {
        public int Value;
    }

    public partial class Outer
    {
        [MemoryPackable]
        public partial class Nested
        {
            public int Value { get; set; }
        }

        // The registrar cannot name a private nested type, so it is left out.
        [MemoryPackable]
        private partial class Hidden
        {
            public int Value { get; set; }
        }
    }

    // An open generic type has no single static constructor to run and is left out.
    [MemoryPackable]
    public partial class Generic<T>
    {
        public T? Value { get; set; }
    }

    // An abstract union base is registered: its static constructor installs the union formatter.
    [MemoryPackable]
    [MemoryPackUnion(0, typeof(UnionLeaf))]
    public abstract partial class UnionBase
    {
        public int Value { get; set; }
    }

    [MemoryPackable]
    public partial class UnionLeaf : UnionBase;

    public class Plain;
}
