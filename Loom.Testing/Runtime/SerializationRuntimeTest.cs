using NuLua;
using NuLua.Luau;

namespace Loom.Testing.Runtime;

/// <summary>
///     Executes the emitted serializers instead of only reading them. Reading the output cannot tell you
///     that a nested struct comes back the wrong shape, that a local shadows the accumulator it is meant
///     to fill, or that a buffer was sized a byte short - all of which shipped past the snapshot suite
///     and were caught here.
/// </summary>
/// <remarks>
///     Each case pairs a Loom snapshot with a Luau assertion body of the same name under
///     <c>Runtime/</c>. The two are assembled with <c>Runtime/prelude.luau</c>, which stubs the Roblox
///     surface the serializers touch and the runtime helpers they call, then run on an embedded Luau.
///     The interpreter ships with the package, so these need nothing installed and run everywhere the
///     rest of the suite does.
/// </remarks>
[Collection("Assembly")]
public class SerializationRuntimeTest
{
    private static readonly string _runtimeDirectory = $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}Runtime";

    public static IEnumerable<TheoryDataRow<string>> Cases =>
        Directory.EnumerateFiles(_runtimeDirectory, "serialize_*.luau")
            .Select(path => new TheoryDataRow<string>(Path.GetFileNameWithoutExtension(path)));

    [Theory]
    [MemberData(nameof(Cases))]
    public void RoundTrips(string caseName)
    {
        var emitted = File.ReadAllText(Path.Combine(AssemblyFixture.Snapshots, "Luau", $"{caseName}.luau"));
        var assertions = File.ReadAllText(Path.Combine(_runtimeDirectory, $"{caseName}.luau"));

        using var state = LuauState.Create();
        state.OpenLibraries();

        try
        {
            state.DoString(Assemble(emitted, assertions));
        }
        catch (Exception exception)
        {
            Assert.Fail($"{caseName} did not round-trip: {exception.Message}");
        }
    }

    /// <summary>
    ///     Splices the emitted serializers between the stubs and the assertions. The runtime import is
    ///     dropped in favour of the stubbed table, and type aliases are stripped because they name Roblox
    ///     types the stubs only model structurally.
    /// </summary>
    /// <remarks>
    ///     Loom spells an immutable binding <c>const</c>, which Luau has no keyword for. The distinction
    ///     has no bearing on whether an encoding round-trips, so it is rewritten rather than worked around.
    /// </remarks>
    private static string Assemble(string emitted, string assertions)
    {
        var body = string.Join(
            '\n',
            emitted.Split('\n')
                .Where(line => !line.Contains("require(", StringComparison.Ordinal))
                .Where(line => !line.StartsWith("type ", StringComparison.Ordinal))
                .Where(line => !line.StartsWith("export type ", StringComparison.Ordinal))
                .Where(line => !line.StartsWith("  read ", StringComparison.Ordinal))
                .Where(line => line.TrimEnd() != "}")
                // a file with nothing to export still ends in 'return nil' (Compiler.EnsureChunkReturnsAValue),
                // which can't sit before the assertions spliced on below - Luau requires 'return' to end its block
                .Where(line => line.Trim() != "return nil")
        );

        var prelude = File.ReadAllText(Path.Combine(_runtimeDirectory, "prelude.luau"));
        return $"{prelude}\n-- emitted\n{body}\n-- assertions\n{assertions}\n".Replace("const ", "local ");
    }
}
