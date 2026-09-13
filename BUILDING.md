# Building GS2Compiler

## Tests

```powershell
dotnet test '.\Preagonal.Scripting.GS2Compiler.UnitTests\Preagonal.Scripting.GS2Compiler.UnitTests.csproj'
```

## CI Coverage

Jenkins runs the public unit tests once using NuGet GSAst dependencies and the
Coverlet VSTest collector:

```sh
dotnet test Preagonal.Scripting.GS2Compiler.UnitTests/Preagonal.Scripting.GS2Compiler.UnitTests.csproj /p:UseLocalGSAst=false --results-directory Testing --logger "trx;LogFileName=unit_tests.xml" --collect:"XPlat Code Coverage;Format=opencover"
```

The TRX report is `Testing/unit_tests.xml`; OpenCover reports are generated under
`Testing/<run-id>/coverage.opencover.xml`. Jenkins clears its results directory
before running, archives both reports, and blocks package publishing on test or
coverage failure, including a missing or empty coverage report.

Do not enable MSBuild coverage alongside the collector. The collector avoids the
[documented test-host shutdown race](https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/KnownIssues.md#vstest-stops-process-execution-early)
that can truncate MSBuild coverage hit files after tests pass. No coverage filters
or compiler tests are disabled by this change. Private GS1 corpus tests restored
locally are not present in a public checkout, so local test counts can differ.

## Windows CLI

```powershell
dotnet publish '.\Preagonal.Scripting.GS2Compiler.Cli\Preagonal.Scripting.GS2Compiler.Cli.csproj' -c Release -r win-x64 --self-contained false -o '.\artifacts\publish\cli-win-x64'
```

## Windows Native AOT DLL

```powershell
dotnet publish '.\Preagonal.Scripting.GS2Compiler.Native\Preagonal.Scripting.GS2Compiler.Native.csproj' -c Release -r win-x64 -o '.\artifacts\publish\native-win-x64'
```

## Browser WASM

```powershell
dotnet workload install wasm-tools wasm-tools-net8 wasm-tools-net9
dotnet publish '.\Preagonal.Scripting.GS2Compiler.Cli\Preagonal.Scripting.GS2Compiler.Cli.csproj' -c Release -r browser-wasm -o '.\artifacts\publish\cli-browser-wasm'
```

## WASI

```powershell
dotnet workload install wasi-experimental
dotnet publish '.\Preagonal.Scripting.GS2Compiler.Cli\Preagonal.Scripting.GS2Compiler.Cli.csproj' -c Release -r wasi-wasm -o '.\artifacts\publish\cli-wasi-wasm' /p:WASI_SDK_PATH='.\artifacts\tools\wasi-sdk-25.0-x86_64-windows\'
```

The .NET 10 WASI SDK target requires wasi-sdk `25.0`; the path must include the trailing slash.
