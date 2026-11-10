using Antlr4.Runtime;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class AntlrGrammarTests
{
	public static IEnumerable<object[]> ParserFixtures()
	{
		var root = BaselineTests.FindRepoRoot();
		yield return [Path.Combine(root, "tests", "scripts", "statements", "03_switch.gs2")];
		foreach (var fixture in BaselineTests.AdvancedParityScripts()) yield return fixture;
	}

	[Theory]
	[MemberData(nameof(ParserFixtures))]
	public void Given_fixture_When_parsed_with_generated_antlr_parser_Then_no_syntax_errors(string scriptPath)
	{
		AssertParses(File.ReadAllText(scriptPath));
	}

	[Fact]
	public void Given_cast_script_When_parsed_with_generated_antlr_parser_Then_no_syntax_errors()
	{
		AssertParses(
			"function onCreated() { if (temp.a) temp.b = 1; elseif (temp.c) temp.b = 2; else temp.b = 3; for (temp.pl: players) continue; for (; temp.i < 3; temp.i++) continue; temp.fn = function() temp.value = 1;; temp.obj = new Gui::Control(\"Name\"); temp.arr = new [2][3]; temp.i = int(\"3.8\"); temp.f = float(\"2.5\"); temp.t = _(\"Hello\"); temp.mask = ~temp.flags; temp.flags <<= 2; temp.flags >>= 1; temp.value ^= 3; temp.mask = temp.flags xor 3; } function Gui::Control.onAction() { return; } function emptyHook()"
		);
	}

	[Fact]
	public void Given_token_quirks_When_lexed_Then_expected_tokens_are_emitted()
	{
		var input         = CharStreams.fromString(":= =< => <> xor SPC NL TAB <<= >>= << >> @= |= &= Gui::Control");
		var lexer         = new GS2Lexer(input);
		var visibleTokens = lexer.GetAllTokens().Where(token => token.Channel == Lexer.DefaultTokenChannel).ToArray();
		var tokens        = visibleTokens.Select(token => token.Type).ToArray();
		Assert.Equal(
			[
				GS2Lexer.WALRUS, GS2Lexer.LTE_ALT, GS2Lexer.GTE_ALT, GS2Lexer.NEQ_ALT, GS2Lexer.BXOR, GS2Lexer.CONCAT, GS2Lexer.CONCAT, GS2Lexer.CONCAT, GS2Lexer.SHL_ASSIGN, GS2Lexer.SHR_ASSIGN, GS2Lexer.SHL, GS2Lexer.SHR, GS2Lexer.CONCAT_ASSIGN,
				GS2Lexer.BWOR_ASSIGN, GS2Lexer.BWAND_ASSIGN, GS2Lexer.IDENTIFIER,
			],
			tokens
		);
		Assert.Equal([" ", "\n", "\t"], visibleTokens.Skip(5).Take(3).Select(token => token.Text).ToArray());
		Assert.Equal("Gui::Control", visibleTokens[^1].Text);
	}

	[Fact]
	public void Given_string_and_char_constants_When_lexed_Then_text_is_unquoted()
	{
		var lexer         = new GS2Lexer(CharStreams.fromString("\"hello\" 'x'"));
		var visibleTokens = lexer.GetAllTokens().Where(token => token.Channel == Lexer.DefaultTokenChannel).ToArray();
		Assert.Equal([GS2Lexer.STRING, GS2Lexer.STRING], visibleTokens.Select(token => token.Type).ToArray());
		Assert.Equal(["hello", "x"], visibleTokens.Select(token => token.Text).ToArray());
	}

	[Fact]
	public void Given_hex_integer_When_lexed_Then_text_is_decimal()
	{
		var lexer = new GS2Lexer(CharStreams.fromString("0x10"));
		var token = lexer.GetAllTokens().Single(token => token.Channel == Lexer.DefaultTokenChannel);
		Assert.Equal(GS2Lexer.NUMBER, token.Type);
		Assert.Equal("16", token.Text);
	}

	private static void AssertParses(string code)
	{
		var input  = CharStreams.fromString(code);
		var lexer  = new GS2Lexer(input);
		var tokens = new CommonTokenStream(lexer);
		var parser = new GS2Parser(tokens);
		lexer.RemoveErrorListeners();
		parser.RemoveErrorListeners();
		var listener = new ThrowingErrorListener();
		lexer.AddErrorListener(listener);
		parser.AddErrorListener(listener);
		parser.script();
		Assert.Equal(0, parser.NumberOfSyntaxErrors);
	}

	private sealed class ThrowingErrorListener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
	{
		public void SyntaxError(
			TextWriter output,
			IRecognizer recognizer,
			int offendingSymbol,
			int line,
			int charPositionInLine,
			string msg,
			RecognitionException e
		) =>
			throw new InvalidOperationException($"{line}:{charPositionInLine} {msg}");

		public void SyntaxError(
			TextWriter output,
			IRecognizer recognizer,
			IToken offendingSymbol,
			int line,
			int charPositionInLine,
			string msg,
			RecognitionException e
		) =>
			throw new InvalidOperationException($"{line}:{charPositionInLine} {msg}");
	}
}