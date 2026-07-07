using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class BaselineTests
{
	public static IEnumerable<object[]> BasicScripts()
	{
		var root = FindRepoRoot();
		foreach (var category in new[] { "basic", "expressions", "functions", "classes", "edge_cases" })
		foreach (var script in Directory.EnumerateFiles(Path.Combine(root, "tests", "scripts", category), "*.gs2", SearchOption.TopDirectoryOnly))
			yield return [script];
		yield return [Path.Combine(root, "tests", "scripts", "statements", "01_conditionals.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "statements", "02_loops.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "statements", "03_switch.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "statements", "04_with.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "statements", "05_goto.gs2")];
	}

	public static IEnumerable<object[]> AdvancedScripts()
	{
		var root = FindRepoRoot();
		foreach (var script in Directory.EnumerateFiles(Path.Combine(root, "tests", "scripts", "advanced"), "*.gs2", SearchOption.AllDirectories))
			yield return [script];
	}

	public static IEnumerable<object[]> AdvancedParityScripts()
	{
		var root = FindRepoRoot();
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponRailroad Destroyer.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponShovel.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponMemeCentral.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponCatch Net.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponMining Bomb.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponViolin.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponBasket.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponCandies.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weapon-Day-Night.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponDiamond Axe.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponDiving.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponGold Axe.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponGold Hammer.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponMetal Hammer.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponParticleEditor.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponRoller.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponRope.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponSkip.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponSpin.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weapon-Systems_Main.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponTrading Item.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponWater Can.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponWooden Hammer.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponItems_Chemicals.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponKinetaro.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponKuJi_Items.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+DogTag.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-BuySys.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-NBuySys.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Presents.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Death.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-DRescript.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-EraThingyForKinetaro.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-FoodSys.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponFrankie_Test.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponItems_Body Armor.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponItems_Cure Virus.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponJobs_Stack.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponJobs_Roller.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponLevelGen.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Map.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Minimap.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Movement.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Movement2.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-NewWeather.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-NewGangSys.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-NewMovement.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Shake.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponSystems_ArchetypeEditor.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponSystems_Vote.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponMetal Axe.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponVulcan.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Gravity.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponSystems_B2.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponRWA_Plane.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponRC_AttributesWindow.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponRC_NCControl.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponSystems_TradeMenu.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponSystems_Free.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponSystems_IRC.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponTwinny_Tool.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponToys_CDPlayer.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Virus.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponWeapons_Grenade.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-Adventure.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-IRC_Login4.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-Rescripted_IRC_Login3.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-Rescripted_IRC_Login4.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-Rescripted_Serverlist.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-ServerListScreen.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-Serverlist_Patches.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-Staff_GUIExplorer.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-Staff_GraalShop.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weapon-StartConnectMessage.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "loginserver", "weaponTestScript.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponApple Seeds.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponBread Stack.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponTestGS2.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "g2k1", "weaponTorch.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon%045CarSystem.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon%045Movement.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-BizSystem.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponBrasas.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponBackups_Radio.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Ammo.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Food.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Guns.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Guns_KuJi.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Guns_M4.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Guns_Shotgun.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Hats.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Melee.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon+Seeds.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Weather.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Events.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponEvents_Lazer.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponFileEditor.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Functions.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Gang Control.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-GraalDB.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Gui.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Profile.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponRC_RightsWindow.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponStaff_Axe.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weapon-Job Quests.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponLevelEditor.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponNotepad.gs2")];
		yield return [Path.Combine(root, "tests", "scripts", "advanced", "graalx", "weaponSystems_FoodSys.gs2")];
	}

	[Theory]
	[MemberData(nameof(BasicScripts))]
	public void Given_basic_fixture_When_compiling_Then_bytecode_matches_baseline(string scriptPath)
	{
		var root = FindRepoRoot();
		var relative = Path.GetRelativePath(Path.Combine(root, "tests", "scripts"), scriptPath);
		var baselinePath = Path.ChangeExtension(Path.Combine(root, "tests", "baselines", relative), ".json");
		var baseline = JsonSerializer.Deserialize<BaselineData>(File.ReadAllText(baselinePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

		var result = Interface.CompileCode(File.ReadAllText(scriptPath), withHeader: false);
		var hash = Convert.ToHexString(SHA256.HashData(result.ByteCode)).ToLowerInvariant();

		Assert.Equal(baseline.CompilationSuccess, result.Success);
		Assert.Equal(baseline.BytecodeSize, result.ByteCode.Length);
		Assert.Equal(baseline.BytecodeHash, hash);
	}

	[Theory]
	[MemberData(nameof(AdvancedScripts))]
	public void Given_advanced_fixture_When_compiling_Then_it_succeeds(string scriptPath)
	{
		var result = Interface.CompileCode(File.ReadAllText(scriptPath), withHeader: false);

		Assert.True(result.Success, result.ErrMsg);
		Assert.NotEmpty(result.ByteCode);
	}

	[Theory]
	[MemberData(nameof(AdvancedParityScripts))]
	public void Given_advanced_parity_fixture_When_compiling_Then_bytecode_matches_baseline(string scriptPath)
	{
		var root = FindRepoRoot();
		var relative = Path.GetRelativePath(Path.Combine(root, "tests", "scripts"), scriptPath);
		var baselinePath = Path.ChangeExtension(Path.Combine(root, "tests", "baselines", relative), ".json");
		var baseline = JsonSerializer.Deserialize<BaselineData>(File.ReadAllText(baselinePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

		var result = Interface.CompileCode(File.ReadAllText(scriptPath), withHeader: false);
		var hash = Convert.ToHexString(SHA256.HashData(result.ByteCode)).ToLowerInvariant();

		Assert.Equal(baseline.CompilationSuccess, result.Success);
		Assert.Equal(baseline.BytecodeSize, result.ByteCode.Length);
		Assert.Equal(baseline.BytecodeHash, hash);
	}

	[Theory]
	[InlineData("01_syntax_errors.gs2")]
	[InlineData("02_semantic_errors.gs2")]
	[InlineData("03_string_escaping.gs2")]
	public void Given_error_fixture_When_compiling_Then_it_fails(string fileName)
	{
		var root = FindRepoRoot();
		var result = Interface.CompileCode(File.ReadAllText(Path.Combine(root, "tests", "scripts", "error_cases", fileName)), withHeader: false);

		Assert.False(result.Success);
		Assert.Empty(result.ByteCode);
	}

	public static string FindRepoRoot()
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Preagonal.Scripting.GS2Compiler.sln")))
			dir = Directory.GetParent(dir)?.FullName;
		return dir ?? throw new DirectoryNotFoundException("Could not find repository root.");
	}

	private sealed class BaselineData
	{
		[JsonPropertyName("bytecode_hash")]
		public string BytecodeHash { get; set; } = "";
		[JsonPropertyName("bytecode_size")]
		public int BytecodeSize { get; set; }
		[JsonPropertyName("compilation_success")]
		public bool CompilationSuccess { get; set; }
	}
}
