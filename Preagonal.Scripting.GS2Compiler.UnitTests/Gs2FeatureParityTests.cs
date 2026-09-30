using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class Gs2FeatureParityTests
{
	[Theory]
	[InlineData("enums", "BLUE }", "BLUE }")]
	[InlineData("enums", "BLUE }", "BLUE, }")]
	[InlineData("enums", "SPADES }", "SPADES, }")]
	[InlineData("enums", "Suit.SPADES};", "Suit.SPADES,};")]
	[InlineData("call", "echo(1, 2)", "echo(1, 2,)")]
	[InlineData("label-break", "outer", "outer")]
	[InlineData("label-continue", "outer", "outer")]
	[InlineData("indices", "indices(2)", "indices(2)")]
	[InlineData("function-order", "zebra", "zebra")]
	[InlineData("math-builtins", "pow", "pow")]
	[InlineData("makevar", "makevar", "makevar")]
	[InlineData("duplicate-functions", "ChatBar", "ChatBar")]
	[InlineData("labels", "outer", "outer")]
	[InlineData("arena-event-aliases", "playerhurted", "playerhurted")]
	[InlineData("arena-event-canonical", "playerhurt", "playerhurt")]
	[InlineData("arena-event-aliases-explicit", "playerhurted", "playerhurted")]
	[InlineData("arena-event-canonical-explicit", "playerhurt", "playerhurt")]
	[InlineData("arena-top-level-flow", "playerhurt", "playerhurt")]
	[InlineData("nested-arrays", "temp.arr", "temp.arr")]
	[InlineData("array-compound", "temp.arr", "temp.arr")]
	[InlineData("with-scope", "this.count", "this.count")]
	[InlineData("unused-statements", "this.n", "this.n")]
	[InlineData("death-values", "NULL", "NULL")]
	[InlineData("plane-empty-params", "params", "params")]
	[InlineData("temp-aliases", "temp.i", "temp.i")]
	[InlineData("cache-aliases", "temp.i", "temp.i")]
	[InlineData("map-cache", "params", "params")]
	[InlineData("single-equals", "if(a=1)", "if(a=1)")]
	[InlineData("image-sizes", "imgwidth", "imgwidth")]
	[InlineData("image-size-arguments", "imgwidth", "imgwidth")]
	[InlineData("movement-values", "playerreading", "playerreading")]
	[InlineData("object-owners", "thiso", "thiso")]
	[InlineData("dynamic-owners", "thiso", "thiso")]
	[InlineData("shop-basics", "VK_LEFT", "VK_LEFT")]
	[InlineData("unused-builtins", "substring", "substring")]
	[InlineData("literal-trim", "trim", "trim")]
	[InlineData("shop-cache", "scheduleevent", "scheduleevent")]
	[InlineData("new-scope", "catchevent", "catchevent")]
	[InlineData("compound-values", "makevar", "makevar")]
	[InlineData("negated-chains", "directOr", "directOr")]
	[InlineData("particle-dynamic-names", "modifier", "modifier")]
	[InlineData("unary-products", "products", "products")]
	[InlineData("legacy-unary", "temp.dy", "temp.dy")]
	[InlineData("coordinate-aliases", "coords", "coords")]
	[InlineData("bare-commands", "destroy", "destroy")]
	[InlineData("serverlist-strings", "translations", "translations")]
	[InlineData("universe-functions", "first", "first")]
	[InlineData("universe-events", "onServerListerConnect", "onServerListerConnect")]
	[InlineData("legacy-universe", "onServerListerConnect", "onServerListerConnect")]
	[InlineData("level-cache", "repeated", "repeated")]
	[InlineData("literal-casts", "casts", "casts")]
	public void Given_gs2_feature_with_optional_syntax_extension_When_compiling_Then_bytecode_matches_official(string name, string original, string replacement)
	{
		var directory                                                                                                                = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Preagonal.Scripting.GS2Compiler.sln"))) directory = directory.Parent;
		Assert.NotNull(directory);
		var path   = Path.Combine(directory.FullName, "tests", "gs2-feature", "probes", name);
		var source = File.ReadAllText(path + ".gs2");
		Assert.Contains(original, source);
		using var official = JsonDocument.Parse(File.ReadAllText(path + ".official.json"));
		Assert.True(official.RootElement.GetProperty("success").GetBoolean());
		var expected = official.RootElement.GetProperty("byteCodeBase64").GetBytesFromBase64();
		Assert.Equal(official.RootElement.GetProperty("byteCount").GetInt32(), expected.Length);

		var result = Interface.CompileCode(source.Replace(original, replacement), type: "weapon", name: "-tmpweapon", withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(expected, result.ByteCode);
	}

	[Fact]
	public void Given_lambda_and_dynamic_member_probe_When_compiling_Then_all_segments_match_official()
	{
		var       path     = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs2-feature", "probes", "particle-lambda-dynamic");
		var       result   = Interface.CompileCode(File.ReadAllText(path + ".gs2"), type: "weapon", name: "-tmpweapon", withHeader: false, grammar: ScriptGrammar.GS2);
		using var official = JsonDocument.Parse(File.ReadAllText(path + ".official.json"));

		Assert.True(result.Success, result.ErrMsg);
		Assert.True(official.RootElement.GetProperty("success").GetBoolean());
		AssertEquivalent(official.RootElement.GetProperty("byteCodeBase64").GetBytesFromBase64(), result.ByteCode);
	}

	[Theory]
	[InlineData("function f() { while(true) { break missing; } }")]
	[InlineData("function f() { while(true) { continue missing; } }")]
	[InlineData("function f() { outer: while(true) { temp.f = function() { break outer; }; } }")]
	[InlineData("function f() { outer: while(true) { break; } continue outer; }")]
	public void Given_missing_or_out_of_scope_loop_label_When_compiling_Then_failure_is_reported(string source)
	{
		var exception = Assert.Throws<InvalidOperationException>(() => Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2));

		Assert.Contains("undefined loop label", exception.Message);
	}

	[Fact]
	public void Given_full_feature_script_When_compiling_Then_all_segments_match_repeated_official_results()
	{
		var root   = BaselineTests.FindRepoRoot();
		var path   = Path.Combine(root, "tests", "gs2-feature");
		var result = Interface.CompileCode(File.ReadAllText(Path.Combine(path, "feature-test.gs2")), withHeader: false, grammar: ScriptGrammar.GS2);
		Assert.True(result.Success, result.ErrMsg);
		foreach (var name in new[] { "feature-test", "feature-test-repeat", "feature-test-verified" })
		{
			using var official = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, name + ".official.json")));
			Assert.True(official.RootElement.GetProperty("success").GetBoolean());
			AssertEquivalent(official.RootElement.GetProperty("byteCodeBase64").GetBytesFromBase64(), result.ByteCode);
		}
	}

	[Fact]
	public void Given_inline_callback_fixture_When_compiling_Then_all_lambdas_remain_supported()
	{
		var root             = BaselineTests.FindRepoRoot();
		var source           = File.ReadAllText(Path.Combine(root, "tests", "scripts", "functions", "03_lambdas.gs2"));
		var compatibleSource = File.ReadAllText(Path.Combine(root, "tests", "official-baselines", "functions", "03_lambdas.official.gs2"));
		var expectedSource = "//#CLIENTSIDE\n" +
		                     source.Replace("  processWithCallback(function(a, b) {\n    return a * b;\n  });", "  temp.callback = function(a, b) {\n    return a * b;\n  };\n  processWithCallback(temp.callback);")
		                           .Replace("  temp.squared = higherOrderFunction(function(x) { return x * x; }, 5);", "  temp.square = function(x) { return x * x; };\n  temp.squared = higherOrderFunction(temp.square, 5);")
		                           .Replace("  temp.doubled = higherOrderFunction(function(x) { return x * 2; }, 10);", "  temp.double = function(x) { return x * 2; };\n  temp.doubled = higherOrderFunction(temp.double, 10);");
		Assert.Equal(expectedSource.TrimEnd(), compatibleSource.TrimEnd());

		var original = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);
		Assert.True(original.Success, original.ErrMsg);
		var functions = ReadFunctions(ReadSegments(original.ByteCode)[2]);
		Assert.Equal(
			new[] { "higherOrderFunction", "processWithCallback", "testHigherOrder", "testLambdas" },
			functions.Where(function => !function.Name.StartsWith("public.function_", StringComparison.Ordinal)).Select(function => function.Name).Order(StringComparer.Ordinal)
		);
		Assert.Equal(
			Enumerable.Range(1, 6).Select(index => "public.function_ID_" + index),
			functions.Where(function => function.Name.StartsWith("public.function_", StringComparison.Ordinal)).Select(function => Regex.Replace(function.Name, @"function_\d+_", "function_ID_")).Order(StringComparer.Ordinal)
		);
	}

	[Fact]
	public void Given_unicode_string_fixture_When_compiling_Then_unicode_matches_official_substitution()
	{
		var          path           = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "scripts", "edge_cases", "02_long_strings.gs2");
		var          source         = File.ReadAllText(path);
		const string unicode        = "Unicode: \u03b1\u03b2\u03b3\u03b4\u03b5 \u0391\u0392\u0393\u0394\u0395 \u4e2d\u6587 \u65e5\u672c\u8a9e \ud55c\uad6d\uc5b4";
		var          compatiblePath = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "official-baselines", "edge_cases", "02_long_strings.official.gs2");
		var          compatible     = File.ReadAllText(compatiblePath);
		Assert.Equal(("//#CLIENTSIDE\n" + source.Replace(unicode, "Unicode: ????? ????? ?? ??? ???")).TrimEnd(), compatible.TrimEnd());
		Assert.All(compatible, character => Assert.InRange((int)character, 0, 127));
		var result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(result.Success, result.ErrMsg);
		var strings = Encoding.UTF8.GetString(ReadSegments(result.ByteCode)[3]).Split('\0');
		// Official language mode: non-Latin-1 characters become ?, matching the official toolchain.
		Assert.Contains("Unicode: ????? ????? ?? ??? ???", strings);
	}

	[Fact]
	public void Given_enum_member_reference_When_compiling_Then_emits_type_var_like_official()
	{
		const string source = "enum State { IDLE, WALKING };\nfunction test() {\n  temp.currentState = State::IDLE;\n}\n";
		var          result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(result.Success, result.ErrMsg);
		var strings = Encoding.UTF8.GetString(ReadSegments(result.ByteCode)[3]).Split('\0');
		// Official behavior: Enum::Member is a runtime variable reference, never constant-folded.
		Assert.Contains("State::IDLE", strings);
	}

	[Fact]
	public void Given_movement_class_join_When_comparing_bytecode_Then_only_the_attachment_is_separated()
	{
		var          root       = BaselineTests.FindRepoRoot();
		var          relative   = Path.Combine("advanced", "graalx", "weapon-Movement");
		var          source     = File.ReadAllText(Path.Combine(root, "tests", "scripts", relative + ".gs2"));
		var          compatible = File.ReadAllText(Path.Combine(root, "tests", "official-baselines", relative + ".official.gs2"));
		const string attachment = "join(\"movement-functions\");\n";
		Assert.Equal(1, source.Split(attachment).Length - 1);
		Assert.Equal(source.Replace(attachment, "").TrimEnd(), compatible.TrimEnd());
		using var originalOfficial   = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "official-baselines", "evidence", relative + ".official.json")));
		using var compatibleOfficial = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "official-baselines", relative + ".official.json")));
		Assert.True(originalOfficial.RootElement.GetProperty("success").GetBoolean());
		Assert.True(compatibleOfficial.RootElement.GetProperty("success").GetBoolean());
		var expected = compatibleOfficial.RootElement.GetProperty("byteCodeBase64").GetBytesFromBase64();
		Assert.Equal(originalOfficial.RootElement.GetProperty("byteCodeBase64").GetBytesFromBase64(), expected);
		var body = Interface.CompileCode(compatible, withHeader: false, grammar: ScriptGrammar.GS2);
		Assert.True(body.Success, body.ErrMsg);
		Assert.Equal(expected, body.ByteCode);

		var original = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);
		Assert.True(original.Success, original.ErrMsg);
		// Official: a bare top-level join("literal"); is a compile-time class-join directive
		// and emits no code, so the source with the attachment compiles to the same bytes.
		Assert.Equal(expected, original.ByteCode);
	}

	[Fact]
	public void Given_bare_top_level_join_When_compiling_Then_emits_no_code()
	{
		const string source = "join(\"some-class\");\n";
		var          result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Empty(ReadSegments(result.ByteCode)[4]);
	}

	[Fact]
	public void Given_join_inside_function_When_compiling_Then_emits_runtime_call()
	{
		const string source = "function onCreated() {\n  join(\"some-class\");\n}\n";
		var          result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(result.Success, result.ErrMsg);
		var strings = Encoding.UTF8.GetString(ReadSegments(result.ByteCode)[3]).Split('\0');
		Assert.Contains("join", strings);
		Assert.Contains("some-class", strings);
	}

	[Fact]
	public void Given_join_under_top_level_conditional_When_compiling_Then_emits_runtime_call()
	{
		const string source = "if (playerenters) join(\"some-class\");\n";
		var          result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(result.Success, result.ErrMsg);
		var strings = Encoding.UTF8.GetString(ReadSegments(result.ByteCode)[3]).Split('\0');
		Assert.Contains("join", strings);
		Assert.Contains("some-class", strings);
	}

	[Fact]
	public void Given_bare_function_declaration_When_compiling_Then_emits_minimal_stub_with_shared_epilogue()
	{
		// Official: `function foo()` with no body and no semicolon gets a minimal stub
		// (no synthesized `return 0`); a single shared `0; return;` epilogue terminates
		// the code segment, targeted by the last function's prejump.
		const string source = "function bare()\nfunction after() {\n  return 1;\n}\n";
		var          result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(result.Success, result.ErrMsg);
		var code = ReadSegments(result.ByteCode)[4];
		// The shared epilogue terminates the code segment.
		Assert.Equal(new byte[] { 20, 0xF3, 0, 7 }, code[^4..]);
		// The bare function has no inline `return 0`: the only TYPENUM #0, RET in the
		// code is the epilogue (after() returns 1, so its body holds TYPENUM #1).
		var returnsZero = 0;
		for (var i = 0; i + 4 <= code.Length; i++)
			if (code[i] == 20 && code[i + 1] == 0xF3 && code[i + 2] == 0 && code[i + 3] == 7)
				returnsZero++;
		Assert.Equal(1, returnsZero);
	}

	[Fact]
	public void Given_semicolon_function_declaration_When_compiling_Then_synthesizes_inline_return()
	{
		// `function foo();` (with semicolon) is not a bare declaration: official synthesizes
		// the `return 0` inline and emits no shared epilogue.
		const string source = "function empty();\nfunction after() {\n  return 1;\n}\n";
		var          result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(result.Success, result.ErrMsg);
		var code = ReadSegments(result.ByteCode)[4];
		// empty()'s inline `return 0` appears before after()'s body (not just at the end).
		var firstZeroReturn = -1;
		for (var i = 0; i + 4 <= code.Length; i++)
			if (code[i] == 20 && code[i + 1] == 0xF3 && code[i + 2] == 0 && code[i + 3] == 7)
			{
				firstZeroReturn = i;
				break;
			}
		Assert.InRange(firstZeroReturn, 0, code.Length - 4 - 4);
	}

	[Theory]
	[InlineData("class")]
	[InlineData("weapon")]
	public void Given_server_class_attachment_When_compiling_clientside_Then_class_and_weapon_bytecode_stay_separate(string type)
	{
		var       evidence = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "official-baselines", "evidence");
		using var capture  = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "class-join-success.json")));
		var       request  = capture.RootElement.GetProperty("request");
		var       response = capture.RootElement.GetProperty("response");
		Assert.True(response.GetProperty("success").GetBoolean());
		Assert.True(response.GetProperty("temporaryScriptsRemoved").GetBoolean());
		var          source      = request.GetProperty(type + "_script_text").GetString()!;
		const string marker      = "//#CLIENTSIDE";
		var          markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
		Assert.True(markerIndex >= 0);
		var expected = response.GetProperty(type).GetProperty("byteCodeBase64").GetBytesFromBase64();
		var result   = Interface.CompileCode(source[(markerIndex + marker.Length)..], type: type, withHeader: false, grammar: ScriptGrammar.GS2);
		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(response.GetProperty(type).GetProperty("byteCount").GetInt32(), expected.Length);
		Assert.Equal(expected, result.ByteCode);
		Assert.Equal(type == "class" ? "parityClassValue" : "onCreated", Assert.Single(ReadFunctions(ReadSegments(result.ByteCode)[2])).Name);
		if (type != "weapon") return;

		var className = request.GetProperty("class_name").GetString();
		Assert.Equal("join(\"" + className + "\");\n", source[..markerIndex]);
		var properties = Assert.Single(response.GetProperty("npcProperties").EnumerateArray());
		Assert.Equal(74, properties.GetProperty("classPropertyId").GetInt32());
		Assert.Equal(className, Assert.Single(properties.GetProperty("joinedClasses").EnumerateArray()).GetString());
		using var afterRemoval = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "class-join-after-removal.json")));
		Assert.Equal(source, afterRemoval.RootElement.GetProperty("request").GetProperty("script_text").GetString());
		var repeated = afterRemoval.RootElement.GetProperty("response");
		Assert.True(repeated.GetProperty("success").GetBoolean());
		Assert.True(repeated.GetProperty("temporaryScriptRemoved").GetBoolean());
		Assert.Equal(expected, repeated.GetProperty("byteCodeBase64").GetBytesFromBase64());
		Assert.Equal(className, Assert.Single(Assert.Single(repeated.GetProperty("npcProperties").EnumerateArray()).GetProperty("joinedClasses").EnumerateArray()).GetString());
	}

	internal static void AssertEquivalent(byte[] expected, byte[] actual)
	{
		var expectedSegments = ReadSegments(expected);
		var actualSegments   = ReadSegments(actual);
		Assert.Equal(new[] { 1, 2, 3, 4 }, expectedSegments.Keys);
		Assert.Equal(expectedSegments.Keys, actualSegments.Keys);
		Assert.Equal(expectedSegments[1], actualSegments[1]);
		Assert.Equal(expectedSegments[4], actualSegments[4]);
		var expectedFunctions = ReadFunctions(expectedSegments[2]);
		var actualFunctions   = ReadFunctions(actualSegments[2]);
		var generatedName     = new Regex(@"^public\.(function_)\d+(_\d+)$", RegexOptions.CultureInvariant);
		Assert.Equal(expectedFunctions.Where(function => !generatedName.IsMatch(function.Name)), actualFunctions.Where(function => !generatedName.IsMatch(function.Name)));
		Assert.Equal(
			expectedFunctions.Where(function => generatedName.IsMatch(function.Name)).Select(function => (Name: generatedName.Replace(function.Name, "$1ID$2"), function.Address)).OrderBy(function => function.Name, StringComparer.Ordinal),
			actualFunctions.Where(function => generatedName.IsMatch(function.Name)).Select(function => (Name: generatedName.Replace(function.Name, "$1ID$2"), function.Address)).OrderBy(function => function.Name, StringComparer.Ordinal)
		);
		Assert.Equal(NormalizeStrings(expectedSegments[3], expectedFunctions), NormalizeStrings(actualSegments[3], actualFunctions));

		string[] NormalizeStrings(byte[] bytes, List<(string Name, int Address)> functions)
		{
			var names = functions.Where(function => generatedName.IsMatch(function.Name)).ToDictionary(function => function.Name[7..], function => generatedName.Replace(function.Name, "$1ID$2"));
			return Encoding.UTF8.GetString(bytes).Split('\0').Select(value => names.GetValueOrDefault(value, value)).ToArray();
		}
	}

	internal static Dictionary<int, byte[]> ReadSegments(byte[] bytes)
	{
		Dictionary<int, byte[]> segments = [];
		for (var offset = 0; offset < bytes.Length;)
		{
			var type   = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
			var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4));
			segments.Add(type, bytes.AsSpan(offset + 8, length).ToArray());
			offset += 8 + length;
		}

		return segments;
	}

	internal static List<(string Name, int Address)> ReadFunctions(byte[] bytes)
	{
		List<(string Name, int Address)> functions = [];
		for (var offset = 0; offset < bytes.Length;)
		{
			var address = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
			offset += 4;
			var end = Array.IndexOf(bytes, (byte)0, offset);
			functions.Add((Encoding.UTF8.GetString(bytes, offset, end - offset), address));
			offset = end + 1;
		}

		return functions;
	}

	[Fact]
	public void Given_corrected_full_feature_script_When_restoring_enum_trailing_comma_Then_local_bytecode_is_unchanged()
	{
		var directory                                                                                                                = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Preagonal.Scripting.GS2Compiler.sln"))) directory = directory.Parent;
		Assert.NotNull(directory);
		var source = File.ReadAllText(Path.Combine(directory.FullName, "tests", "gs2-feature", "feature-test.gs2"));
		Assert.Contains("  BLUE\n", source);
		var officialSyntax = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);
		var trailingComma  = Interface.CompileCode(source.Replace("  BLUE\n", "  BLUE,\n"), withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(officialSyntax.Success, officialSyntax.ErrMsg);
		Assert.True(trailingComma.Success, trailingComma.ErrMsg);
		Assert.Equal(officialSyntax.ByteCode, trailingComma.ByteCode);
	}
}