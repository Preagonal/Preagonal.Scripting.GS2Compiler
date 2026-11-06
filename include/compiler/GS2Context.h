#pragma once

#ifndef GS2CONTEXT_H
#define GS2CONTEXT_H

#include <set>
#include <vector>
#include <memory>
#include <GS2Compiler.h>
#include "encoding/buffer.h"
#include "exceptions/GS2CompilerError.h"

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

#endif
