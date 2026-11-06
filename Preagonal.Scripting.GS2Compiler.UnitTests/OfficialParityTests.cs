using Preagonal.Scripting.Common;
using Preagonal.Scripting.Corpus;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

/// <summary>
/// Direct C#-compiler-vs-official-toolchain parity gate.
///
/// For every corpus fixture that has captured official bytecode, compiles the
/// fixture source with the C# compiler and asserts the output is equivalent to
/// the official toolchain's bytecode, using
/// <see cref="BytecodeEquivalence.AreEquivalent"/> for normalized comparison
/// (tolerates the official toolchain's nondeterministic generated-lambda ids).
///
/// This is the official-parity counterpart to <see cref="CorpusCompilerTests"/>,
/// which only checks the C# compiler against its own prior captures
/// (self-regression). A failure here means the C# compiler diverges from the
/// official toolchain on real-world source shapes.
///
/// Intended to go red when a fixture exposes a compiler bug.
/// </summary>
public class OfficialParityTests
{
    public static TheoryData<string> FixtureNamesWithOfficialBytecode()
    {
        var data = new TheoryData<string>();
        foreach (var name in CorpusFixtures.DiscoverFixtureNames())
            if (CorpusFixtures.ReadOfficialBytecode(name) is not null)
                data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureNamesWithOfficialBytecode))]
    public void Fixture_compiles_to_official_equivalent_bytecode(string fixtureName)
    {
        var official = CorpusFixtures.ReadOfficialBytecode(fixtureName)
            ?? throw new InvalidOperationException("fixture has no captured official bytecode: " + fixtureName);

        var response = Interface.CompileCode(CorpusFixtures.ReadSource(fixtureName), withHeader: false);
        Assert.True(response.Success, "[" + fixtureName + "] compile failed: " + response.ErrMsg);
        var csharp = response.ByteCode
            ?? throw new InvalidOperationException("[" + fixtureName + "] compiler reported success with null bytecode.");

        var result = BytecodeEquivalence.AreEquivalent(official, csharp);
        Assert.True(result.IsEquivalent,
            "[" + fixtureName + "] C# bytecode differs from official: " + string.Join("; ", result.Differences));
    }
}
