using NuLua;
using NuLua.Luau;

namespace Loom.Testing.Runtime;

/// <summary>
///     Runs the arrays a spread builds instead of only reading them. A snapshot can say that a
///     <c>table.move</c> was emitted; it cannot say the elements landed in the right order, that the
///     write position survived a second spread, or that the operand was left alone - which is the whole
///     of what a spread promises.
/// </summary>
[Collection("Assembly")]
public class SpreadRuntimeTest
{
    private static readonly string _runtimeDirectory = $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}Runtime";

    public static IEnumerable<TheoryDataRow<string>> Cases =>
        Directory.EnumerateFiles(_runtimeDirectory, "spread_*.luau")
            .Select(path => new TheoryDataRow<string>(Path.GetFileNameWithoutExtension(path)));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Runs(string caseName)
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
            Assert.Fail($"{caseName} did not run: {exception.Message}");
        }
    }

    /// <remarks>
    ///     Loom spells an immutable binding <c>const</c>, which Luau has no keyword for, so it is rewritten
    ///     rather than worked around - what a spread builds does not depend on how the binding was spelled.
    /// </remarks>
    private static string Assemble(string emitted, string assertions)
    {
        // a file with nothing to export still ends in 'return nil' (Compiler.EnsureChunkReturnsAValue), which
        // can't sit before the assertions spliced on below - Luau requires 'return' to end its block.
        var body = string.Join('\n', emitted.Split('\n').Where(line => line.Trim() != "return nil"));

        return $"""
                  local function same(actual, expected)
                    if #actual ~= #expected then
                      error("length " .. #actual .. ", expected " .. #expected)
                    end
                    for index, value in expected do
                      if actual[index] ~= value then
                        error("[" .. index .. "] is " .. tostring(actual[index]) .. ", expected " .. tostring(value))
                      end
                    end
                  end
                  -- emitted
                  {body.Replace("const ", "local ")}
                  -- assertions
                  {assertions}
                  """;
    }
}
