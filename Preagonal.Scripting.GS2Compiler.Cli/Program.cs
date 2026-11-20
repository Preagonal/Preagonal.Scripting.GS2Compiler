using System;
using System.Collections.Generic;
using System.IO;
using Preagonal.Scripting.GS2Compiler;

internal static class Program
{
	private static int Main(string[] args)
	{
		var parsed = Arguments.Parse(args);
		if (parsed.Help)
		{
			Console.Write(Arguments.HelpText(AppDomain.CurrentDomain.FriendlyName));
			return 0;
		}

		if (parsed.Error.Length > 0)
		{
			Console.Error.WriteLine($"Error: {parsed.Error}");
			Console.Error.WriteLine("Use --help for usage information.");
			return 1;
		}

		var errors = 0;
		foreach (var input in parsed.Inputs)
			if (!Compile(input, parsed.Output, parsed.Verbose))
				errors++;
		return errors > 0 ? 1 : 0;
	}

	private static bool Compile(string input, string output, bool verbose)
	{
		if (!File.Exists(input))
		{
			Console.WriteLine(" -> [ERROR] File does not exist");
			return false;
		}

		if (verbose) Console.WriteLine($"Compiling file {input}");
		var response = Interface.CompileCode(File.ReadAllText(input), withHeader: false);
		if (!response.Success)
		{
			Console.WriteLine($" -> [ERROR] {response.ErrMsg}");
			return false;
		}

		var outPath = output.Length > 0 ? output : Path.ChangeExtension(input, ".gs2bc");
		var outDir  = Path.GetDirectoryName(outPath);
		if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
		File.WriteAllBytes(outPath, response.ByteCode);
		if (verbose) Console.WriteLine($" -> saved to {outPath}");
		else Console.WriteLine("Compilation successful");
		return true;
	}

	private sealed class Arguments
	{
		public List<string> Inputs  { get; }              = [];
		public string       Output  { get; private set; } = "";
		public bool         Verbose { get; private set; }
		public bool         Help    { get; private set; }
		public string       Error   { get; private set; } = "";

		public static Arguments Parse(string[] args)
		{
			Arguments parsed = new();
			if (args.Length == 0)
			{
				parsed.Error = "No input file specified.";
				return parsed;
			}

			for (var i = 0; i < args.Length; i++)
			{
				var arg = args[i];
				if (arg is "-h" or "--help")
				{
					parsed.Help = true;
					return parsed;
				}

				if (arg is "-v" or "--verbose")
				{
					parsed.Verbose = true;
					continue;
				}

				if (arg is "-o" or "--output")
				{
					if (++i >= args.Length)
					{
						parsed.Error = "Missing output file after " + arg;
						return parsed;
					}

					parsed.Output = args[i];
					continue;
				}

				if (arg.StartsWith("-", StringComparison.Ordinal))
				{
					parsed.Error = "Unknown option: " + arg;
					return parsed;
				}

				parsed.Inputs.Add(arg);
			}

			if (parsed.Inputs.Count == 2 && parsed.Output.Length == 0)
			{
				parsed.Output = parsed.Inputs[1];
				parsed.Inputs.RemoveAt(1);
			}

			if (parsed.Inputs.Count == 0) parsed.Error                            = "No input file specified";
			if (parsed.Inputs.Count > 1 && parsed.Output.Length > 0) parsed.Error = "Output file cannot be specified when processing multiple files";
			return parsed;
		}

		public static string HelpText(string program) =>
			$"""
			 GS2 Script Compiler

			 Usage:
			   {program} [OPTIONS] INPUT [OUTPUT]
			   {program} INPUT -o OUTPUT
			   {program} --help
			 """;
	}
}