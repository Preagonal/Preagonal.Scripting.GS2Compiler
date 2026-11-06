#ifndef GS2_COMPILER_H
#define GS2_COMPILER_H
#include <stdint.h>
#ifndef __cplusplus
#include <stdbool.h>
#endif

#ifdef __cplusplus
#define GS2COMPILER_EXTERN extern "C"
#else
#define GS2COMPILER_EXTERN extern
#endif
#ifdef _WIN32
#define DLL_EXPORT GS2COMPILER_EXTERN __declspec(dllimport)
#else
#define DLL_EXPORT GS2COMPILER_EXTERN __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
namespace gs2compiler_native
{
#endif

typedef struct CompilerResponse {
    bool Success;
    const char *ErrMsg;
    unsigned char *ByteCode;
    uint32_t ByteCodeSize;
    const char *const *JoinedClasses;
    uint32_t JoinedClassesCount;
} CompilerResponse;

DLL_EXPORT void *get_context();

DLL_EXPORT CompilerResponse compile_code_no_header(void *context, const char *code);

DLL_EXPORT CompilerResponse compile_code(void *context, const char *code, const char *type, const char *name);

// Releases all response allocations and clears the response. Strings are UTF-8.
DLL_EXPORT void free_response(CompilerResponse *response);

DLL_EXPORT void delete_context(void *context);
#ifdef __cplusplus
}
#endif
#undef DLL_EXPORT
#undef GS2COMPILER_EXTERN
#endif
