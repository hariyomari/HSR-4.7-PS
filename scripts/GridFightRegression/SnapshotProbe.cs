using System.Collections;
using System.Reflection;
using MemoryPack;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Game.GridFight.Persistence;

/// <summary>
///     Reflection helpers for the run persistence checks: they see every GridFightSession field, so a field that is
///     neither captured nor listed in GridFightSession.PersistenceTransientFields fails the regression.
/// </summary>
static class SnapshotProbe
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static uint _sample = 60000;

    public static IReadOnlyList<FieldInfo> PersistedFields { get; } = typeof(GridFightSession)
        .GetFields(InstanceFields)
        .Where(field => !GridFightSession.PersistenceTransientFields.Contains(field.Name))
        .OrderBy(field => field.Name, StringComparer.Ordinal)
        .ToList();

    public static byte[] Serialize(GridFightSession session) => MemoryPackSerializer.Serialize(session.CaptureSnapshot());

    public static GridFightSession RoundTrip(GridFightResourceCatalog catalog, GridFightSession session) =>
        GridFightSession.Restore(catalog, MemoryPackSerializer.Deserialize<GridFightRunSnapshot>(Serialize(session))!);

    /// <summary>First difference between two sessions over every persisted field, or null when they match.</summary>
    public static string? CompareSessions(GridFightSession expected, GridFightSession actual)
    {
        var map = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach (var field in PersistedFields)
            if (Difference(field.GetValue(expected), field.GetValue(actual), field.Name, map) is { } difference)
                return difference;
        return null;
    }

    /// <summary>
    ///     Structural comparison. GridFight objects reached twice must map to the same restored instance, which is how
    ///     shared references (an encounter's battle plan, a role in two lists) are checked.
    /// </summary>
    public static string? Difference(object? expected, object? actual, string path, Dictionary<object, object> map)
    {
        if (expected == null || actual == null)
            return expected == null && actual == null ? null : $"{path}: {Show(expected)} != {Show(actual)}";
        var type = expected.GetType();
        if (type.IsPrimitive || type.IsEnum || expected is string or decimal)
            return Equals(expected, actual) ? null : $"{path}: {Show(expected)} != {Show(actual)}";
        if (expected is GridFightMonsterExcel monster)
            return actual is GridFightMonsterExcel other && other.MonsterID == monster.MonsterID
                ? null
                : $"{path}: monster {monster.MonsterID} != {Show(actual)}";
        if (expected is IDictionary expectedDictionary)
        {
            if (actual is not IDictionary actualDictionary || actualDictionary.Count != expectedDictionary.Count)
                return $"{path}: dictionary size differs";
            var actualEntries = new List<DictionaryEntry>();
            var enumerator = actualDictionary.GetEnumerator();
            while (enumerator.MoveNext()) actualEntries.Add(enumerator.Entry);
            var index = 0;
            foreach (DictionaryEntry entry in expectedDictionary)
            {
                var other = actualEntries[index++];
                if (Difference(entry.Key, other.Key, $"{path}.key", map) is { } keyDifference) return keyDifference;
                if (Difference(entry.Value, other.Value, $"{path}[{entry.Key}]", map) is { } valueDifference)
                    return valueDifference;
            }
            return null;
        }
        if (expected is IEnumerable expectedSequence)
        {
            if (actual is not IEnumerable actualSequence) return $"{path}: {Show(actual)} is not a sequence";
            var left = expectedSequence.Cast<object?>().ToList();
            var right = actualSequence.Cast<object?>().ToList();
            if (left.Count != right.Count) return $"{path}: {left.Count} items != {right.Count} items";
            for (var index = 0; index < left.Count; index++)
                if (Difference(left[index], right[index], $"{path}[{index}]", map) is { } itemDifference)
                    return itemDifference;
            return null;
        }
        if (actual.GetType() != type) return $"{path}: {type.Name} != {actual.GetType().Name}";
        if (!type.IsValueType && type.Namespace == typeof(GridFightSession).Namespace)
        {
            if (map.TryGetValue(expected, out var mapped))
                return ReferenceEquals(mapped, actual) ? null : $"{path}: shared {type.Name} instance was not preserved";
            map[expected] = actual;
        }
        foreach (var field in type.GetFields(InstanceFields))
            if (Difference(field.GetValue(expected), field.GetValue(actual), $"{path}.{field.Name}", map) is { } difference)
                return difference;
        return null;
    }

    /// <summary>A non-default value of the given field type (fresh unique numbers, real resource rows).</summary>
    public static object Sample(Type type)
    {
        if (type == typeof(uint)) return ++_sample;
        if (type == typeof(int)) return (int)++_sample;
        if (type == typeof(bool)) return true;
        if (type == typeof(double)) return ++_sample + 0.5;
        if (type == typeof(string)) return "Sample" + ++_sample;
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(values.Length > 1 ? 1 : 0)!;
        }
        if (Nullable.GetUnderlyingType(type) is { } underlying) return Sample(underlying);
        if (type == typeof(GridFightMonsterExcel)) return GameData.GridFightMonsterData.Values.First();
        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            var array = Array.CreateInstance(element, 1);
            array.SetValue(Sample(element), 0);
            return array;
        }
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal))
                return Activator.CreateInstance(type, arguments.Select(Sample).ToArray())!;
            if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IReadOnlyCollection<>) || definition == typeof(IEnumerable<>))
            {
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(arguments))!;
                list.Add(Sample(arguments[0]));
                return list;
            }
            if (definition == typeof(HashSet<>) || definition == typeof(IReadOnlySet<>))
            {
                var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(arguments))!;
                set.GetType().GetMethod("Add")!.Invoke(set, [Sample(arguments[0])]);
                return set;
            }
            if (definition == typeof(Dictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments))!;
                dictionary.Add(Sample(arguments[0]), Sample(arguments[1]));
                return dictionary;
            }
        }

        var constructor = type.GetConstructors().OrderByDescending(ctor => ctor.GetParameters().Length).First();
        var instance = constructor.Invoke(constructor.GetParameters().Select(parameter => Sample(parameter.ParameterType)).ToArray());
        foreach (var field in type.GetFields(InstanceFields))
            if (IsEmpty(field.GetValue(instance)))
                field.SetValue(instance, Sample(field.FieldType));
        // Restore refuses board roles and shop slots whose role is not in the loaded tables, so these two carry a real one.
        if (instance is GridFightRoleState role) role.RoleId = GameData.GridFightRoleBasicInfoData.Keys.Min();
        if (instance is GridFightShopSlot slot) slot.RoleId = GameData.GridFightRoleBasicInfoData.Keys.Min();
        return instance;
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string text => text.Length == 0,
        IEnumerable sequence => !sequence.Cast<object?>().Any(),
        _ => value.GetType().IsValueType && value.Equals(Activator.CreateInstance(value.GetType())),
    };

    private static string Show(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        _ => value.ToString() ?? value.GetType().Name,
    };
}
