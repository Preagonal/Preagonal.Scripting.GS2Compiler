namespace Preagonal.Scripting.GS2Compiler.UnitTests;

// Focused tests for indexed/register loop-variable allocation.
// Official shape (verified against official feature-test.gs2bc):
//   Temp, TypeVar "<name>", MemberAccess, MarkRegisterVariable,
//   SetRegister <slot>, IndexDec
// with small slot operands encoded 0xF3 (not 0xF0). Later reads and
// lvalue uses of an allocated variable become GetRegister <slot>.
public class RegisterAllocationTests
{
	// Opcode values from the Op enum.
	private const byte Temp = 189;
	private const byte TypeVar = 22;
	private const byte MemberAccess = 35;
	private const byte IndexDec = 32;
	private const byte SetRegister = 45;
	private const byte GetRegister = 46;
	private const byte MarkRegisterVariable = 47;
	private const byte SmallNumber = 0xF3;

	private static byte[] Compile(string script)
	{
		var result = Interface.CompileCode(script, withHeader: false);
		Assert.True(result.Success, result.ErrMsg);
		return result.ByteCode;
	}

	private static int CountOccurrences(byte[] bytes, byte value)
	{
		var count = 0;
		foreach (var b in bytes)
			if (b == value) count++;
		return count;
	}

	// Finds the first occurrence of a subsequence; returns -1 when absent.
	private static int IndexOf(byte[] bytes, byte[] pattern)
	{
		for (var i = 0; i + pattern.Length <= bytes.Length; i++)
		{
			var match = true;
			for (var j = 0; j < pattern.Length; j++)
				if (bytes[i + j] != pattern[j]) { match = false; break; }
			if (match) return i;
		}
		return -1;
	}

	[Fact]
	public void Given_for_loop_When_compiling_Then_loop_variable_uses_official_declaration_sequence()
	{
		var bytes = Compile("function onTest() { for (temp.i = 0; temp.i < 3; temp.i++) { temp.x = temp.i; } }");

		// MarkRegisterVariable must be preceded by Temp/TypeVar/<2-byte string
		// index>/MemberAccess and followed by SetRegister, small-number slot 0,
		// IndexDec.
		var prologue = new[] { MarkRegisterVariable, SetRegister, SmallNumber, (byte)0, IndexDec };
		var idx = IndexOf(bytes, prologue);
		Assert.True(idx >= 0, "register declaration prologue not found");
		Assert.Equal(MemberAccess, bytes[idx - 1]);
		Assert.Equal(TypeVar, bytes[idx - 4]);
		Assert.Equal(Temp, bytes[idx - 5]);
	}

	[Fact]
	public void Given_allocated_variable_When_reading_Then_getregister_with_small_number_slot_is_used()
	{
		var bytes = Compile("function onTest() { for (temp.i = 0; temp.i < 3; temp.i++) { temp.x = temp.i; } }");

		var read = new[] { GetRegister, SmallNumber, (byte)0 };
		Assert.True(IndexOf(bytes, read) >= 0, "GetRegister read not found");
	}

	[Fact]
	public void Given_allocated_variable_When_assigning_Then_lvalue_uses_getregister()
	{
		// temp.x is assigned inside the loop, so it is register-allocated;
		// the assignment target must be GetRegister, not Temp/TypeVar.
		var bytes = Compile("function onTest() { for (temp.i = 0; temp.i < 3; temp.i++) { temp.x = temp.i * 2; } }");

		Assert.True(CountOccurrences(bytes, GetRegister) >= 2, "expected GetRegister for both lvalue and read uses");
	}

	[Fact]
	public void Given_non_loop_variable_When_compiling_Then_no_register_is_allocated()
	{
		var bytes = Compile("function onTest() { temp.a = 1; temp.b = temp.a + 2; }");

		Assert.Equal(0, CountOccurrences(bytes, MarkRegisterVariable));
		Assert.Equal(0, CountOccurrences(bytes, GetRegister));
	}

	[Fact]
	public void Given_while_counter_When_compiling_Then_counter_is_register_allocated()
	{
		var bytes = Compile("function onTest() { temp.i = 0; while (temp.i < 3) { temp.i++; } }");

		Assert.True(IndexOf(bytes, new[] { MarkRegisterVariable, SetRegister, SmallNumber, (byte)0, IndexDec }) >= 0,
			"while counter was not register-allocated");
	}

	[Fact]
	public void Given_foreach_loop_When_compiling_Then_target_is_register_allocated()
	{
		var bytes = Compile("function onTest() { for (temp.e : { 1, 2, 3 }) { temp.s += temp.e; } }");

		Assert.True(CountOccurrences(bytes, MarkRegisterVariable) >= 1, "foreach target was not register-allocated");
	}

	[Fact]
	public void Given_nested_loops_When_compiling_twice_Then_slot_allocation_is_deterministic()
	{
		const string script = "function onTest() { for (temp.i = 0; temp.i < 2; temp.i++) { for (temp.j = 0; temp.j < 2; temp.j++) { temp.t += temp.i + temp.j; } } }";

		var first = Compile(script);
		var second = Compile(script);

		Assert.Equal(first, second);
	}

	[Fact]
	public void Given_lambda_assigning_locals_When_compiling_Then_lambda_locals_do_not_leak_into_outer_function()
	{
		// temp.q is assigned four times inside the lambda, so it is allocated a
		// register in the lambda's own function. The outer function allocates
		// temp.i and temp.x only. If lambda locals leaked into the outer
		// function's allocation, the outer function would declare temp.q too,
		// giving 4 register declarations instead of 3.
		var bytes = Compile(
			"function onTest() { temp.cb = function() { temp.q = 1; temp.q = 2; temp.q = 3; temp.q = 4; }; " +
			"for (temp.i = 0; temp.i < 3; temp.i++) { temp.x = temp.i; } }");

		Assert.Equal(3, CountOccurrences(bytes, MarkRegisterVariable));
	}

	[Fact]
	public void Given_nested_logical_with_register_reads_When_compiling_Then_jump_targets_match_pristine_compiler()
	{
		// Regression test: register-allocated reads shorten a nested "||" inside
		// a negated "&&" from three instructions to one, so the historical
		// hardcoded +6 logical jump offset targeted the wrong instruction
		// (seen in g2k1/weaponShovel.gs2 onWeaponFired). These bytes were
		// verified to normalize exactly to pristine-main output.
		var bytes = Compile(
			"function onTest() { for (temp.i = 0; temp.i < 4; temp.i++) { temp.bx = temp.i; temp.by = temp.i * 2; } " +
			"if (!(temp.bx == 15 && (temp.by == 31 || temp.by == 63))) { temp.r = 1; } }");

		var expected = Convert.FromHexString(
			"000000010000000400000000000000020000000b000000016f6e5465737400000000030000000a6279006278006900720000" +
			"0000040000008401f4004117330abd16f000232f2df30020bd16f001232f2df30120bd16f002232f2df302202ef30214f300" +
			"322ef3022114f3044804f4002c092ef3012ef302322ef3002ef3022114f3023e322ef302342001f3192ef30114f30f4605f4" +
			"00372ef30014f31f4603f400372ef30014f33f464404f4003ebd16f0032314f3013214f30007070a");

		Assert.Equal(expected, bytes);
	}
}
