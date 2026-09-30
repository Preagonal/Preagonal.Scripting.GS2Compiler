using Preagonal.Scripting.Common;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

/// <summary>
/// Documents the C# compiler's context-sensitive trailing return 0 bug.
///
/// Official GS2 compiler behavior: a function ending with guard-form returns
/// (e.g., `if (c) { return 1; } return 2;`) emits a trailing `OP_TYPE_NUMBER 0; OP_RET`
/// (return 0) after the conditional returns. This is verified against official
/// bytecode captures (e.g., `feat-probes-shop-cache` function `ending`).
///
/// C# compiler bug: the trailing `return 0` is only emitted when the last statement
/// is NOT a ReturnStmt (see GS2Compiler.cs: `lastStatement is not ReturnStmt`).
/// For guard-form, the last statement IS a ReturnStmt, so the trailing return 0 is
/// omitted. For else-form (`if (c) { return 1; } else { return 2; }`), the last
/// statement is an IfStmt, so the trailing return 0 IS emitted.
///
/// The decompiler works around this by emitting if/else form when the official
/// bytecode contains the trailing return 0, and guard form otherwise.
/// </summary>
public class TrailingReturnZeroTests
{
    private static byte[] GetTrailingReturnZeroSequence()
    {
        var sequence = new List<byte>();
        sequence.Add((byte)Opcode.TypeNumber);
        sequence.AddRange(ImmediateOperandCodec.Official.EncodeNumber(0));
        sequence.Add((byte)Opcode.Ret);
        return sequence.ToArray();
    }

    private static bool EndsWithTrailingReturnZero(byte[] bytecode)
    {
        var expected = GetTrailingReturnZeroSequence();
        if (bytecode.Length < expected.Length) return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if (bytecode[bytecode.Length - expected.Length + i] != expected[i])
                return false;
        }
        return true;
    }

    [Fact]
    public void Given_guardFormWithReturns_When_compiled_Then_emitsTrailingReturnZero()
    {
        // Official behavior: guard-form with returns emits trailing `return 0`.
        // See: feat-probes-shop-cache `ending` function official bytecode.
        // This test is currently RED (fails) - documents the C# compiler bug.
        var source = "function testGuard() { if (temp.c) { return 1; } return 2; }";
        var result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

        Assert.True(result.Success, result.ErrMsg);
        Assert.True(
            EndsWithTrailingReturnZero(result.ByteCode),
            "Guard-form should emit trailing OP_TYPE_NUMBER 0; OP_RET to match official compiler.");
    }

    [Fact]
    public void Given_elseFormWithReturns_When_compiled_Then_emitsTrailingReturnZero()
    {
        // Control: else-form correctly emits the trailing `return 0`.
        // This test is currently GREEN (passes).
        var source = "function testElse() { if (temp.c) { return 1; } else { return 2; } }";
        var result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

        Assert.True(result.Success, result.ErrMsg);
        Assert.True(
            EndsWithTrailingReturnZero(result.ByteCode),
            "Else-form should emit trailing OP_TYPE_NUMBER 0; OP_RET.");
    }
}
