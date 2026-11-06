using System;
using System.Runtime.InteropServices;
using Preagonal.Scripting.GS2Compiler;

namespace Preagonal.Scripting.GS2Compiler.Native;

public static unsafe class NativeExports
{
	private sealed class Context;

	[StructLayout(LayoutKind.Sequential)]
	public struct Response
	{
		[MarshalAs(UnmanagedType.I1)] public bool   Success;
		public                               IntPtr ErrMsg;
		public                               IntPtr ByteCode;
		public                               uint   ByteCodeSize;
		public                               IntPtr JoinedClasses;
		public                               uint   JoinedClassesCount;
	}

	[UnmanagedCallersOnly(EntryPoint = "get_context")]
	public static IntPtr GetContext()
	{
		var handle = GCHandle.Alloc(new Context());
		return GCHandle.ToIntPtr(handle);
	}

	[UnmanagedCallersOnly(EntryPoint = "compile_code_no_header")]
	public static Response CompileCodeNoHeader(IntPtr context, byte* code) => Compile(code, null, null, false);

	[UnmanagedCallersOnly(EntryPoint = "compile_code")]
	public static Response CompileCode(IntPtr context, byte* code, byte* type, byte* name) => Compile(code, type, name, true);

	[UnmanagedCallersOnly(EntryPoint = "delete_context")]
	public static void DeleteContext(IntPtr context)
	{
		if (context != IntPtr.Zero) GCHandle.FromIntPtr(context).Free();
	}

	private static Response Compile(byte* code, byte* type, byte* name, bool withHeader)
	{
		var result = Interface.CompileCode(Utf8(code), Utf8(type) ?? "weapon", Utf8(name) ?? "npc", withHeader);
		return new()
		{
			Success            = result.Success,
			JoinedClasses      = Copy(result.JoinedClasses),
			JoinedClassesCount = (uint)result.JoinedClasses.Length,
			ErrMsg             = result.ErrMsg == null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(result.ErrMsg),
			ByteCode           = result.ByteCode.Length == 0 ? IntPtr.Zero : Copy(result.ByteCode),
			ByteCodeSize       = (uint)result.ByteCode.Length,
		};
	}

	[UnmanagedCallersOnly(EntryPoint = "free_response")]
	public static void FreeResponse(Response* response)
	{
		if (response == null) return;
		Marshal.FreeCoTaskMem(response->ErrMsg);
		Marshal.FreeCoTaskMem(response->ByteCode);
		for (var i = 0; i < response->JoinedClassesCount; i++) Marshal.FreeCoTaskMem(((IntPtr*)response->JoinedClasses)[i]);
		Marshal.FreeCoTaskMem(response->JoinedClasses);
		*response = default;
	}

	private static IntPtr Copy(string[] strings)
	{
		if (strings.Length == 0) return IntPtr.Zero;
		var ptr = Marshal.AllocCoTaskMem(strings.Length * IntPtr.Size);
		for (var i = 0; i < strings.Length; i++) ((IntPtr*)ptr)[i] = Marshal.StringToCoTaskMemUTF8(strings[i]);
		return ptr;
	}

	private static string? Utf8(byte* text) => text == null ? null : Marshal.PtrToStringUTF8((IntPtr)text);

	private static IntPtr Copy(byte[] bytes)
	{
		var ptr = Marshal.AllocCoTaskMem(bytes.Length);
		Marshal.Copy(bytes, 0, ptr, bytes.Length);
		return ptr;
	}
}