using System.Text.Json;

namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class Gs1WeaponParityTests
{
	[Theory]
	[InlineData("weapon%042Bomb")]
	[InlineData("probes/bomb-translations")]
	[InlineData("probes/call-cache-below-threshold")]
	[InlineData("probes/call-cache-at-threshold")]
	[InlineData("probes/logical-branches")]
	[InlineData("probes/function-order")]
	[InlineData("probes/function-table-growth")]
	public void Given_official_accepted_weapon_When_compiling_with_gs1_grammar_Then_bytecode_matches_official(string name)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Preagonal.Scripting.GS2Compiler.sln"))) directory = directory.Parent;
		Assert.NotNull(directory);
		var path = Path.Combine(directory.FullName, "tests", "gs1-weapons", name);
		using var official = JsonDocument.Parse(File.ReadAllText(path + ".official.json"));
		var expected = official.RootElement.GetProperty("byteCodeBase64").GetBytesFromBase64();
		Assert.True(official.RootElement.GetProperty("success").GetBoolean());
		Assert.Equal(official.RootElement.GetProperty("byteCount").GetInt32(), expected.Length);

		var result = Interface.CompileCode(File.ReadAllText(path + ".gs1"), type: "weapon", name: "-tmpweapon", withHeader: false, grammar: ScriptGrammar.GS1);

		Assert.True(result.Success, result.ErrMsg);
		Assert.Equal(expected, result.ByteCode);
	}
}
