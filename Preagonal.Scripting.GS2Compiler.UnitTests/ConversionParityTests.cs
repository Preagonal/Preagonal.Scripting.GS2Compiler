using System.Text;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class ConversionParityTests
{
	private const byte ConvToFloat = 33;
	private const byte ArrayNew = 38;

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

	private static List<byte> DecodeOps(byte[] code)
	{
		var ops = new List<byte>();
		var i = 0;
		while (i < code.Length)
		{
			var b = code[i];
			if (b is 0xF0 or 0xF1 or 0xF2) { i += 1 + (b == 0xF0 ? 1 : b == 0xF1 ? 2 : 4); continue; }
			if (b is 0xF3 or 0xF4 or 0xF5) { i += 1 + (b == 0xF3 ? 1 : b == 0xF4 ? 2 : 4); continue; }
			if (b == 0xF6) { i++; while (i < code.Length && code[i] != 0) i++; i++; continue; }
			ops.Add(b); i++;
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
	public void Given_new_array_with_identifier_dim_When_compiling_Then_conv_to_float_precedes_array_new()
	{
		var ops = DecodeOps(CompileToCode("function onCreated() { temp.a = new [temp.n]; }"));

		// Official: <dim>; CONV_TO_FLOAT; ARRAY_NEW (24 occurrences for non-literals)
		Assert.True(ContainsSequence(ops, ConvToFloat, ArrayNew),
			"Expected CONV_TO_FLOAT before ARRAY_NEW for non-numeric dimension");
	}

	[Fact]
	public void Given_new_array_with_literal_dim_When_compiling_Then_no_conv_to_float_is_emitted()
	{
		var ops = DecodeOps(CompileToCode("function onCreated() { temp.a = new [10]; }"));

		// Official: numeric literals get no conversion (155 occurrences without).
		Assert.DoesNotContain(ConvToFloat, ops);
	}
}
