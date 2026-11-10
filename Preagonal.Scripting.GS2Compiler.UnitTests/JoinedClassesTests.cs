namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class JoinedClassesTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Given_literal_joins_When_compiling_Then_classes_are_unique_and_sorted(bool withHeader)
	{
		var result = Interface.CompileCode("join(\"zebra\"); function onCreated() { join(\"alpha\"); join(\"zebra\"); this.join(\"member\"); }", withHeader: withHeader);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(new[] { "alpha", "member", "zebra" }, result.JoinedClasses);
	}

	[Fact]
	public void Given_dynamic_or_invalid_join_arguments_When_compiling_Then_classes_are_not_reported()
	{
		var result = Interface.CompileCode("join(temp.name); join(); join(\"a\", \"b\"); other(\"c\");", withHeader: false);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Empty(result.JoinedClasses);
	}

	[Fact]
	public void Given_previous_joins_When_compiling_again_Then_classes_do_not_leak_between_compiles()
	{
		Assert.Single(Interface.CompileCode("join(\"first\");").JoinedClasses);
		Assert.Empty(Interface.CompileCode("echo(\"next\");").JoinedClasses);
		var failed = Interface.CompileCode("join(\"first\"); function {");
		Assert.False(failed.Success);
		Assert.Empty(failed.JoinedClasses);
	}
}
