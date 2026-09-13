using System.Text.Json;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class Gs1WeaponParityTests
{
	[Theory]
	[InlineData("weapon%042Bomb")]
	[InlineData("weapon%042Bomb%032Bag")]
	[InlineData("weapon%042Bow")]
	[InlineData("weapon%042Bruges%032Bird")]
	[InlineData("weapon%042Clock-compatible")]
	[InlineData("weapon%042Crystal%032Ball")]
	[InlineData("weapon%042Fishbowl")]
	[InlineData("weapon%042Hammer")]
	[InlineData("weapon%042Mace")]
	[InlineData("weapon%042NPCWDisable")]
	[InlineData("weapon%042Pizza%032Stack")]
	[InlineData("weapon%042Quiver-compatible")]
	[InlineData("weapon%042Rat%032Form")]
	[InlineData("weapon%042Shovel")]
	[InlineData("weapon%042Staff%032Tool-compatible")]
	[InlineData("weapon%042Tools%047Bank%032Card")]
	[InlineData("weapon%042Torch")]
	[InlineData("weapon%042Warp%032Ring-compatible")]
	[InlineData("weapon%042bank%032card")]
	[InlineData("weapon%042note")]
	[InlineData("weapon%042pkave%045system1-compatible")]
	[InlineData("weapon%042pkave%045system2-compatible")]
	[InlineData("weapon%045Alien")]
	[InlineData("weapon%045BarrelRide-compatible")]
	[InlineData("weapon%045BombAndBowUpdater-compatible")]
	[InlineData("weapon%045DragonRide")]
	[InlineData("weapon%045GC%032Droppables1")]
	[InlineData("weapon%045GC%032Droppables2-compatible")]
	[InlineData("weapon%045Nations")]
	[InlineData("weapon%045Player%047Hats", true)]
	[InlineData("weapon%045RideToCC")]
	[InlineData("weapon%045System-compatible")]
	[InlineData("weapon%045System2")]
	[InlineData("weapon%045System3-compatible")]
	[InlineData("weapon%045Vampire")]
	[InlineData("weapon%045WhistleKiller")]
	[InlineData("weapon%045WongGuard")]
	[InlineData("weapon%045com013%032arena%032system%045")]
	[InlineData("weapon%045game%045Block%032Destroyer")]
	[InlineData("weapon%045game%045Bomb%032Dodger")]
	[InlineData("weapon%045game%045Falling%032Blocks")]
	[InlineData("weapon%045game%045Graal%032Invaders")]
	[InlineData("weapon%045game%045Graal%032Snake")]
	[InlineData("weapon%045gr%095movement")]
	[InlineData("weapon%045magic%045Attack")]
	[InlineData("weapon%045magic%045Defend")]
	[InlineData("weapon%045magic%045Effect-compatible")]
	[InlineData("weapon%045nimda%032boat%032race%032display%045")]
	[InlineData("weapon%045system%047sword")]
	[InlineData("weapon%045system4")]
	[InlineData("weapon300%032Club%032Card")]
	[InlineData("weaponAngel")]
	[InlineData("weaponAstral%032Projection")]
	[InlineData("weaponBetting%032System-compatible")]
	[InlineData("weaponBirdshot")]
	[InlineData("weaponBomb%032Stock")]
	[InlineData("weaponBounty")]
	[InlineData("weaponBowling%032Ball")]
	[InlineData("weaponCC%032Map")]
	[InlineData("weaponCall%032Spaceship-compatible")]
	[InlineData("weaponCane%032of%032Brugara")]
	[InlineData("weaponCape%032of%032the%032Hero")]
	[InlineData("weaponChain%032Thingy")]
	[InlineData("weaponCheese%032Burger-compatible")]
	[InlineData("weaponCheese")]
	[InlineData("weaponClaim%032Check")]
	[InlineData("weaponClairvoyance")]
	[InlineData("weaponClaw")]
	[InlineData("weaponCoin")]
	[InlineData("weaponConch%032Shell")]
	[InlineData("weaponDancing%032Shoes")]
	[InlineData("weaponDark%032Knife")]
	[InlineData("weaponDefensive%032Fire")]
	[InlineData("weaponDice")]
	[InlineData("weaponDoll")]
	[InlineData("weaponEntertainer")]
	[InlineData("weaponFake%032Dagger")]
	[InlineData("weaponFire%032Staff")]
	[InlineData("weaponFire%032Tablet-compatible")]
	[InlineData("weaponFishing%032Rod")]
	[InlineData("weaponGC%032Tool%0322")]
	[InlineData("weaponGC%032Tool%032v2")]
	[InlineData("weaponGC%032Uniform")]
	[InlineData("weaponGP%032Barricade")]
	[InlineData("weaponGP%032Flashbomb")]
	[InlineData("weaponGP%032Warpbomb")]
	[InlineData("weaponGaia%032Shot-compatible")]
	[InlineData("weaponGraal%032Map")]
	[InlineData("weaponGraal%032Radio")]
	[InlineData("weaponGrog")]
	[InlineData("weaponHamburger-compatible")]
	[InlineData("weaponHerb")]
	[InlineData("weaponHook%032Thingy")]
	[InlineData("weaponIce%032Cream-compatible")]
	[InlineData("weaponIce%032Tablet-compatible")]
	[InlineData("weaponJellybeans")]
	[InlineData("weaponJoltbomb")]
	[InlineData("weaponJump%032Rope")]
	[InlineData("weaponJumping%032Boots")]
	[InlineData("weaponKnife%032of%032Judgement")]
	[InlineData("weaponLeapfrog")]
	[InlineData("weaponLode%032Glove")]
	[InlineData("weaponMagic%032Smoke")]
	[InlineData("weaponMagic%032Vial")]
	[InlineData("weaponMeditation")]
	[InlineData("weaponMiniGameSystem")]
	[InlineData("weaponMoonBoots%033")]
	[InlineData("weaponMorpher")]
	[InlineData("weaponMusic%032Maker")]
	[InlineData("weaponNo%032NPC%032Weapons%032are%032allowed")]
	[InlineData("weaponNoise%032Maker")]
	[InlineData("weaponOff%045limits%032Grenade")]
	[InlineData("weaponOfficial%032Nimda%032Dragon%039s%032Map")]
	[InlineData("weaponOld%032Graal%032Warper")]
	[InlineData("weaponOpposite%032Boots")]
	[InlineData("weaponOut%032Of%032Bounds")]
	[InlineData("weaponPanda%032Beer")]
	[InlineData("weaponPanda%032Wine")]
	[InlineData("weaponPermaClothes%032TM")]
	[InlineData("weaponPestilence%032Staff")]
	[InlineData("weaponPoly%032Trail")]
	[InlineData("weaponPortable%032Chair")]
	[InlineData("weaponQuiver")]
	[InlineData("weaponSTARCON%032FireMine")]
	[InlineData("weaponShovel-compatible")]
	[InlineData("weaponSkull%032Storm")]
	[InlineData("weaponSkull%032Summon")]
	[InlineData("weaponSoft%032Drink-compatible")]
	[InlineData("weaponSpace%032Storm")]
	[InlineData("weaponSpace%032Toy")]
	[InlineData("weaponSpooon%032Warp")]
	[InlineData("weaponStaff%032Tool")]
	[InlineData("weaponStaff%047Boots")]
	[InlineData("weaponStone%032Morph")]
	[InlineData("weaponStone%032Tablet-compatible")]
	[InlineData("weaponSub-compatible")]
	[InlineData("weaponSuicide%032Dagger")]
	[InlineData("weaponSword%032Dance")]
	[InlineData("weaponSwords%032Only")]
	[InlineData("weaponTitanium")]
	[InlineData("weaponToggle%032Magic")]
	[InlineData("weaponTools%042Bank%032Card")]
	[InlineData("weaponTools%047%064Bank%032Card")]
	[InlineData("weaponTools%047Bank%032Card")]
	[InlineData("weaponTreasure%032Map")]
	[InlineData("weaponTriSword")]
	[InlineData("weaponTrident")]
	[InlineData("weaponVampire%032Bite")]
	[InlineData("weaponWar%032Staff")]
	[InlineData("weaponWuSeh%032Key")]
	[InlineData("weaponZorbi%032Key")]
	[InlineData("weaponZorbi%032Ring-compatible")]
	[InlineData("weaponflame%032spiral")]
	[InlineData("weaponflamespiral")]
	[InlineData("weaponkalzor%047swordsystem")]
	[InlineData("weaponkalzor%047swordsystem2")]
	[InlineData("weaponrupees")]
	[InlineData("weaponrupees2")]
	[InlineData("probes/zorbi-string-cache")]
	[InlineData("probes/extra-message-codes")]
	[InlineData("probes/indexed-message-codes")]
	[InlineData("probes/message-code-cache")]
	[InlineData("probes/level-cache")]
	[InlineData("probes/parenthesized-strings")]
	[InlineData("probes/space-math")]
	[InlineData("probes/space-numeric")]
	[InlineData("probes/music-playersays")]
	[InlineData("probes/music-betting")]
	[InlineData("probes/minigame-expressions")]
	[InlineData("probes/minigame-modulo")]
	[InlineData("probes/ice-tablet-empty-else")]
	[InlineData("probes/gaia-projectiles")]
	[InlineData("probes/fishing-startswith")]
	[InlineData("probes/burger-logical-values")]
	[InlineData("probes/bowling-assignments")]
	[InlineData("probes/bowling-assignment-chains")]
	[InlineData("probes/playersays-strings")]
	[InlineData("evidence/betting-playersays-quoted")]
	[InlineData("probes/astral-loops")]
	[InlineData("probes/nimda-timeout")]
	[InlineData("probes/nimda-timeout-values")]
	[InlineData("probes/magic-strings")]
	[InlineData("probes/block-body")]
	[InlineData("probes/block-braces")]
	[InlineData("probes/arena-event-aliases")]
	[InlineData("probes/arena-event-canonical")]
	[InlineData("probes/system3-keycode")]
	[InlineData("probes/system2-properties")]
	[InlineData("probes/system-negated-chains")]
	[InlineData("probes/ride-properties")]
	[InlineData("probes/nations-messages")]
	[InlineData("probes/nations-builtins")]
	[InlineData("probes/nations-numeric")]
	[InlineData("probes/nations-argument-order")]
	[InlineData("probes/bomb-translations")]
	[InlineData("probes/bow-translations-and-cache")]
	[InlineData("probes/bow-expressions")]
	[InlineData("probes/final-string-commas")]
	[InlineData("probes/string-lookups")]
	[InlineData("probes/braced-setstring")]
	[InlineData("probes/tiledef-prototypes")]
	[InlineData("probes/message-spaced")]
	[InlineData("probes/action-and-chat")]
	[InlineData("probes/bomb-bag-syntax")]
	[InlineData("probes/bomb-bag-strings")]
	[InlineData("probes/bomb-bag-values")]
	[InlineData("probes/bruges-values")]
	[InlineData("probes/bruges-cache")]
	[InlineData("probes/bruges-boundaries")]
	[InlineData("probes/bruges-precision")]
	[InlineData("probes/bruges-thresholds")]
	[InlineData("probes/clock-arrays")]
	[InlineData("probes/clock-ranges")]
	[InlineData("probes/clock-empty")]
	[InlineData("probes/clock-equals")]
	[InlineData("probes/clock-events")]
	[InlineData("probes/crystal-strings")]
	[InlineData("probes/crystal-arguments")]
	[InlineData("probes/fishbowl-image")]
	[InlineData("probes/fishbowl-cache")]
	[InlineData("probes/hammer-negation")]
	[InlineData("probes/hammer-for-conditions")]
	[InlineData("probes/mace-sprite")]
	[InlineData("probes/npcwdisable-spacing")]
	[InlineData("probes/quiver-strings")]
	[InlineData("probes/quiver-multiple-range")]
	[InlineData("probes/rat-properties")]
	[InlineData("probes/staff-properties")]
	[InlineData("probes/staff-equals")]
	[InlineData("probes/staff-contexts")]
	[InlineData("probes/pkave-trim")]
	[InlineData("probes/alien-glove")]
	[InlineData("probes/droppables-replace")]
	[InlineData("probes/droppables-image")]
	[InlineData("probes/droppables-indirection")]
	[InlineData("probes/call-cache-below-threshold")]
	[InlineData("probes/call-cache-at-threshold")]
	[InlineData("probes/logical-branches")]
	[InlineData("probes/function-order")]
	[InlineData("probes/function-table-growth")]
	public void Given_official_accepted_weapon_When_compiling_with_gs1_grammar_Then_bytecode_matches_official(string name, bool clientsideOnly = false)
	{
		var directory                                                                                                                = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Preagonal.Scripting.GS2Compiler.sln"))) directory = directory.Parent;
		Assert.NotNull(directory);
		var       path     = Path.Combine(directory.FullName, "tests", "gs1-weapons", name);
		using var official = JsonDocument.Parse(File.ReadAllText(path + ".official.json"));
		var       expected = official.RootElement.GetProperty("byteCodeBase64").GetBytesFromBase64();
		Assert.True(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(official.RootElement.GetProperty("byteCount").GetInt32(), expected.Length);

		var source = File.ReadAllText(path + ".gs1");
		if (clientsideOnly)
		{
			var marker = source.IndexOf("//#CLIENTSIDE", StringComparison.Ordinal);
			Assert.True(marker >= 0);
			source = source[marker..];
		}

		var result = Interface.CompileCode(source, type: "weapon", name: "-tmpweapon", withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(expected, result.ByteCode);
	}

	[Fact]
	public void Given_gaia_shot_unmatched_brace_When_compiling_as_gs1_Then_no_partial_bytecode_is_emitted()
	{
		var       path     = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons", "evidence", "weaponGaia%032Shot");
		using var official = JsonDocument.Parse(File.ReadAllText(path + ".official.json"));
		Assert.False(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, official.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Equal("Official compiler error for weapon '-tmpweapon': error: unexpected token: } at line 6: }", official.RootElement.GetProperty("diagnostic").GetString());
		var result = Interface.CompileCode(File.ReadAllText(path + ".gs1"), withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.False(result.Success);
		Assert.Empty(result.ByteCode);
		Assert.Contains("malformed input at line 6: }", result.ErrMsg);
		var original   = File.ReadAllText(path + ".gs1");
		var compatible = File.ReadAllText(Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons", "weaponGaia%032Shot-compatible.gs1"));
		Assert.Equal(original.Replace("}\n}\nif (weaponfired)", "}\nif (weaponfired)", StringComparison.Ordinal), compatible);
	}

	[Fact]
	public void Given_truncated_gc_tool_When_compiling_as_gs1_Then_no_partial_bytecode_is_emitted()
	{
		var       path     = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons", "evidence", "weaponGC%032Tool");
		using var official = JsonDocument.Parse(File.ReadAllText(path + ".official.json"));
		Assert.False(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, official.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Equal("Official compiler error for weapon '-tmpweapon': error: unexpected token:  at line 388", official.RootElement.GetProperty("diagnostic").GetString());
		var source = File.ReadAllText(path + ".gs1");
		Assert.EndsWith("setstring local.gc_img,#t(", source.TrimEnd());
		var result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.False(result.Success);
		Assert.Empty(result.ByteCode);
		Assert.Contains("malformed input at line 387:", result.ErrMsg);
	}

	[Theory]
	[InlineData("weaponFire%032Tablet", "missing semicolon at line 50: setbackpal pics1.png;")]
	[InlineData("weaponIce%032Tablet", "missing semicolon at line 22: setbackpal dusk2.png;")]
	[InlineData("weaponShovel", "missing semicolon at line 18: setbackpal pics1.png;")]
	[InlineData("weaponStone%032Tablet", "missing semicolon at line 21: setbackpal dusk2.png;")]
	[InlineData("weaponZorbi%032Ring", "missing semicolon at line 124: setbackpal pics1.png;")]
	public void Given_palette_compatible_variant_When_compiled_Then_only_removed_palette_calls_are_omitted_and_original_support_remains(string name, string diagnostic)
	{
		var directory       = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons");
		var source          = File.ReadAllText(Path.Combine(directory, "evidence", name + ".gs1"));
		var compatible      = File.ReadAllText(Path.Combine(directory, name + "-compatible.gs1"));
		var withoutPalettes = source;
		foreach (var image in new[]
		         {
			         "pics1.png",
			         "flarepal5.png",
			         "flarepal4.png",
			         "flarepal3.png",
			         "flarepal2.png",
			         "flarepal1.png",
			         "dusk2.png",
		         })
			withoutPalettes = withoutPalettes.Replace("setbackpal " + image + ";", ";", StringComparison.Ordinal);
		Assert.Equal(withoutPalettes, compatible);
		using var official = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "evidence", name + ".official.json")));
		Assert.False(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, official.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Contains(diagnostic, official.RootElement.GetProperty("diagnostic").GetString());
		var originalResult   = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS1);
		var compatibleResult = Interface.CompileCode(compatible, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.True(originalResult.Success, originalResult.ErrMsg);
		Assert.True(compatibleResult.Success, compatibleResult.ErrMsg);
		Assert.NotEqual(originalResult.ByteCode, compatibleResult.ByteCode);
	}

	[Theory]
	[InlineData("weaponCheese%032Burger", "Cheeseburger", "Cheeseburger", 15, 5)]
	[InlineData("weaponHamburger", "Hamburger", "Hamburger", 15, 5)]
	[InlineData("weaponIce%032Cream", "Ice Cream", "Ice", 15, 5)]
	[InlineData("weaponSoft%032Drink", "Soft Drink", "Soft", 21, 5)]
	[InlineData("weaponSub", "Sub", "Sub", 25, 10)]
	public void Given_food_compatible_variant_When_compiled_Then_only_required_syntax_changes_and_original_output_is_preserved(string name, string item, string unexpectedSymbol, int semicolonLine, int argumentLine)
	{
		var directory  = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons");
		var original   = File.ReadAllText(Path.Combine(directory, "evidence", name + ".gs1"));
		var quoted     = File.ReadAllText(Path.Combine(directory, "evidence", name + "-quoted.gs1"));
		var compatible = File.ReadAllText(Path.Combine(directory, name + "-compatible.gs1"));
		Assert.Equal(original.Replace("playersays(Buy " + item + ")", "playersays(\"Buy " + item + "\")", StringComparison.Ordinal), quoted);
		Assert.Equal(quoted.Replace("playersprite = 3}", "playersprite = 3;}", StringComparison.Ordinal), compatible);
		using var originalOfficial = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "evidence", name + ".official.json")));
		using var quotedOfficial   = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "evidence", name + "-quoted.official.json")));
		Assert.False(originalOfficial.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, originalOfficial.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Contains("unexpected symbol " + unexpectedSymbol + " at line " + argumentLine, originalOfficial.RootElement.GetProperty("diagnostic").GetString());
		Assert.False(quotedOfficial.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, quotedOfficial.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Contains("missing semicolon at line " + semicolonLine, quotedOfficial.RootElement.GetProperty("diagnostic").GetString());
		var originalResult   = Interface.CompileCode(original, withHeader: false, grammar: ScriptGrammar.GS1);
		var compatibleResult = Interface.CompileCode(compatible, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.True(originalResult.Success, originalResult.ErrMsg);
		Assert.True(compatibleResult.Success, compatibleResult.ErrMsg);
		Assert.Equal(originalResult.ByteCode, compatibleResult.ByteCode);
	}

	[Fact]
	public void Given_betting_system_compatible_variant_When_compiled_Then_phrases_stay_supported_and_quotes_distinguish_variables()
	{
		var directory  = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons");
		var semicolons = File.ReadAllText(Path.Combine(directory, "evidence", "weaponBetting%032System-semicolons.gs1"));
		var compatible = File.ReadAllText(Path.Combine(directory, "weaponBetting%032System-compatible.gs1"));
		var quoted     = semicolons;
		foreach (var argument in new[]
		         {
			         "place bet",
			         "who is player1",
			         "who is player2",
			         "player1",
			         "player2",
			         "ap",
			         "gelats",
			         "arrows",
			         "bombs",
		         })
			quoted = quoted.Replace("playersays(" + argument + ")", "playersays(\"" + argument + "\")", StringComparison.Ordinal);
		Assert.Equal(quoted, compatible);
		Assert.Equal(18, compatible.Length - semicolons.Length);
		var originalSource = File.ReadAllText(Path.Combine(directory, "evidence", "weaponBetting%032System.gs1"));
		var original       = Interface.CompileCode(originalSource, withHeader: false, grammar: ScriptGrammar.GS1);
		var result         = Interface.CompileCode(compatible, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.True(original.Success, original.ErrMsg);
		Assert.True(result.Success, result.ErrMsg);
		Assert.NotEqual(original.ByteCode, result.ByteCode);
		var quotedPhrases = originalSource;
		foreach (var argument in new[] { "place bet", "who is player1", "who is player2" })
			quotedPhrases = quotedPhrases.Replace("playersays(" + argument + ")", "playersays(\"" + argument + "\")", StringComparison.Ordinal);
		var phrases = Interface.CompileCode(quotedPhrases, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.True(phrases.Success, phrases.ErrMsg);
		Assert.Equal(original.ByteCode, phrases.ByteCode);
	}

	[Fact]
	public void Given_betting_system_semicolon_variant_When_compiled_Then_only_terminators_change_and_local_bytecode_is_preserved()
	{
		var directory      = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons", "evidence");
		var originalSource = File.ReadAllText(Path.Combine(directory, "weaponBetting%032System.gs1"));
		var source         = File.ReadAllText(Path.Combine(directory, "weaponBetting%032System-semicolons.gs1"));
		var terminated = originalSource.Replace("this.mode = 0}", "this.mode = 0;}", StringComparison.Ordinal)
		                               .Replace("unset betwithgelats}", "unset betwithgelats;}", StringComparison.Ordinal)
		                               .Replace("unset betwithbombs}", "unset betwithbombs;}", StringComparison.Ordinal)
		                               .Replace("unset betwitharrows}", "unset betwitharrows;}", StringComparison.Ordinal);
		Assert.Equal(terminated, source);
		Assert.Equal(12, source.Length - originalSource.Length);
		var original = Interface.CompileCode(originalSource, withHeader: false, grammar: ScriptGrammar.GS1);
		var variant  = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.True(original.Success, original.ErrMsg);
		Assert.True(variant.Success, variant.ErrMsg);
		Assert.Equal(original.ByteCode, variant.ByteCode);
		using var official = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "weaponBetting%032System-semicolons.official.json")));
		Assert.False(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, official.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Contains("unexpected symbol bet at line 3", official.RootElement.GetProperty("diagnostic").GetString());
	}

	[Fact]
	public void Given_original_betting_system_When_compiling_Then_omitted_final_semicolons_remain_supported()
	{
		var       path     = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons", "evidence", "weaponBetting%032System");
		using var official = JsonDocument.Parse(File.ReadAllText(path + ".official.json"));
		Assert.False(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, official.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Contains("missing semicolon at line 2", official.RootElement.GetProperty("diagnostic").GetString());
		var result = Interface.CompileCode(File.ReadAllText(path + ".gs1"), withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.True(result.Success, result.ErrMsg);
		Assert.NotEmpty(result.ByteCode);
	}

	[Fact]
	public void Given_gs1_for_update_semicolons_When_omitted_Then_bytecode_is_unchanged()
	{
		var path              = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons", "probes", "astral-loops.gs1");
		var source            = File.ReadAllText(path);
		var withSemicolons    = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS1);
		var withoutSemicolons = Interface.CompileCode(source.Replace(";)", ")", StringComparison.Ordinal), withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.True(withSemicolons.Success, withSemicolons.ErrMsg);
		Assert.True(withoutSemicolons.Success, withoutSemicolons.ErrMsg);
		Assert.Equal(withSemicolons.ByteCode, withoutSemicolons.ByteCode);
	}

	[Fact]
	public void Given_gs1_empty_for_update_with_extra_semicolon_When_compiled_Then_both_compilers_reject_it()
	{
		var       path     = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons", "evidence", "astral-empty-update");
		using var official = JsonDocument.Parse(File.ReadAllText(path + ".official.json"));
		Assert.False(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, official.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Contains("unexpected token: ; at line 6", official.RootElement.GetProperty("diagnostic").GetString());
		var result = Interface.CompileCode(File.ReadAllText(path + ".gs1"), withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.False(result.Success);
		Assert.Empty(result.ByteCode);
	}

	[Fact]
	public void Given_magic_effect_reserved_function_name_When_corrected_Then_only_that_name_is_changed()
	{
		var path       = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons");
		var original   = File.ReadAllText(Path.Combine(path, "evidence", "weapon%045magic%045Effect.gs1"));
		var compatible = File.ReadAllText(Path.Combine(path, "weapon%045magic%045Effect-compatible.gs1"));
		Assert.Equal(original.Replace("break()", "effectbreak()", StringComparison.Ordinal), compatible);
		using var official = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "evidence", "weapon%045magic%045Effect.official.json")));
		Assert.False(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, official.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Contains("unexpected token: ( at line 135: break();", official.RootElement.GetProperty("diagnostic").GetString());
		var result = Interface.CompileCode(original, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.False(result.Success);
		Assert.Empty(result.ByteCode);
		Assert.Contains("line 135:", result.ErrMsg);
	}

	[Fact]
	public void Given_csharp_gr_corpus_entry_When_compiling_as_gs1_Then_no_partial_bytecode_is_emitted()
	{
		var       path     = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons", "evidence", "weapon%045gr");
		using var official = JsonDocument.Parse(File.ReadAllText(path + ".official.json"));
		Assert.False(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(0, official.RootElement.GetProperty("byteCount").GetInt32());
		Assert.Contains("unexpected token: void at line 3", official.RootElement.GetProperty("diagnostic").GetString());
		var source = File.ReadAllText(path + ".gs1");
		Assert.Contains("public void onCreated()", source);
		var result = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.False(result.Success);
		Assert.NotEmpty(result.ErrMsg!);
		Assert.Empty(result.ByteCode);
	}

	[Fact]
	public void Given_system3_unmatched_final_brace_When_corrected_Then_only_that_brace_is_removed()
	{
		var path       = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons");
		var original   = File.ReadAllText(Path.Combine(path, "evidence", "weapon%045System3.gs1"));
		var compatible = File.ReadAllText(Path.Combine(path, "weapon%045System3-compatible.gs1"));
		Assert.Equal(compatible + "}\n", original);
		var result = Interface.CompileCode(original, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.False(result.Success);
		Assert.Empty(result.ByteCode);
		Assert.Contains("line 251: }", result.ErrMsg);
	}

	[Fact]
	public void Given_droppables2_invalid_coordinate_When_corrected_Then_only_the_stray_backslash_changes()
	{
		var path       = Path.Combine(BaselineTests.FindRepoRoot(), "tests", "gs1-weapons");
		var original   = File.ReadAllText(Path.Combine(path, "evidence", "weapon%045GC%032Droppables2.gs1"));
		var compatible = File.ReadAllText(Path.Combine(path, "weapon%045GC%032Droppables2-compatible.gs1"));
		Assert.Contains("boxy = {1\\0,10,10,16,16,16,22,22,22};", original);
		Assert.Equal(compatible, original.Replace("boxy = {1\\0,10,10,16,16,16,22,22,22};", "boxy = {10,10,10,16,16,16,22,22,22};"));
		var result = Interface.CompileCode(original, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.False(result.Success);
		Assert.Empty(result.ByteCode);
	}

	[Fact]
	public void Given_barrel_ride_joined_commands_When_spacing_is_normalized_Then_local_bytecode_is_unchanged()
	{
		var root   = BaselineTests.FindRepoRoot();
		var path   = Path.Combine(root, "tests", "gs1-weapons");
		var source = File.ReadAllText(Path.Combine(path, "evidence", "weapon%045BarrelRide-unspaced.gs1"));
		var spaced = File.ReadAllText(Path.Combine(path, "weapon%045BarrelRide-compatible.gs1"));
		Assert.Equal(spaced, source.Replace("freezeplayer.05;", "freezeplayer .05;").Replace("setlevel2b39_watertunnel2.nw", "setlevel2 b39_watertunnel2.nw"));
		var joinedResult = Interface.CompileCode(source, withHeader: false, grammar: ScriptGrammar.GS1);
		var spacedResult = Interface.CompileCode(spaced, withHeader: false, grammar: ScriptGrammar.GS1);
		Assert.True(joinedResult.Success, joinedResult.ErrMsg);
		Assert.True(spacedResult.Success, spacedResult.ErrMsg);
		Assert.Equal(spacedResult.ByteCode, joinedResult.ByteCode);
	}

	[Fact]
	public void Given_quiver_with_normalized_terminators_When_compiling_Then_multi_value_range_matches_the_array_form()
	{
		var directory                                                                                                                = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Preagonal.Scripting.GS2Compiler.sln"))) directory = directory.Parent;
		Assert.NotNull(directory);
		var source = File.ReadAllText(Path.Combine(directory.FullName, "tests", "gs1-weapons", "evidence", "weapon%042Quiver.gs1"))
		                 .Replace("this.firstout=this.numout-4;;", "this.firstout=this.numout-4;")
		                 .Replace("this.firstin=this.numin-4;;", "this.firstin=this.numin-4;");
		var arraySource = source.Replace("mapdx,mapdy in |-1,1|", "{mapdx,mapdy} in |-1,1|");
		Assert.NotEqual(source, arraySource);

		var actual   = Interface.CompileCode(source, type: "weapon", withHeader: false, grammar: ScriptGrammar.GS1);
		var expected = Interface.CompileCode(arraySource, type: "weapon", withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(actual.Success, actual.ErrMsg);
		Assert.True(expected.Success, expected.ErrMsg);
		Assert.Equal(expected.ByteCode, actual.ByteCode);
	}

	[Theory]
	[InlineData("weapon%042Warp%032Ring")]
	[InlineData("weapon%042pkave%045system1")]
	[InlineData("weapon%042pkave%045system2")]
	[InlineData("weapon%045BombAndBowUpdater")]
	[InlineData("weapon%045System")]
	[InlineData("weaponCall%032Spaceship")]
	public void Given_original_weapon_When_compiling_Then_legacy_palette_command_remains_supported(string name)
	{
		var directory                                                                                                                = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Preagonal.Scripting.GS2Compiler.sln"))) directory = directory.Parent;
		Assert.NotNull(directory);
		var path             = Path.Combine(directory.FullName, "tests", "gs1-weapons");
		var source           = File.ReadAllText(Path.Combine(path, "evidence", name + ".gs1"));
		var compatibleSource = File.ReadAllText(Path.Combine(path, name + "-compatible.gs1"));
		Assert.NotEqual(source, compatibleSource);
		Assert.Equal(compatibleSource, source.Replace("setbackpal pics1.png;", ";"));

		var original   = Interface.CompileCode(source, type: "weapon", withHeader: false, grammar: ScriptGrammar.GS1);
		var compatible = Interface.CompileCode(compatibleSource, type: "weapon", withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(original.Success, original.ErrMsg);
		Assert.True(compatible.Success, compatible.ErrMsg);
		Assert.NotEqual(compatible.ByteCode, original.ByteCode);
	}

	[Fact]
	public void Given_original_clock_weapon_When_compiling_Then_legacy_commands_and_omitted_semicolons_remain_supported()
	{
		var directory                                                                                                                = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Preagonal.Scripting.GS2Compiler.sln"))) directory = directory.Parent;
		Assert.NotNull(directory);
		var path       = Path.Combine(directory.FullName, "tests", "gs1-weapons");
		var source     = File.ReadAllText(Path.Combine(path, "evidence", "weapon%042Clock.gs1"));
		var terminated = source.Replace("{this.x[this.index]=62}", "{this.x[this.index]=62;}").Replace("{this.x[this.index]=1}", "{this.x[this.index]=1;}").Replace("{this.y[this.index]=1}", "{this.y[this.index]=1;}");
		Assert.NotEqual(source, terminated);

		var original   = Interface.CompileCode(source, type: "weapon", withHeader: false, grammar: ScriptGrammar.GS1);
		var normalized = Interface.CompileCode(terminated, type: "weapon", withHeader: false, grammar: ScriptGrammar.GS1);
		var compatible = Interface.CompileCode(File.ReadAllText(Path.Combine(path, "weapon%042Clock-compatible.gs1")), type: "weapon", withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(original.Success, original.ErrMsg);
		Assert.True(normalized.Success, normalized.ErrMsg);
		Assert.True(compatible.Success, compatible.ErrMsg);
		Assert.Equal(normalized.ByteCode, original.ByteCode);
		Assert.NotEqual(compatible.ByteCode, original.ByteCode);
	}
}