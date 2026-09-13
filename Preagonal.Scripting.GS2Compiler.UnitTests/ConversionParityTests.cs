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
	public void Given_nested_index_read_When_compiling_Then_conv_to_object_follows_inner_array()
	{
		var ops = DecodeOps(CompileToCode("function onCreated() { temp.x = a[0][1]; }"));

		// Official: a; CONV_TO_OBJECT; 0; ARRAY; CONV_TO_OBJECT; 1; ARRAY
		Assert.True(ContainsSequence(ops, Array, ConvToObject),
			"Expected CONV_TO_OBJECT immediately after the inner ARRAY for a[0][1]");
	}

	[Fact]
	public void Given_nested_index_write_When_compiling_Then_conv_to_object_follows_inner_array()
	{
		var ops = DecodeOps(CompileToCode("function onCreated() { a[0][1] = temp.x; }"));

		Assert.True(ContainsSequence(ops, Array, ConvToObject),
			"Expected CONV_TO_OBJECT immediately after the inner ARRAY for a[0][1] = x");
	}

	[Fact]
	public void Given_member_access_on_index_read_When_compiling_Then_conv_to_object_is_emitted()
	{
		var ops = DecodeOps(CompileToCode("function onCreated() { temp.x = a[0].b; }"));

		// Official: a; CONV_TO_OBJECT; 0; ARRAY; CONV_TO_OBJECT; "b"; MEMBER_ACCESS
		Assert.True(ContainsSequence(ops, Array, ConvToObject),
			"Expected CONV_TO_OBJECT after ARRAY before member access for a[0].b");
	}

	[Fact]
	public void Given_member_access_on_index_write_When_compiling_Then_conv_to_object_is_emitted()
	{
		var ops = DecodeOps(CompileToCode("function onCreated() { a[0].b = temp.x; }"));

		Assert.True(ContainsSequence(ops, Array, ConvToObject),
			"Expected CONV_TO_OBJECT after ARRAY before member write for a[0].b = x");
	}

	[Fact]
	public void Given_plain_index_read_When_compiling_Then_single_conv_to_object_is_emitted()
	{
		var ops = DecodeOps(CompileToCode("function onCreated() { temp.x = a[0]; }"));

		// a[0] must still convert the target exactly once; no double conversion.
		var count = ops.Count(o => o == ConvToObject);
		Assert.Equal(1, count);
	}
}
