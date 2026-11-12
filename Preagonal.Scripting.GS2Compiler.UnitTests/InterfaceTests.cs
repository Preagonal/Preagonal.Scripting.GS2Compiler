using System.Text;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class InterfaceTests
{
	[Theory]
	[InlineData("a,b", "|-1,1|")]
	[InlineData("a,b,c", "|0,10|")]
	[InlineData("a+1,b-1", "<0,10>")]
	[InlineData("a,b", "|3,1|")]
	[InlineData("a,b", "{1,2}")]
	[InlineData("playerenters,playerchats", "|0,1|")]
	public void Given_gs1_multi_value_membership_When_compiling_Then_all_subjects_match_the_array_form(string subjects, string range)
	{
		var source      = "//#CLIENTSIDE\nif(" + subjects + " in " + range + ") hideimg 1;";
		var arraySource = "//#CLIENTSIDE\nif({" + subjects + "} in " + range + ") hideimg 1;";

		var actual   = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS1);
		var expected = Interface.CompileCode(arraySource, withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(actual.Success, actual.ErrMsg);
		Assert.True(expected.Success, expected.ErrMsg);
		Assert.Equal(expected.ByteCode, actual.ByteCode);
	}

	[Fact]
	public void Given_script_that_is_faulty_When_compiling_Then_success_is_false_and_error_message_is_returned()
	{
		const string scriptText = """
		                          			//#CLIENTSIDE
		                          			function onCreated() 
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.False(result.Success);
		Assert.Equal("malformed input at line 3: \t\t\t}\n", result.ErrMsg);
	}

	[Fact]
	public void Given_script_that_is_correct_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			//#CLIENTSIDE
		                          			function onCreated() {
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_script_that_is_correct_When_compiling_without_header_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			//#CLIENTSIDE
		                          			function onCreated() {
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_script_and_explicit_gs2_grammar_When_compiling_Then_bytecode_matches_default()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  this.value = 1;
		                          			}
		                          """;

		var defaultResult  = Interface.CompileCode(scriptText, withHeader: false);
		var explicitResult = Interface.CompileCode(scriptText, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(defaultResult.Success);
		Assert.True(explicitResult.Success);
		Assert.Equal(defaultResult.ByteCode, explicitResult.ByteCode);
	}

	[Fact]
	public void Given_common_script_When_compiling_with_gs1_or_gs2_grammar_Then_bytecode_matches()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  this.value = 1;
		                          			  this.value += 2;
		                          			}
		                          """;

		var gs2Result = Interface.CompileCode(scriptText, withHeader: false, grammar: ScriptGrammar.GS2);
		var gs1Result = Interface.CompileCode(scriptText, withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(gs2Result.Success, gs2Result.ErrMsg);
		Assert.True(gs1Result.Success, gs1Result.ErrMsg);
		Assert.Equal(gs2Result.ByteCode, gs1Result.ByteCode);
	}

	[Fact]
	public void Given_gs1_command_script_When_compiling_with_gs1_grammar_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			set playerhearts;
		                          			unset playerhearts;
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(result.Success, result.ErrMsg);
		Assert.NotEmpty(result.ByteCode);
	}

	[Theory]
	[InlineData("messageHey;", "message Hey;")]
	[InlineData("messageHey,there;", "message Hey,there;")]
	[InlineData("say2Hello,there;", "say2 Hello,there;")]
	[InlineData("toweaponsShovel;", "toweapons Shovel;")]
	[InlineData("hideimg1;", "hideimg 1;")]
	[InlineData("setanicn_shoot,one,two,three;", "setani cn_shoot,one,two,three;")]
	[InlineData("addtiledef2alienpics.png,alien1.nw,1728,0;", "addtiledef2 alienpics.png,alien1.nw,1728,0;")]
	[InlineData("freezeplayer0.2;", "freezeplayer 0.2;")]
	[InlineData("seteffectmode1;", "seteffectmode 1;")]
	[InlineData("takehorse1;", "takehorse 1;")]
	[InlineData("setgifpartpics1.png,0,0,16,16;", "setgifpart pics1.png,0,0,16,16;")]
	public void Given_gs1_command_without_argument_space_When_compiling_Then_bytecode_matches_spaced_command(string compact, string spaced)
	{
		var expected = Interface.CompileCode("//#CLIENTSIDE\nif(created){" + spaced + "}", withHeader: false, grammar: ScriptGrammar.GS1);
		var actual   = Interface.CompileCode("//#CLIENTSIDE\nif(created){" + compact + "}", withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(expected.Success, expected.ErrMsg);
		Assert.True(actual.Success, actual.ErrMsg);
		Assert.Equal(expected.ByteCode, actual.ByteCode);
	}

	[Theory]
	[InlineData("message Hey,there")]
	[InlineData("say2 Hello,there")]
	[InlineData("setani cn_shoot,one,two,three")]
	[InlineData("setani idle")]
	[InlineData("setstring greeting,Hello,there")]
	[InlineData("setstring test2,{this; {is}; a test}")]
	[InlineData("message {this; {is}; a test}")]
	[InlineData("say2 {this; {is}; a test}")]
	[InlineData("hideimg 1")]
	[InlineData("setbackpal pics1.png")]
	[InlineData("destroy")]
	[InlineData("this.value=1")]
	[InlineData("this.value++")]
	[InlineData("if(playerenters){message Hey}")]
	public void Given_gs1_final_statement_without_semicolon_When_compiling_Then_bytecode_matches_terminated_statement(string statement)
	{
		var expected = Interface.CompileCode("//#CLIENTSIDE\nif(created){" + statement + ";}", withHeader: false, grammar: ScriptGrammar.GS1);
		var actual   = Interface.CompileCode("//#CLIENTSIDE\nif(created){" + statement + "}", withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(expected.Success, expected.ErrMsg);
		Assert.True(actual.Success, actual.ErrMsg);
		Assert.Equal(expected.ByteCode, actual.ByteCode);
	}

	[Theory]
	[InlineData("message Hey,there,friend;", "Hey,there,friend")]
	[InlineData("say2 Hey,there,friend;", "Hey,there,friend")]
	[InlineData("setstring greeting,Hey,there,friend;", "Hey,there,friend")]
	[InlineData("setani cn_shoot,one,two,three;", "one,two,three")]
	[InlineData("setstring test2,{this; {is}; a test};", "{this; {is}; a test}")]
	[InlineData("setstring test2,{#w; {#s(race)}; a test};", "{#w; {#s(race)}; a test}")]
	[InlineData("message prefix{this; {is}; a test}suffix;", "prefix{this; {is}; a test}suffix")]
	[InlineData("setani cn_shoot,{one,two; {three}},four;", "{one,two; {three}},four")]
	[InlineData("message ##c;", "#c")]
	[InlineData("setbackpal pics1.png;", "setbackpal")]
	[InlineData("setbackpal pics1.png;", "pics1.png")]
	[InlineData("toweapons #s(weapon);", "#s(weapon)")]
	[InlineData("replaceani idle,#s(animation);", "#s(animation)")]
	public void Given_gs1_final_string_argument_with_commas_When_compiling_Then_string_is_not_split(string statement, string argument)
	{
		var result = Interface.CompileCode("//#CLIENTSIDE\nif(created){" + statement + "}", withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Contains(argument, ReadStringTable(result.ByteCode));
	}

	[Fact]
	public void Given_gs1_reserved_pi_constant_When_compiling_Then_bytecode_matches_gs2()
	{
		const string source = "function test(){this.value=pi;}";
		var          gs1    = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS1);
		var          gs2    = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(gs1.Success, gs1.ErrMsg);
		Assert.True(gs2.Success, gs2.ErrMsg);
		Assert.Equal(gs2.ByteCode, gs1.ByteCode);
	}

	[Fact]
	public void Given_gs1_direct_assignment_of_braced_text_When_compiling_Then_it_is_not_accepted_as_setstring()
	{
		var result = Interface.CompileCode("//#CLIENTSIDE\nif(created){test2={this; {is}; a test};}", withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.False(result.Success);
	}

	[Fact]
	public void Given_gs1_top_level_object_events_When_compiling_with_gs1_grammar_Then_gs1_event_flags_are_written()
	{
		const string scriptText = """
		                          			if (playerenters || washit) {
		                          			  this.value = 1;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal((1 << 0) | (1 << 22), ReadGs1EventFlags(result.ByteCode));
	}

	[Theory]
	[InlineData(ScriptGrammar.GS1)]
	[InlineData(ScriptGrammar.GS2)]
	public void Given_noncanonical_event_names_When_compiling_Then_they_remain_ordinary_identifiers(ScriptGrammar grammar)
	{
		const string scriptText = """
		                          			if (playertouchesme || playertouchesother || playerhurted || wasshooted) {
		                          			  this.value = 1;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false, grammar: grammar);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(0, ReadGs1EventFlags(result.ByteCode));
	}

	[Fact]
	public void Given_gs1_created_condition_When_compiling_with_gs1_grammar_Then_gs1_event_flags_stay_zero()
	{
		const string scriptText = """
		                          			if (created) {
		                          			  this.value = 1;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(0, ReadGs1EventFlags(result.ByteCode));
	}

	[Fact]
	public void Given_gs2_top_level_object_event_name_When_compiling_with_gs2_grammar_Then_gs1_event_flags_are_written()
	{
		const string scriptText = """
		                          			//#CLIENTSIDE
		                          			function onCreated() {
		                          			  echo("created");
		                          			}

		                          			if (playerenters) {
		                          			  triggeraction(0, 0, "serverside", player.weapon, "test", "hi");
		                          			}
		                          """;

		var result        = Interface.CompileCode(scriptText, withHeader: false, grammar: ScriptGrammar.GS2);
		var functionNames = ReadFunctionNames(result.ByteCode);
		var strings       = ReadStringTable(result.ByteCode);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(1 << 0, ReadGs1EventFlags(result.ByteCode));
		Assert.Contains("onCreated", functionNames);
		Assert.Contains("created", strings);
		Assert.Contains("triggeraction", strings);
		Assert.Contains("serverside", strings);
		Assert.Contains("weapon", strings);
		Assert.Contains("test", strings);
		Assert.Contains("hi", strings);
	}

	[Fact]
	public void Given_gs2_function_body_object_event_name_When_compiling_with_gs2_grammar_Then_gs1_event_flags_stay_zero()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  if (playerenters) {
		                          			    this.value = 1;
		                          			  }
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false, grammar: ScriptGrammar.GS2);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(0, ReadGs1EventFlags(result.ByteCode));
	}

	[Fact]
	public void Given_gs1_clientside_action_and_playerchats_script_When_compiling_with_gs1_grammar_Then_playerchats_event_flag_and_commands_are_emitted()
	{
		const string scriptText = """
		                          			//#CLIENTSIDE
		                          			if (actionclientside) {
		                          			  say2 hi;
		                          			  setplayerprop #c,received in weapon #p(0);
		                          			}

		                          			if (playerchats && strequals(#c,test2)) {
		                          			  triggeraction 0,0,serverside,#w,test,hi;
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false, grammar: ScriptGrammar.GS1);
		var strings = ReadStringTable(result.ByteCode);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(1 << 3, ReadGs1EventFlags(result.ByteCode));
		Assert.Contains("actionclientside", strings);
		Assert.Contains("say2", strings);
		Assert.Contains("hi", strings);
		Assert.DoesNotContain("setplayerprop", strings);
		Assert.DoesNotContain("#c", strings);
		Assert.Contains("chat", strings);
		Assert.Contains("received in weapon ", strings);
		Assert.Contains("test2", strings);
		Assert.Contains("triggeraction", strings);
		Assert.Contains("serverside", strings);
		Assert.Contains("weapon", strings);
		Assert.Contains("name", strings);
		Assert.DoesNotContain("#w", strings);
		Assert.Contains("test", strings);
	}

	[Fact]
	public void Given_script_with_continue_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  while (this.i < 3) {
		                          			    this.i++;
		                          			    continue;
		                          			  }
		                          			  for (temp.i = 0; temp.i < 3; temp.i++) {
		                          			    continue;
		                          			  }
		                          			  for (temp.pl: players) {
		                          			    continue;
		                          			  }
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_script_with_casts_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.i = int("3.8");
		                          			  temp.f = float("2.5");
		                          			  temp.t = _("Hello");
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_script_with_bitwise_invert_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.mask = ~temp.flags;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_script_with_shift_assign_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.flags <<= 2;
		                          			  temp.flags >>= 1;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_new_array_with_expression_dimension_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  this.tilemap = new[64*64];
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_temp_alias_with_string_cast_array_comparison_When_compiling_Then_bytecode_matches_official()
	{
		const string scriptText = """
		                          			//#CLIENTSIDE
		                          			function onCreated() {
		                          			  temp.var = {true,true};

		                          			  return @var == @{1,1};
		                          			}
		                          """;

		var       result   = Interface.CompileCode(scriptText, withHeader: false);
		var       path     = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs2-feature", "probes", "legacy-string-cast.official.json");
		using var official = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
		var       expected = official.RootElement.GetProperty("byteCodeBase64").GetBytesFromBase64();

		Assert.True(result.Success, result.ErrMsg);
		Assert.True(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(official.RootElement.GetProperty("byteCount").GetInt32(), expected.Length);
		Assert.Equal(expected, result.ByteCode);
	}

	[Fact]
	public void Given_script_with_power_assign_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.value ^= 3;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_script_with_bitwise_xor_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.mask = temp.flags xor 3;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_for_loop_without_init_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.i = 0;
		                          			  for (; temp.i < 3; temp.i++) {
		                          			    continue;
		                          			  }
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_lambda_with_single_statement_body_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.fn = function() temp.value = 1;;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_new_object_with_scoped_type_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.obj = new Gui::Control("Name");
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_scoped_object_function_When_compiling_Then_success_is_true_and_bytecode_is_not_empty()
	{
		const string scriptText = """
		                          			function Gui::Control.onAction() {
		                          			  return;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText);

		Assert.True(result.Success);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_universe_object_function_When_compiling_Then_function_table_matches_reference_name()
	{
		const string scriptText = """
		                          			function universe.onServerListerConnect() {
		                          			  return;
		                          			}
		                          """;

		var result        = Interface.CompileCode(scriptText, withHeader: false);
		var functionNames = ReadFunctionNames(result.ByteCode);

		Assert.True(result.Success);
		Assert.Equal("universe.onServerListerConnect", Assert.Single(functionNames));
	}

	[Fact]
	public void Given_math_builtin_calls_When_compiling_Then_native_builtins_are_inlined_and_pow_is_called()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.a = random(1, 2);
		                          			  temp.b = min(1, 2);
		                          			  temp.c = max(1, 2);
		                          			  temp.d = exp(2);
		                          			  temp.e = log(2, 10);
		                          			  temp.f = pow(2, 3);
		                          			  temp.g = getangle(1, 0);
		                          			  temp.h = getdir(1, 0);
		                          			  temp.i = char(65);
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)87, code);
		Assert.Contains((byte)91, code);
		Assert.Contains((byte)92, code);
		Assert.Contains((byte)93, code);
		Assert.Contains((byte)94, code);
		Assert.Contains((byte)95, code);
		Assert.Contains((byte)96, code);
		Assert.Contains((byte)103, code);
		Assert.Contains((byte)65, code);
		Assert.DoesNotContain("random", strings);
		Assert.DoesNotContain("min", strings);
		Assert.DoesNotContain("max", strings);
		Assert.DoesNotContain("exp", strings);
		Assert.DoesNotContain("log", strings);
		Assert.Contains("pow", strings);
		Assert.DoesNotContain("getangle", strings);
		Assert.DoesNotContain("getdir", strings);
		Assert.DoesNotContain("char", strings);
	}

	[Fact]
	public void Given_unary_multiply_expression_When_compiling_Then_negation_precedes_multiply()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.da = 1;
		                          			  temp.dy = -sin(temp.da) * 4;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false);
		var code   = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.True(Contains([33, 69, 20, 243, 4, 62], code));
	}

	[Fact]
	public void Given_duplicate_qualified_function_names_When_compiling_Then_both_entries_are_retained()
	{
		const string scriptText = """
		                          			function ChatBar.onAction() {
		                          			  temp.a = 1;
		                          			}

		                          			function ChatBar.onAction() {
		                          			  temp.b = 2;
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false);
		var code   = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.False(Contains([1, 243, 0], code));
		Assert.Equal(new[] { "ChatBar.onAction", "ChatBar.onAction" }, ReadFunctionNames(result.ByteCode));
	}

	[Fact]
	public void Given_new_block_with_call_When_compiling_Then_function_emits_cmd_call()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  new GuiWindowCtrl("Window") {
		                          			    clearrows();
		                          			  }
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false);
		var code   = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.True(Contains([10, 9], code));
	}

	[Fact]
	public void Given_makevar_call_When_compiling_Then_direct_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.obj = makevar("this.value");
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.DoesNotContain((byte)34, code);
		Assert.Contains((byte)41, code);
		Assert.DoesNotContain("makevar", strings);
	}

	[Fact]
	public void Given_format_call_When_compiling_Then_direct_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.text = format("Value: %d", 3);
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)84, code);
		Assert.DoesNotContain("format", strings);
	}

	[Fact]
	public void Given_arraylen_calls_When_compiling_Then_object_size_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.a = arraylen(players);
		                          			  temp.b = sarraylen(players);
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)130, code);
		Assert.DoesNotContain("arraylen", strings);
		Assert.DoesNotContain("sarraylen", strings);
	}

	[Fact]
	public void Given_setarray_call_When_compiling_Then_direct_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  setarray(this.key, 11);
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)39, code);
		Assert.DoesNotContain("setarray", strings);
	}

	[Fact]
	public void Given_sleep_call_When_compiling_Then_direct_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  sleep(0.05);
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)8, code);
		Assert.DoesNotContain("sleep", strings);
	}

	[Fact]
	public void Given_waitfor_call_When_compiling_Then_direct_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.done = waitfor(this, "Done", 1);
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)11, code);
		Assert.DoesNotContain("waitfor", strings);
	}

	[Fact]
	public void Given_size_method_call_When_compiling_Then_object_size_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.count = players.size();
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)130, code);
		Assert.DoesNotContain("size", strings);
	}

	[Fact]
	public void Given_object_query_method_calls_When_compiling_Then_direct_opcodes_are_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.posi = this.items.index("a");
		                          			  temp.kind = this.items.type();
		                          			  temp.tail = this.items.subarray(1);
		                          			  temp.keys = this.items.indices();
		                          			  temp.ref = this.items.link();
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)82, code);
		Assert.Contains((byte)83, code);
		Assert.Contains((byte)99, code);
		Assert.Contains((byte)100, code);
		Assert.Contains((byte)135, code);
		Assert.DoesNotContain("index", strings);
		Assert.DoesNotContain("type", strings);
		Assert.DoesNotContain("indices", strings);
		Assert.DoesNotContain("link", strings);
		Assert.DoesNotContain("subarray", strings);
	}

	[Fact]
	public void Given_index_method_on_array_index_When_compiling_Then_receiver_is_converted()
	{
		const string scriptText = """
		                          			function onActionClientSide(action, args) {
		                          			  temp.pos = args[0].index("-");
		                          			}
		                          """;

		var result = Interface.CompileCode(scriptText, withHeader: false);
		var code   = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.True(Contains([131, 36, 21, 240, 3, 82], code));
	}

	[Fact]
	public void Given_length_method_call_When_compiling_Then_object_length_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.count = player.account.length();
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)111, code);
		Assert.DoesNotContain("length", strings);
	}

	[Fact]
	public void Given_substring_method_call_When_compiling_Then_object_substring_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.prefix = player.account.substring(0, 3);
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)115, code);
		Assert.DoesNotContain("substring", strings);
	}

	[Fact]
	public void CompileCode_Given_substring_without_length_When_called_Then_emits_negative_one_length()
	{
		//Arrange
		const string scriptText = "function onCreated() { return \"abcdef\".substring(2); }";

		//Act
		var result = Interface.CompileCode(scriptText, withHeader: false);
		var code   = ReadBytecodeSegment(result.ByteCode);

		//Assert
		Assert.True(Contains([20, 0xF3, 0xFF, 115], code));
	}

	[Fact]
	public void Given_pos_method_call_When_compiling_Then_object_pos_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.index = player.account.pos("a");
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)112, code);
		Assert.DoesNotContain("pos", strings);
	}

	[Fact]
	public void Given_charat_and_positions_method_calls_When_compiling_Then_direct_opcodes_are_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.char = player.account.charat(0);
		                          			  temp.dots = player.account.positions(".");
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)114, code);
		Assert.Contains((byte)120, code);
		Assert.DoesNotContain("charat", strings);
		Assert.DoesNotContain("positions", strings);
	}

	[Fact]
	public void Given_starts_and_ends_method_calls_When_compiling_Then_direct_opcodes_are_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.a = player.account.starts("A");
		                          			  temp.b = player.account.ends("Z");
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)116, code);
		Assert.Contains((byte)117, code);
		Assert.DoesNotContain("starts", strings);
		Assert.DoesNotContain("ends", strings);
	}

	[Fact]
	public void Given_trim_method_call_When_compiling_Then_object_trim_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.name = " player ".trim();
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.DoesNotContain((byte)34, code);
		Assert.Contains((byte)110, code);
		Assert.DoesNotContain("trim", strings);
	}

	[Fact]
	public void Given_tokenize_method_call_When_compiling_Then_object_tokenize_opcode_is_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  temp.a = player.chat.tokenize();
		                          			  temp.b = player.chat.tokenize("|");
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)118, code);
		Assert.Contains(" ,", strings);
		Assert.DoesNotContain("tokenize", strings);
	}

	[Fact]
	public void Given_object_mutation_method_calls_When_compiling_Then_direct_opcodes_are_emitted()
	{
		const string scriptText = """
		                          			function onCreated() {
		                          			  this.items.clear();
		                          			  this.items.add("a");
		                          			  this.items.delete(0);
		                          			  this.items.insert(0, "b");
		                          			  this.items.remove("b");
		                          			  this.items.replace("a", "c");
		                          			}
		                          """;

		var result  = Interface.CompileCode(scriptText, withHeader: false);
		var strings = ReadStringTable(result.ByteCode);
		var code    = ReadBytecodeSegment(result.ByteCode);

		Assert.True(result.Success);
		Assert.Contains((byte)136, code);
		Assert.Contains((byte)137, code);
		Assert.Contains((byte)138, code);
		Assert.Contains((byte)139, code);
		Assert.Contains((byte)140, code);
		Assert.Contains((byte)141, code);
		Assert.DoesNotContain("clear", strings);
		Assert.DoesNotContain("add", strings);
		Assert.DoesNotContain("delete", strings);
		Assert.DoesNotContain("insert", strings);
		Assert.DoesNotContain("remove", strings);
		Assert.DoesNotContain("replace", strings);
	}

	private static List<string> ReadFunctionNames(byte[] bytecode)
	{
		var offset = 0;
		while (offset + 8 <= bytecode.Length)
		{
			var type   = ReadInt(bytecode, offset);
			var length = ReadInt(bytecode, offset + 4);
			offset += 8;
			if (type == 2) return ReadFunctionSegment(bytecode, offset, length);
			offset += length;
		}

		return [];
	}

	private static List<string> ReadStringTable(byte[] bytecode)
	{
		var          segment = ReadSegment(bytecode, 3);
		List<string> strings = [];
		var          offset  = 0;
		while (offset < segment.Length)
		{
			var start = offset;
			while (offset < segment.Length && segment[offset] != 0) offset++;
			strings.Add(Encoding.UTF8.GetString(segment, start, offset - start));
			offset++;
		}

		return strings;
	}

	private static byte[] ReadBytecodeSegment(byte[] bytecode) => ReadSegment(bytecode, 4);

	private static int ReadGs1EventFlags(byte[] bytecode)
	{
		var segment = ReadSegment(bytecode, 1);
		return segment.Length >= 4 ? ReadInt(segment, 0) : 0;
	}

	private static byte[] ReadSegment(byte[] bytecode, int segmentType)
	{
		var offset = 0;
		while (offset + 8 <= bytecode.Length)
		{
			var type   = ReadInt(bytecode, offset);
			var length = ReadInt(bytecode, offset + 4);
			offset += 8;
			if (type == segmentType) return bytecode[offset..(offset + length)];
			offset += length;
		}

		return [];
	}

	private static List<string> ReadFunctionSegment(byte[] bytecode, int offset, int length)
	{
		List<string> names = [];
		var          end   = offset + length;
		while (offset + 4 < end)
		{
			offset += 4;
			var start = offset;
			while (offset < end && bytecode[offset] != 0) offset++;
			names.Add(Encoding.UTF8.GetString(bytecode, start, offset - start));
			offset++;
		}

		return names;
	}

	private static int ReadInt(byte[] bytes, int offset) => bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];

	private static bool Contains(byte[] expected, byte[] actual)
	{
		for (var i = 0; i <= actual.Length - expected.Length; i++)
		{
			var matched = true;
			for (var j = 0; j < expected.Length; j++)
			{
				if (actual[i + j] == expected[j]) continue;
				matched = false;
				break;
			}

			if (matched) return true;
		}

		return false;
	}

	private static bool ContainsPattern(int[] expected, byte[] actual)
	{
		for (var i = 0; i <= actual.Length - expected.Length; i++)
		{
			var matched = true;
			for (var j = 0; j < expected.Length; j++)
			{
				if (expected[j] < 0 || actual[i + j] == expected[j]) continue;
				matched = false;
				break;
			}

			if (matched) return true;
		}

		return false;
	}
}