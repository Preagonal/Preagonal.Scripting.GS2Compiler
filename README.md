# Preagonal.Scripting.GS2Compiler

C# GS2 compiler package for Preagonal scripting projects.

```csharp
using Preagonal.Scripting.GS2Compiler;

CompilerResponse response = Interface.CompileCode("function onCreated() { echo(\"hi\"); }");

if (response.Success)
{
	byte[] bytecode = response.ByteCode;
}
```

The public API is centered on `Interface.CompileCode(string? code, string? type = "weapon", string? name = "npc", bool withHeader = true, ScriptGrammar grammar = ScriptGrammar.GS2)` and returns `CompilerResponse`.
