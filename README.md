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

The response also contains `JoinedClasses`, the unique, sorted class names from literal `join("class")` calls. Failed compilations return an empty collection.

The NativeAOT project publishes `gs2compiler` and its public `include/` headers. C++ callers can include `<compiler/GS2Context.h>` and use the reference compilation API used by gserver:

```cpp
GS2Context context;
auto response = context.compile("join(\"example\");");
if (response.success)
{
    const auto& classes = response.joinedClasses;
    const auto* bytes = response.bytecode.buffer();
    const auto size = response.bytecode.length();
}
```

These inline bindings call the C# native exports and copy the results into the reference C++ response types. Responses own their buffers and remain valid across further compilations and context destruction. The C++ binding exposes the headerless compilation path used by gserver.

For direct C ABI calls, `<GS2Compiler.h>` declares the exports (inside `gs2compiler_native` for C++). `JoinedClasses` is an array of UTF-8 string pointers with `JoinedClassesCount` entries. Release every returned response with `free_response(&response)`; this frees the error text, bytecode, and class strings and clears the response. Release contexts with `delete_context`.

The native integration regression can be built against a published library:

```sh
g++ -std=c++23 -Iinclude tests/native/compiler_api.cpp /path/to/gs2compiler.so -Wl,-rpath,/path/to -o /tmp/gs2compiler-api-test
/tmp/gs2compiler-api-test
```
