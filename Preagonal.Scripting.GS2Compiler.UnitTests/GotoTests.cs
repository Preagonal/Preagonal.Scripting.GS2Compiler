using System.Buffers.Binary;
using System.Text;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class GotoTests
{
	[Theory]
	[InlineData("testForwardGoto", false)]
	[InlineData("testBackwardGoto", true)]
	[InlineData("testGotoIntoNestedBlock", false)]
	public void Given_local_goto_fixture_When_compiling_Then_jump_lands_on_the_labeled_statement(string functionName, bool backward)
	{
		var source = File.ReadAllText(Path.Combine(BaselineTests.FindRepoRoot(), "tests", "scripts", "statements", "05_goto.gs2"));
		var result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);
		Assert.True(result.Success, result.ErrMsg);
		var segments  = Gs2FeatureParityTests.ReadSegments(result.ByteCode);
		var code      = ReadInstructions(segments[4]);
		var strings   = Encoding.UTF8.GetString(segments[3]).Split('\0');
		var functions = Gs2FeatureParityTests.ReadFunctions(segments[2]);
		Assert.Equal(3, functions.Count);
		var start = functions.Single(function => function.Name == functionName).Address;
		var end   = functions.Where(function => function.Address > start).Select(function => function.Address - 1).DefaultIfEmpty(code.Count).Min();
		var jump  = Assert.Single(Enumerable.Range(start, end - start), index => code[index].Opcode == 1);
		var target = Assert.Single(
			Enumerable.Range(start, end - start - 3),
			index => code[index].Opcode == 189 &&
			         code[index + 1].Opcode == 22 &&
			         strings[code[index + 1].Operand!.Value] == (backward ? "sum" : "result") &&
			         code[index + 2].Opcode == 35 &&
			         code[index + 3].Opcode == (backward ? 30 : 20) &&
			         (backward || code[index + 3].Operand == 1)
		);

		Assert.Equal(target, code[jump].Operand);
		Assert.Equal(backward, target < jump);
		Assert.InRange(target, start, end - 1);
		if (functionName == "testForwardGoto")
			Assert.Contains(code.Skip(jump + 1).Take(target - jump - 1), instruction => instruction == (20, 999));
		else if (backward)
		{
			Assert.Equal(4, code[jump - 1].Opcode);
			Assert.Equal(jump + 1, code[jump - 1].Operand);
			Assert.Contains(code.Skip(target).Take(jump - target), instruction => instruction.Opcode == 52);
		}
		else
		{
			var guards = Enumerable.Range(start, end - start).Where(index => code[index].Opcode == 4).ToArray();
			Assert.Equal(2, guards.Length);
			Assert.Equal(jump + 1, code[guards[0]].Operand);
			Assert.Equal(target - 1, guards[1]);
			Assert.Equal(end - 2, code[guards[1]].Operand);
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Given_long_goto_When_compiling_Then_target_is_an_instruction_index_beyond_one_byte(bool backward)
	{
		var padding = string.Concat(Enumerable.Repeat("temp.padding = 0;", 100));
		var body    = backward ? padding + "target: temp.marker = 1;" + padding + "goto target;" : "goto target;" + padding + "target: temp.marker = 1;";
		var result  = Interface.CompileCode("function test() {" + body + "}", withHeader: false, grammar: ScriptGrammar.GS2);
		Assert.True(result.Success, result.ErrMsg);
		var segments = Gs2FeatureParityTests.ReadSegments(result.ByteCode);
		var strings  = Encoding.UTF8.GetString(segments[3]).Split('\0');
		var code     = ReadInstructions(segments[4]);
		var jump     = Assert.Single(Enumerable.Range(1, code.Count - 1), index => code[index].Opcode == 1);
		var marker   = Assert.Single(Enumerable.Range(0, code.Count - 2), index => code[index].Opcode == 189 && code[index + 1].Opcode == 22 && strings[code[index + 1].Operand!.Value] == "marker");

		Assert.Equal(marker, code[jump].Operand);
		Assert.True(marker > sbyte.MaxValue);
		Assert.Equal(backward, marker < jump);
		Assert.Equal((20, (int?)1), code[marker + 3]);
		Assert.Equal((byte)50, code[marker + 4].Opcode);
	}

	[Theory]
	[InlineData("function test() { goto missing; }")]
	[InlineData("function a() { target: temp.x=1; } function b() { goto target; }")]
	[InlineData("function a() { goto target; } function b() { target: temp.x=1; }")]
	[InlineData("function test() { target: temp.x=1; temp.f=function() { goto target; }; }")]
	[InlineData("function test() { temp.f=function() { target: temp.x=1; }; goto target; }")]
	public void Given_missing_or_cross_function_label_When_compiling_Then_goto_is_rejected(string source)
	{
		var exception = Assert.Throws<InvalidOperationException>(() => Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2));

		Assert.Contains("goto to undefined label", exception.Message);
	}

	[Fact]
	public void Given_reused_labels_in_functions_and_lambda_When_compiling_Then_each_jump_stays_in_its_function()
	{
		const string source = "function a() { goto target; temp.x=0; target: temp.x=1; temp.f=function() { goto target; temp.x=2; target: temp.x=3; }; } function b() { goto target; temp.x=4; target: temp.x=5; }";
		var          result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);
		Assert.True(result.Success, result.ErrMsg);
		var segments  = Gs2FeatureParityTests.ReadSegments(result.ByteCode);
		var code      = ReadInstructions(segments[4]);
		var functions = Gs2FeatureParityTests.ReadFunctions(segments[2]);
		Assert.Equal(3, functions.Count);
		foreach (var function in functions)
		{
			var jump   = Enumerable.Range(function.Address, code.Count - function.Address).First(index => code[index].Opcode == 1);
			var target = code[jump].Operand!.Value;
			var value  = function.Name == "a" ? 1 : function.Name == "b" ? 5 : 3;
			Assert.True(target > jump);
			Assert.Equal((byte)189, code[target].Opcode);
			Assert.Equal((20, (int?)value), code[target + 3]);
			Assert.Equal((byte)50, code[target + 4].Opcode);
		}
	}

	private static List<(byte Opcode, int? Operand)> ReadInstructions(byte[] bytes)
	{
		List<(byte Opcode, int? Operand)> instructions = [];
		for (var offset = 0; offset < bytes.Length;)
		{
			var  opcode  = bytes[offset++];
			int? operand = null;
			if (opcode is 1 or 2 or 3 or 4 or 5 or 20 or 21 or 22 or 45 or 46 or 150 or 163)
			{
				var kind = bytes[offset++];
				Assert.InRange((int)kind, 0xF0, 0xF5);
				var size = 1 << ((kind - 0xF0) % 3);
				operand = size switch
				{
					1 => kind < 0xF3 ? bytes[offset] : (sbyte)bytes[offset],
					2 => kind < 0xF3 ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)) : BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(offset)),
					_ => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset)),
				};
				offset += size;
			}

			instructions.Add((opcode, operand));
		}

		return instructions;
	}
}