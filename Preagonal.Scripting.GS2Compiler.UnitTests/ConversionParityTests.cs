using System.Text;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

/// <summary>
/// Regression tests for official-toolchain conversion parity.
/// Each test verifies a ConvToObject / ConvToFloat / ConvToString emission shape
/// observed in official GS2 bytecode that the C# compiler previously dropped.
/// </summary>
public class ConversionParityTests
{
	// Opcode values from the compiler's private Op enum (verified against official disassembly).
	private const byte ConvToObject = 36;
	private const byte ConvToFloat = 33;
	private const byte ConvToString = 34;
	private const byte Array = 131;
	private const byte ArrayAssign = 132;
	private const byte ArrayNew = 38;
	private const byte MemberAccess = 35;
	private const byte Translate = 119;
	private const byte Join = 113;

	private static byte[] CompileToCode(string scriptText)
	{
		var result = Interface.CompileCode(scriptText, withHeader: false);
		Assert.True(result.Success);
		return ReadCodeSegment(result.ByteCode);
	}

	private static byte[] ReadCodeSegment(byte[] bytecode)
	{
		var offset = 0;
		while (offset + 8 <= bytecode.Length)
		{
			var type = (bytecode[offset] << 24) | (bytecode[offset + 1] << 16) | (bytecode[offset + 2] << 8) | bytecode[offset + 3];
			var length = (bytecode[offset + 4] << 24) | (bytecode[offset + 5] << 16) | (bytecode[offset + 6] << 8) | bytecode[offset + 7];
			offset += 8;
			if (type == 4) return bytecode[offset..(offset + length)];
			offset += length;
		}
		return [];
	}

	/// <summary>
	/// Decodes the code segment into a flat opcode list, skipping operands,
	/// so tests can assert on exact emission order.
	/// </summary>
	private static List<byte> DecodeOps(byte[] code)
	{
		var ops = new List<byte>();
		var i = 0;
		while (i < code.Length)
		{
			var b = code[i];
			if (b is 0xF0 or 0xF1 or 0xF2)
			{
				i += 1 + (b == 0xF0 ? 1 : b == 0xF1 ? 2 : 4);
				continue;
			}
			if (b is 0xF3 or 0xF4 or 0xF5)
			{
				i += 1 + (b == 0xF3 ? 1 : b == 0xF4 ? 2 : 4);
				continue;
			}
			if (b == 0xF6)
			{
				i++;
				while (i < code.Length && code[i] != 0) i++;
				i++;
				continue;
			}
			ops.Add(b);
			i++;
		}
		return ops;
	}

	private static bool ContainsSequence(List<byte> ops, params byte[] sequence)
	{
		for (var i = 0; i + sequence.Length <= ops.Count; i++)
		{
			var match = true;
			for (var j = 0; j < sequence.Length; j++)
				if (ops[i + j] != sequence[j]) { match = false; break; }
			if (match) return true;
		}
		return false;
	}

	[Fact]
	public void Given_translate_as_join_left_operand_When_compiling_Then_conv_to_string_follows_translate()
	{
		var ops = DecodeOps(CompileToCode("function onCreated() { temp.x = _(\"hi\") @ temp.y; }"));

		// Official: <arg>; CONV_TO_STRING; TRANSLATE; CONV_TO_STRING; ...; JOIN
		Assert.True(ContainsSequence(ops, Translate, ConvToString),
			"Expected CONV_TO_STRING immediately after TRANSLATE for _() @ x");
	}

	[Fact]
	public void Given_translate_not_in_join_When_compiling_Then_no_extra_conv_to_string_is_emitted()
	{
		var ops = DecodeOps(CompileToCode("function onCreated() { temp.x = _(\"hi\"); }"));

		// Standalone _() must not gain a trailing conversion.
		var translateIdx = ops.IndexOf(Translate);
		Assert.True(translateIdx >= 0);
		Assert.True(translateIdx + 1 >= ops.Count || ops[translateIdx + 1] != ConvToString);
	}
}
