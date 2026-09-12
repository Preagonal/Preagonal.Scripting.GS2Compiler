namespace Preagonal.Scripting.GS2Compiler.UnitTests;

/// <summary>
/// Labeled loops (label: for/while + break label / continue label) are genuine
/// official GS2: the official toolchain compiles them, and the official
/// feature-test bytecode contains them (testLabeledBreak, testLabeledContinue,
/// testLabeledBreakWhile). Previously our compiler rejected the syntax with
/// "malformed input", which blocked round-tripping any decompiled source
/// that faithfully recovers labels.
/// </summary>
public class LabeledLoopTests
{
	private static void AssertCompiles(string script, string? name = null)
	{
		var result = Interface.CompileCode(script, withHeader: false);
		Assert.True(result.Success, $"expected success{(name == null ? "" : $" for {name}")}, got: {result.ErrMsg}");
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_labeled_for_with_break_label_When_compiling_Then_succeeds()
	{
		const string script =
			"""
			function testLabeledBreak() {
			  temp.found = false;
			  outer: for (temp.i = 0; temp.i < 10; temp.i++) {
			    for (temp.j = 0; temp.j < 10; temp.j++) {
			      if (temp.i * temp.j > 20) {
			        temp.found = true;
			        break outer;
			      }
			    }
			  }
			  return temp.found;
			}
			""";

		AssertCompiles(script, nameof(Given_labeled_for_with_break_label_When_compiling_Then_succeeds));
	}

	[Fact]
	public void Given_labeled_for_with_continue_label_When_compiling_Then_succeeds()
	{
		const string script =
			"""
			function testLabeledContinue() {
			  temp.s = 0;
			  outer: for (temp.i = 0; temp.i < 10; temp.i++) {
			    for (temp.j = 0; temp.j < 10; temp.j++) {
			      if (temp.j == 2) continue outer;
			      temp.s++;
			    }
			  }
			  return temp.s;
			}
			""";

		AssertCompiles(script, nameof(Given_labeled_for_with_continue_label_When_compiling_Then_succeeds));
	}

	[Fact]
	public void Given_labeled_while_with_break_label_When_compiling_Then_succeeds()
	{
		const string script =
			"""
			function testLabeledBreakWhile() {
			  temp.n = 0;
			  search: while (true) {
			    temp.n++;
			    if (temp.n > 100) break search;
			    while (temp.n % 7 == 0) {
			      temp.n++;
			      if (temp.n > 50) break search;
			      break;
			    }
			  }
			  return temp.n;
			}
			""";

		AssertCompiles(script, nameof(Given_labeled_while_with_break_label_When_compiling_Then_succeeds));
	}

	[Fact]
	public void Given_labeled_foreach_with_break_label_When_compiling_Then_succeeds()
	{
		const string script =
			"""
			function testLabeledForeach() {
			  temp.found = false;
			  outer: for (temp.x : temp.items) {
			    for (temp.y : temp.x) {
			      if (temp.y == 3) {
			        temp.found = true;
			        break outer;
			      }
			    }
			  }
			  return temp.found;
			}
			""";

		AssertCompiles(script, nameof(Given_labeled_foreach_with_break_label_When_compiling_Then_succeeds));
	}

	[Fact]
	public void Given_labeled_loop_When_compiling_twice_Then_bytecode_is_deterministic()
	{
		const string script =
			"""
			function t() {
			  temp.r = 0;
			  outer: for (temp.i = 0; temp.i < 3; temp.i++) {
			    for (temp.j = 0; temp.j < 3; temp.j++) {
			      if (temp.j == 1) break outer;
			      temp.r++;
			    }
			  }
			  return temp.r;
			}
			""";

		var first = Interface.CompileCode(script, withHeader: false);
		var second = Interface.CompileCode(script, withHeader: false);

		Assert.True(first.Success, first.ErrMsg);
		Assert.True(second.Success, second.ErrMsg);
		Assert.Equal(first.ByteCode, second.ByteCode);
	}

	[Fact]
	public void Given_unlabeled_break_continue_When_compiling_Then_still_succeeds()
	{
		const string script =
			"""
			function t() {
			  temp.r = 0;
			  for (temp.i = 0; temp.i < 3; temp.i++) {
			    for (temp.j = 0; temp.j < 3; temp.j++) {
			      if (temp.j == 1) break;
			      if (temp.j == 2) continue;
			      temp.r++;
			    }
			  }
			  while (temp.r < 100) {
			    temp.r++;
			    if (temp.r == 50) break;
			  }
			  return temp.r;
			}
			""";

		AssertCompiles(script, nameof(Given_unlabeled_break_continue_When_compiling_Then_still_succeeds));
	}

	[Fact]
	public void Given_label_on_non_loop_When_compiling_Then_it_fails()
	{
		const string script =
			"""
			function t() {
			  nope: temp.x = 1;
			  return temp.x;
			}
			""";

		var result = Interface.CompileCode(script, withHeader: false);

		Assert.False(result.Success);
	}
}
