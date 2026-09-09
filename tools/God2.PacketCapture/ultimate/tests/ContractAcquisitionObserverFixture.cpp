#include <windows.h>

#include <cstdio>
#include <cstdint>

namespace {

volatile LONG g_stop{};

__declspec(noinline) int __cdecl candidateCdecl(int value, const int* pointer)
{
    return value + (pointer != nullptr ? *pointer : 0);
}

__declspec(noinline) int __stdcall candidateStdcall(int value, int increment)
{
    return value + increment;
}

__declspec(noinline) int __fastcall candidateFastcall(int value, int increment)
{
    return value + increment;
}

} // namespace

int main()
{
    auto* const image = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    const auto rva = [image](const void* value) {
        return static_cast<unsigned long>(
            reinterpret_cast<const unsigned char*>(value) - image);
    };
    std::printf(
        "{\"ProcessId\":%lu,\"ImageBase\":%lu,"
        "\"CandidateCdeclRva\":%lu,\"CandidateStdcallRva\":%lu,"
        "\"CandidateFastcallRva\":%lu}\n",
        GetCurrentProcessId(),
        static_cast<unsigned long>(reinterpret_cast<std::uintptr_t>(image)),
        rva(reinterpret_cast<const void*>(&candidateCdecl)),
        rva(reinterpret_cast<const void*>(&candidateStdcall)),
        rva(reinterpret_cast<const void*>(&candidateFastcall)));
    std::fflush(stdout);

    int seed = 7;
    int accumulator = 0;
    const ULONGLONG deadline = GetTickCount64() + 30'000u;
    while (InterlockedCompareExchange(&g_stop, 0, 0) == 0 &&
           GetTickCount64() < deadline) {
        accumulator += candidateCdecl(3, &seed);
        accumulator += candidateStdcall(5, 2);
        accumulator += candidateFastcall(11, 4);
        Sleep(2);
    }
    std::printf("{\"Completed\":true,\"AccumulatorNonZero\":%s}\n",
        accumulator != 0 ? "true" : "false");
    return accumulator == 0 ? 2 : 0;
}
