FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG VER
WORKDIR /src
COPY ["NuGet.Config", "NuGet.Config"]
COPY ["Preagonal.Scripting.GS2Compiler/Preagonal.Scripting.GS2Compiler.csproj", "Preagonal.Scripting.GS2Compiler/"]
COPY ["Preagonal.Scripting.GS2Compiler.Native/Preagonal.Scripting.GS2Compiler.Native.csproj", "Preagonal.Scripting.GS2Compiler.Native/"]
RUN dotnet restore "Preagonal.Scripting.GS2Compiler/Preagonal.Scripting.GS2Compiler.csproj"
RUN dotnet restore "Preagonal.Scripting.GS2Compiler.Native/Preagonal.Scripting.GS2Compiler.Native.csproj"
COPY . .
WORKDIR "/src/Preagonal.Scripting.GS2Compiler"
RUN dotnet pack Preagonal.Scripting.GS2Compiler.csproj -c $BUILD_CONFIGURATION $VER
WORKDIR "/src/Preagonal.Scripting.GS2Compiler.Native"
RUN dotnet pack Preagonal.Scripting.GS2Compiler.Native.csproj -c $BUILD_CONFIGURATION $VER