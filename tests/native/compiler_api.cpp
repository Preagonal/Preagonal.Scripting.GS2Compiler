#include <compiler/GS2Context.h>
#include <cassert>
#include <cstring>

int main()
{
    GS2Context context;
    auto result = context.compile("join(\"zebra\"); function onCreated() { join(\"alpha\"); join(\"zebra\"); this.join(\"member\"); }");
    assert(result.success && result.errors.empty() && result.bytecode.length());
    assert((result.joinedClasses == std::set<std::string>{"alpha", "member", "zebra"}));
    auto failed = context.compile("function {");
    assert(!failed.success && !failed.errors.empty() && failed.joinedClasses.empty());
    assert(failed.errors.front().level() == ErrorLevel::E_ERROR);
    auto next = context.compile("echo(\"next\");");
    assert(next.success && next.joinedClasses.empty());
    assert(result.joinedClasses.size() == 3 && result.bytecode.length());
    auto header = gs2compiler_native::compile_code("join(\"header\");", "weapon", "test");
    assert(header.Success && header.ByteCodeSize && header.JoinedClassesCount == 1);
    assert(std::strcmp(header.JoinedClasses[0], "header") == 0);
    gs2compiler_native::free_response(&header);
    auto error = gs2compiler_native::compile_code_no_header("function {");
    assert(!error.Success && error.ErrMsg && !error.ByteCode && !error.JoinedClassesCount);
    gs2compiler_native::free_response(&error);
    for (int i = 0; i < 100; ++i)
    {
        auto raw = gs2compiler_native::compile_code_no_header("join(\"native\");");
        assert(raw.Success && raw.JoinedClassesCount == 1);
        assert(std::strcmp(raw.JoinedClasses[0], "native") == 0);
        gs2compiler_native::free_response(&raw);
        assert(!raw.Success && !raw.ByteCode && !raw.JoinedClasses && !raw.JoinedClassesCount);
        gs2compiler_native::free_response(&raw);
    }
}
