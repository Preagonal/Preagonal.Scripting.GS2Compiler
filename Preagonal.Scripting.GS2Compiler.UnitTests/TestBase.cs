namespace Preagonal.Scripting.GS2Compiler.UnitTests;

public class TestBase
{
	protected static string FindRepoRoot()
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Preagonal.Scripting.GS2Compiler.slnx")))
			dir = Directory.GetParent(dir)?.FullName;
		return dir ?? throw new DirectoryNotFoundException("Could not find repository root.");
	}
}