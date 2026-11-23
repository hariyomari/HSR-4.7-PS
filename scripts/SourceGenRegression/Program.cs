using March7thHoney.SourceGen;
using March7thHoney.SourceGen.Database;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Compares each generator's output with checked-in expectations for reviewable diffs.
//
//   dotnet run --project scripts/SourceGenRegression/SourceGenRegression.csproj -c Release
//   dotnet run --project scripts/SourceGenRegression/SourceGenRegression.csproj -c Release -- --update
//
// --update rewrites the expectations from the current generators; review the diff before committing.
// Each case is a directory under Cases/ with an Input.cs and the files the generator must emit.
// Also checks output reuse after unrelated edits and errors for duplicate declarations.

var update = args.Contains("--update");
var casesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Cases"));
var checks = 0;
var failures = 0;

void Check(string name, bool passed, string? detail = null)
{
    checks++;
    if (!passed) failures++;
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}{(passed || detail == null ? "" : $": {detail}")}");
}

// The generated samples are compiled against everything this program itself can load.
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
    .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
    .ToList();
var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
    nullableContextOptions: NullableContextOptions.Enable);

static string Normalize(string text) => text.Replace("\r\n", "\n");

CSharpCompilation Compile(string caseName, string source) => CSharpCompilation.Create(
    "Sample" + caseName,
    [CSharpSyntaxTree.ParseText(source, parseOptions, path: "Input.cs")],
    references,
    compilationOptions);

void RunCase(string caseName, IIncrementalGenerator generator)
{
    var directory = Path.Combine(casesRoot, caseName);
    var input = File.ReadAllText(Path.Combine(directory, "Input.cs"));
    var compilation = Compile(caseName, input);

    var inputErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    Check($"{caseName}: the input compiles on its own", inputErrors.Count == 0,
        string.Join("; ", inputErrors.Take(3)));

    var driver = CSharpGeneratorDriver.Create(generator)
        .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
    Check($"{caseName}: the generator reports nothing", generatorDiagnostics.IsEmpty,
        string.Join("; ", generatorDiagnostics.Take(3)));

    var outputErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    Check($"{caseName}: the generated code compiles", outputErrors.Count == 0,
        string.Join("; ", outputErrors.Take(3)));

    var generated = driver.GetRunResult().Results.Single().GeneratedSources
        .ToDictionary(s => s.HintName, s => Normalize(s.SourceText.ToString()));
    var expectedFiles = Directory.EnumerateFiles(directory, "*.g.cs").Select(Path.GetFileName).OfType<string>().ToList();

    if (update)
    {
        foreach (var stale in expectedFiles.Where(name => !generated.ContainsKey(name)))
            File.Delete(Path.Combine(directory, stale));
        foreach (var (name, text) in generated)
            File.WriteAllText(Path.Combine(directory, name), text);
        Console.WriteLine($"UPDATED {caseName}: {string.Join(", ", generated.Keys.Order())}");
        return;
    }

    Check($"{caseName}: emits exactly the expected files",
        generated.Keys.Order().SequenceEqual(expectedFiles.Order()),
        $"emitted [{string.Join(", ", generated.Keys.Order())}], expected [{string.Join(", ", expectedFiles.Order())}]");
    foreach (var name in expectedFiles.Where(generated.ContainsKey))
    {
        var expected = Normalize(File.ReadAllText(Path.Combine(directory, name)));
        var actual = generated[name];
        Check($"{caseName}: {name} matches the expectation", actual == expected, FirstDifference(expected, actual));
    }
}

static string FirstDifference(string expected, string actual)
{
    var expectedLines = expected.Split('\n');
    var actualLines = actual.Split('\n');
    for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
    {
        var e = i < expectedLines.Length ? expectedLines[i] : "<end of file>";
        var a = i < actualLines.Length ? actualLines[i] : "<end of file>";
        if (e != a) return $"line {i + 1}: expected '{e.Trim()}', generated '{a.Trim()}'";
    }
    return "";
}

// A second declaration for a key that can hold one is an error at the duplicate, not a silent overwrite.
void ExpectDuplicateError(string caseName, IIncrementalGenerator generator, string duplicate, string id)
{
    var input = File.ReadAllText(Path.Combine(casesRoot, caseName, "Input.cs"));
    var compilation = Compile(caseName, input)
        .AddSyntaxTrees(CSharpSyntaxTree.ParseText(duplicate, parseOptions, path: "Duplicate.cs"));
    CSharpGeneratorDriver.Create(generator)
        .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
    Check($"{caseName}: a duplicate is reported as {id} where it is declared",
        diagnostics is [{ Severity: DiagnosticSeverity.Error } diagnostic] && diagnostic.Id == id &&
        diagnostic.Location.GetLineSpan().Path == "Duplicate.cs",
        string.Join("; ", diagnostics));
}

// An edit that does not touch what a generator reads must not make it produce its output again.
void ExpectReuse(string caseName, IIncrementalGenerator generator)
{
    const string unrelated = """
        namespace Sample.Unrelated
        {
            public class Later : System.Exception;

            public abstract class LaterBase;
        }
        """;
    var compilation = Compile(caseName, File.ReadAllText(Path.Combine(casesRoot, caseName, "Input.cs")));
    GeneratorDriver driver = CSharpGeneratorDriver.Create(
        [generator.AsSourceGenerator()],
        parseOptions: parseOptions,
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    driver = driver.RunGenerators(compilation);
    driver = driver.RunGenerators(
        compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(unrelated, parseOptions, path: "Unrelated.cs")));

    var result = driver.GetRunResult().Results.Single();
    // "Model" is the name each generator gives the step that holds everything its output is written from.
    var models = result.TrackedSteps.TryGetValue("Model", out var steps)
        ? steps.SelectMany(step => step.Outputs).Select(o => o.Reason).ToList()
        : [];
    Check($"{caseName}: the model is unchanged by an unrelated edit",
        models.Count > 0 && models.All(r => r is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached),
        string.Join(", ", models));
    var outputs = result.TrackedOutputSteps.Values.SelectMany(s => s).SelectMany(step => step.Outputs)
        .Select(o => o.Reason).ToList();
    Check($"{caseName}: the output is reused after an unrelated edit",
        outputs.Count > 0 && outputs.All(r => r == IncrementalStepRunReason.Cached),
        string.Join(", ", outputs));
}

(string Name, Func<IIncrementalGenerator> Create)[] cases =
[
    ("Handlers", () => new HandlerRegistryGenerator()),
    ("Missions", () => new MissionHandlerRegistryGenerator()),
    ("Commands", () => new CommandRegistryGenerator()),
    ("MemoryPack", () => new MemoryPackRegistrarGenerator()),
    ("Database", () => new DatabaseStoreGenerator()),
];

foreach (var (name, create) in cases) RunCase(name, create());

if (!update)
{
    foreach (var (name, create) in cases) ExpectReuse(name, create());

    ExpectDuplicateError("Handlers", new HandlerRegistryGenerator(), """
        namespace Sample.Handlers
        {
            [March7thHoney.GameServer.Server.Packet.Opcode(Ids.Ping)]
            public class HandlerPingAgain : March7thHoney.GameServer.Server.Packet.Handler;
        }
        """, "M7H001");
    ExpectDuplicateError("Missions", new MissionHandlerRegistryGenerator(), """
        using March7thHoney.Enums.Mission;
        using March7thHoney.GameServer.Game.Mission.FinishType;

        namespace Sample.Missions
        {
            [MissionFinishType(MissionFinishTypeEnum.Talk)]
            public class TalkAgainHandler : MissionFinishTypeHandler;
        }
        """, "M7H002");
    ExpectDuplicateError("Database", new DatabaseStoreGenerator(), """
        namespace Sample.Other
        {
            public class ComplexEntity : March7thHoney.Database.BaseDatabaseDataHelper
            {
                public int Value { get; set; }
            }
        }
        """, "M7H003");
}

Console.WriteLine($"SourceGen regression checks: {checks}, failures: {failures}");
return failures == 0 ? 0 : 1;
