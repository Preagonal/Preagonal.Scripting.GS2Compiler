#pragma once

#ifndef GS2CONTEXT_H
#define GS2CONTEXT_H

#include <set>
#include <vector>
#include <memory>
#include <GS2Compiler.h>
#include "encoding/buffer.h"
#include "exceptions/GS2CompilerError.h"
#include "encoding/graalencoding.h"

struct CompilerResponse
{
	bool success;
	std::vector<GS2CompilerError> errors;

	Buffer bytecode;
	std::set<std::string> joinedClasses;
};

class GS2Context
{
public:
	GS2Context() : context(gs2compiler_native::get_context(), gs2compiler_native::delete_context) {}

	CompilerResponse compile(const std::string& script);
	CompilerResponse compile(const std::string& script, const std::string& scriptType, const std::string& scriptName, bool saveToDisk);

	static Buffer CreateHeader(const Buffer& bytecode, const std::string& scriptType, const std::string& scriptName, bool saveToDisk);
	static CompilerResponse Compile(const std::string& script);
	static CompilerResponse Compile(const std::string& script, const std::string& scriptType, const std::string& scriptName, bool saveToDisk);

private:
	std::unique_ptr<void, decltype(&gs2compiler_native::delete_context)> context;
};

inline CompilerResponse GS2Context::compile(const std::string& script)
{
	auto native = gs2compiler_native::compile_code_no_header(context.get(), script.c_str());
	std::unique_ptr<gs2compiler_native::CompilerResponse, decltype(&gs2compiler_native::free_response)> response(&native, gs2compiler_native::free_response);
	CompilerResponse result{ native.Success, {}, Buffer{}, {} };
	if (native.ErrMsg)
		result.errors.emplace_back(ErrorLevel::E_ERROR, GS2CompilerError::ErrorCategory::Parser, native.ErrMsg);
	if (native.ByteCodeSize)
		result.bytecode.write(reinterpret_cast<const char*>(native.ByteCode), native.ByteCodeSize);
	for (uint32_t i = 0; i < native.JoinedClassesCount; ++i)
		result.joinedClasses.insert(native.JoinedClasses[i]);
	return result;
}

inline CompilerResponse GS2Context::compile(const std::string& script, const std::string& scriptType, const std::string& scriptName, bool saveToDisk)
{
	CompilerResponse results = compile(script);
	if (results.success)
		results.bytecode = CreateHeader(results.bytecode, scriptType, scriptName, saveToDisk);

	return results;
}

inline CompilerResponse GS2Context::Compile(const std::string& script)
{
	GS2Context ctx;
	return ctx.compile(script);
}

inline CompilerResponse GS2Context::Compile(const std::string& script, const std::string& scriptType, const std::string& scriptName, bool saveToDisk)
{
	GS2Context ctx;
	return ctx.compile(script, scriptType, scriptName, saveToDisk);
}

inline Buffer GS2Context::CreateHeader(const Buffer& bytecode, const std::string& scriptType, const std::string& scriptName, bool saveToDisk)
{
	// Empty bytecode buffer indicates there was a compilation error
	if (!bytecode.length())
		return {};

	// Precalculating the lengths to reduce allocations
	auto startSectionLength = scriptType.length() + scriptName.length() + 4 + 10;
	auto headerLength = 2 + startSectionLength + bytecode.length();

	Buffer bytecodeWithHeader(headerLength);

	// Write the length of the header section
	bytecodeWithHeader.Write<GraalShort>(uint16_t(startSectionLength));

	// Create the sections header
	bytecodeWithHeader.write(scriptType.c_str(), scriptType.length());
	bytecodeWithHeader.write(',');
	bytecodeWithHeader.write(scriptName.c_str(), scriptName.length());
	bytecodeWithHeader.write(',');
	bytecodeWithHeader.write(saveToDisk ? '1' : '0');
	bytecodeWithHeader.write(',');

	// Checksum or key for encrypted files
	// Needs to be new every time script gets generated, otherwise the client won't request updated script
	for (int i = 0; i < 10; i++)
		bytecodeWithHeader.Write<GraalByte>(rand() % 0xFF);

	// Write out the bytecode to the buffer
	bytecodeWithHeader.write(bytecode);
	return bytecodeWithHeader;
}

#endif
