using Preagonal.Scripting.Corpus;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

/// <summary>
/// Executes the shared language corpus (embedded in the Preagonal.Scripting.Common
/// package) through the C# compiler: every fixture source must compile to
/// byte-identical output versus the captured <c>.csharp.json</c> bytecode, and
/// the negative fixtures must fail to compile.
/// </summary>
public class CorpusCompilerTests
{
    public static TheoryData<string> PositiveFixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in CorpusFixtures.DiscoverFixtureNames())
        {
            byte[]? expected;
            try
            {
                expected = CorpusFixtures.ReadCSharpBytecode(name);
            }
            catch (InvalidOperationException)
            {
                // Fixture has no captured C# baseline at all (e.g. official-only
                // fixtures such as scr-statements-06_guard_return, which record
                // official captures but no C# bytecode). Skip it instead of
                // capturing the current output as a baseline, which could
                // enshrine a compiler bug.
                continue;
            }
            if (expected is not null)
                data.Add(name);
        }
        return data;
    }

    public static TheoryData<string> NegativeFixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in CorpusFixtures.DiscoverFixtureNames())
            if (CorpusFixtures.ReadStatus(name) == "pending-manual-capture")
                data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(PositiveFixtureNames))]
    public void Fixture_compiles_to_captured_csharp_bytecode(string fixtureName)
    {
        var expected = CorpusFixtures.ReadCSharpBytecode(fixtureName)
            ?? throw new InvalidOperationException("fixture has no captured C# bytecode: " + fixtureName);
        var response = Interface.CompileCode(CorpusFixtures.ReadSource(fixtureName), withHeader: false);
        Assert.True(response.Success, "[" + fixtureName + "] compile failed: " + response.ErrMsg);
        var actual = response.ByteCode
            ?? throw new InvalidOperationException("[" + fixtureName + "] compiler reported success with null bytecode.");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(NegativeFixtureNames))]
    public void Fixture_negative_cases_fail_to_compile(string fixtureName)
    {
        var response = Interface.CompileCode(CorpusFixtures.ReadSource(fixtureName), withHeader: false);
        Assert.False(response.Success, "[" + fixtureName + "] negative fixture unexpectedly compiled.");
        Assert.False(string.IsNullOrEmpty(response.ErrMsg));
    }
}
