using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace God2.ContractAcquisition
{
    public sealed class CandidateSpec
    {
        public string CandidateId { get; set; }
        public string Domain { get; set; }
        public uint Rva { get; set; }
        public bool CaptureConsumerSteps { get; set; }
    }

    public sealed class ValueEvidence
    {
        public string ValueClass { get; set; }
        public string StableToken { get; set; }
    }

    public sealed class CandidateRuntimeObservation
    {
        public string ObservationId { get; set; }
        public string Phase { get; set; }
        public int CandidateIndex { get; set; }
        public string CandidateId { get; set; }
        public string Domain { get; set; }
        public uint CandidateRva { get; set; }
        public uint ThreadId { get; set; }
        public long InvocationId { get; set; }
        public int InvocationDepth { get; set; }
        public bool NestedInvocation { get; set; }
        public bool SameCandidateRecursion { get; set; }
        public long Qpc { get; set; }
        public long DurationQpc { get; set; }
        public uint ReturnRva { get; set; }
        public int? StackCleanupBytes { get; set; }
        public bool RuntimeObservation { get; set; }
        public bool RegisterEvidenceCaptured { get; set; }
        public bool StackEvidenceCaptured { get; set; }
        public bool ReturnEvidenceCaptured { get; set; }
        public bool ConsumerEvidenceCaptured { get; set; }
        public int ConsumerStepIndex { get; set; }
        public uint InstructionRva { get; set; }
        public string InstructionBytesHex { get; set; }
        public ValueEvidence Eax { get; set; }
        public ValueEvidence Ebx { get; set; }
        public ValueEvidence Ecx { get; set; }
        public ValueEvidence Edx { get; set; }
        public ValueEvidence Esi { get; set; }
        public ValueEvidence Edi { get; set; }
        public ValueEvidence Ebp { get; set; }
        public ValueEvidence[] StackArguments { get; set; }
    }

    public sealed class ObserverResult
    {
        public bool Attached { get; set; }
        public bool Detached { get; set; }
        public bool MemoryWritten { get; set; }
        public bool DebugRegistersCleared { get; set; }
        public int CandidateCount { get; set; }
        public int RuntimeObservationCount { get; set; }
        public int EntryObservationCount { get; set; }
        public int ReturnObservationCount { get; set; }
        public int ConsumerObservationCount { get; set; }
        public int ThreadCount { get; set; }
        public int ThreadConfigurationFailureCount { get; set; }
        public int ReadFailureCount { get; set; }
        public int UnmatchedReturnCount { get; set; }
        public int NonCandidateSingleStepCount { get; set; }
        public string TokenSaltSha256 { get; set; }
        public string Status { get; set; }
        public CandidateRuntimeObservation[] Observations { get; set; }
        public string[] Diagnostics { get; set; }
    }

    public static class HardwareBreakpointObserver
    {
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessVmRead = 0x0010;
        private const uint ThreadSuspendResume = 0x0002;
        private const uint ThreadGetContext = 0x0008;
        private const uint ThreadSetContext = 0x0010;
        private const uint ThreadQueryInformation = 0x0040;
        private const uint ContextI386 = 0x00010000;
        private const uint ContextControl = ContextI386 | 0x00000001;
        private const uint ContextInteger = ContextI386 | 0x00000002;
        private const uint ContextDebugRegisters = ContextI386 | 0x00000010;
        private const uint ExceptionDebugEvent = 1;
        private const uint CreateThreadDebugEvent = 2;
        private const uint CreateProcessDebugEvent = 3;
        private const uint ExitThreadDebugEvent = 4;
        private const uint ExitProcessDebugEvent = 5;
        private const uint ExceptionSingleStep = 0x80000004;
        private const uint ExceptionBreakpoint = 0x80000003;
        private const uint StatusWx86SingleStep = 0x4000001E;
        private const uint StatusWx86Breakpoint = 0x4000001F;
        private const uint DbgContinue = 0x00010002;
        private const uint DbgExceptionNotHandled = 0x80010001;
        private const uint MemCommit = 0x1000;
        private const uint PageGuard = 0x100;
        private const uint PageNoAccess = 0x01;
        private const int MaximumCandidates = 12;
        private const int MaximumActiveBreakpoints = 3;
        private const int StackArgumentCount = 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct Wow64FloatingSaveArea
        {
            public uint ControlWord;
            public uint StatusWord;
            public uint TagWord;
            public uint ErrorOffset;
            public uint ErrorSelector;
            public uint DataOffset;
            public uint DataSelector;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]
            public byte[] RegisterArea;
            public uint Cr0NpxState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Wow64Context
        {
            public uint ContextFlags;
            public uint Dr0;
            public uint Dr1;
            public uint Dr2;
            public uint Dr3;
            public uint Dr6;
            public uint Dr7;
            public Wow64FloatingSaveArea FloatSave;
            public uint SegGs;
            public uint SegFs;
            public uint SegEs;
            public uint SegDs;
            public uint Edi;
            public uint Esi;
            public uint Ebx;
            public uint Edx;
            public uint Ecx;
            public uint Eax;
            public uint Ebp;
            public uint Eip;
            public uint SegCs;
            public uint EFlags;
            public uint Esp;
            public uint SegSs;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
            public byte[] ExtendedRegisters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        private sealed class InvocationFrame
        {
            public int CandidateIndex;
            public long InvocationId;
            public int Depth;
            public uint EntryEsp;
            public uint ReturnAddress;
            public long EntryQpc;
        }

        private sealed class ThreadState
        {
            public readonly List<InvocationFrame> Frames = new List<InvocationFrame>();
            public InvocationFrame PendingConsumerFrame;
            public int ConsumerStepIndex;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DebugActiveProcess(uint processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DebugActiveProcessStop(uint processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DebugSetProcessKillOnExit(bool killOnExit);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WaitForDebugEvent(IntPtr debugEvent, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ContinueDebugEvent(uint processId, uint threadId, uint continueStatus);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint access, bool inheritHandle, uint threadId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Wow64GetThreadContext(IntPtr thread, ref Wow64Context context);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Wow64SetThreadContext(IntPtr thread, ref Wow64Context context);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address,
            byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address,
            out MemoryBasicInformation information, UIntPtr length);
        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceCounter(out long value);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private static Wow64Context NewContext()
        {
            Wow64Context context = new Wow64Context();
            context.ContextFlags = ContextControl | ContextInteger | ContextDebugRegisters;
            context.FloatSave.RegisterArea = new byte[80];
            context.ExtendedRegisters = new byte[512];
            return context;
        }

        private static string Hex(byte[] value)
        {
            return BitConverter.ToString(value).Replace("-", string.Empty);
        }

        private static ValueEvidence ClassifyValue(IntPtr process, uint value,
            uint imageBase, uint imageSize, byte[] salt)
        {
            ValueEvidence result = new ValueEvidence();
            if (value == 0)
            {
                result.ValueClass = "Zero";
                return result;
            }
            if (value >= imageBase && value - imageBase < imageSize)
            {
                result.ValueClass = "OfficialImagePointer";
                result.StableToken = "RVA-" + (value - imageBase).ToString("X8", CultureInfo.InvariantCulture);
                return result;
            }
            MemoryBasicInformation information;
            UIntPtr queried = VirtualQueryEx(process, new IntPtr(unchecked((int)value)),
                out information, new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryBasicInformation))));
            if (queried != UIntPtr.Zero && information.State == MemCommit &&
                (information.Protect & (PageGuard | PageNoAccess)) == 0)
            {
                byte[] material = new byte[salt.Length + 4];
                Buffer.BlockCopy(salt, 0, material, 0, salt.Length);
                Buffer.BlockCopy(BitConverter.GetBytes(value), 0, material, salt.Length, 4);
                using (SHA256 sha = SHA256.Create())
                {
                    result.StableToken = "PTR-" + Hex(sha.ComputeHash(material)).Substring(0, 24);
                }
                result.ValueClass = "ReadableMemoryPointer";
                return result;
            }
            result.ValueClass = value <= 0xFFFFu ? "SmallUnsigned" : "OpaqueScalar";
            return result;
        }

        private static bool ReadStackWords(IntPtr process, uint esp, out uint[] words)
        {
            byte[] bytes = new byte[(StackArgumentCount + 1) * 4];
            UIntPtr read;
            if (!ReadProcessMemory(process, new IntPtr(unchecked((int)esp)), bytes,
                    new UIntPtr((uint)bytes.Length), out read) ||
                read.ToUInt64() != (ulong)bytes.Length)
            {
                words = new uint[0];
                return false;
            }
            words = new uint[StackArgumentCount + 1];
            for (int index = 0; index < words.Length; ++index)
                words[index] = BitConverter.ToUInt32(bytes, index * 4);
            return true;
        }

        private static ValueEvidence[] ClassifyStack(IntPtr process, uint[] words,
            uint imageBase, uint imageSize, byte[] salt)
        {
            if (words == null || words.Length <= 1) return new ValueEvidence[0];
            ValueEvidence[] result = new ValueEvidence[words.Length - 1];
            for (int index = 1; index < words.Length; ++index)
                result[index - 1] = ClassifyValue(process, words[index], imageBase, imageSize, salt);
            return result;
        }

        private static CandidateRuntimeObservation BuildObservation(IntPtr process,
            CandidateSpec candidate, int candidateIndex, uint threadId,
            InvocationFrame frame, Wow64Context context, string phase,
            uint[] stackWords, uint imageBase, uint imageSize, byte[] salt)
        {
            long qpc;
            QueryPerformanceCounter(out qpc);
            CandidateRuntimeObservation row = new CandidateRuntimeObservation();
            row.ObservationId = Guid.NewGuid().ToString("D");
            row.Phase = phase;
            row.CandidateIndex = candidateIndex;
            row.CandidateId = candidate.CandidateId;
            row.Domain = candidate.Domain;
            row.CandidateRva = candidate.Rva;
            row.ThreadId = threadId;
            row.InvocationId = frame.InvocationId;
            row.InvocationDepth = frame.Depth;
            row.NestedInvocation = frame.Depth > 1;
            row.Qpc = qpc;
            row.RuntimeObservation = true;
            row.RegisterEvidenceCaptured = true;
            row.StackEvidenceCaptured = stackWords != null && stackWords.Length > 1;
            row.ReturnEvidenceCaptured = phase == "Return";
            row.ConsumerEvidenceCaptured = phase == "ConsumerStep";
            if (context.Eip >= imageBase && context.Eip - imageBase < imageSize)
            {
                row.InstructionRva = context.Eip - imageBase;
                byte[] instructionBytes = new byte[8];
                UIntPtr instructionBytesRead;
                if (ReadProcessMemory(process, new IntPtr(unchecked((int)context.Eip)),
                        instructionBytes, new UIntPtr((uint)instructionBytes.Length),
                        out instructionBytesRead) && instructionBytesRead.ToUInt64() == 8UL)
                    row.InstructionBytesHex = Hex(instructionBytes);
            }
            row.Eax = ClassifyValue(process, context.Eax, imageBase, imageSize, salt);
            row.Ebx = ClassifyValue(process, context.Ebx, imageBase, imageSize, salt);
            row.Ecx = ClassifyValue(process, context.Ecx, imageBase, imageSize, salt);
            row.Edx = ClassifyValue(process, context.Edx, imageBase, imageSize, salt);
            row.Esi = ClassifyValue(process, context.Esi, imageBase, imageSize, salt);
            row.Edi = ClassifyValue(process, context.Edi, imageBase, imageSize, salt);
            row.Ebp = ClassifyValue(process, context.Ebp, imageBase, imageSize, salt);
            row.StackArguments = ClassifyStack(process, stackWords, imageBase, imageSize, salt);
            if (frame.ReturnAddress >= imageBase && frame.ReturnAddress - imageBase < imageSize)
                row.ReturnRva = frame.ReturnAddress - imageBase;
            if (phase == "Return" || phase == "ConsumerStep")
            {
                row.DurationQpc = qpc - frame.EntryQpc;
            }
            if (phase == "Return")
            {
                long cleanup = (long)context.Esp - ((long)frame.EntryEsp + 4L);
                if (cleanup >= 0 && cleanup <= 256 && cleanup % 4 == 0)
                    row.StackCleanupBytes = (int)cleanup;
            }
            return row;
        }

        private static bool ConfigureThread(uint threadId, uint[] addresses,
            uint returnAddress, bool suspend, out IntPtr retainedHandle)
        {
            retainedHandle = IntPtr.Zero;
            IntPtr thread = OpenThread(ThreadSuspendResume | ThreadGetContext |
                ThreadSetContext | ThreadQueryInformation, false, threadId);
            if (thread == IntPtr.Zero) return false;
            bool suspended = false;
            try
            {
                if (suspend)
                {
                    if (SuspendThread(thread) == uint.MaxValue) return false;
                    suspended = true;
                }
                Wow64Context context = NewContext();
                if (!Wow64GetThreadContext(thread, ref context)) return false;
                context.Dr0 = addresses.Length > 0 ? addresses[0] : 0;
                context.Dr1 = addresses.Length > 1 ? addresses[1] : 0;
                context.Dr2 = addresses.Length > 2 ? addresses[2] : 0;
                context.Dr3 = returnAddress;
                context.Dr6 = 0;
                context.Dr7 = 0;
                for (int index = 0; index < addresses.Length; ++index)
                    context.Dr7 |= 1u << (index * 2);
                if (returnAddress != 0) context.Dr7 |= 1u << 6;
                if (!Wow64SetThreadContext(thread, ref context)) return false;
                if (suspend)
                {
                    retainedHandle = thread;
                    thread = IntPtr.Zero;
                }
                return true;
            }
            finally
            {
                if (thread != IntPtr.Zero)
                {
                    if (suspended) ResumeThread(thread);
                    CloseHandle(thread);
                }
            }
        }

        private static void ResumeAndClose(List<IntPtr> handles)
        {
            foreach (IntPtr handle in handles)
            {
                ResumeThread(handle);
                CloseHandle(handle);
            }
            handles.Clear();
        }

        private static uint[] ActiveAddressBatch(uint[] addresses, int start)
        {
            int count = Math.Min(MaximumActiveBreakpoints, addresses.Length - start);
            uint[] active = new uint[count];
            Array.Copy(addresses, start, active, 0, count);
            return active;
        }

        private static bool ClearBreakpoints(uint processId, uint[] addresses,
            List<string> diagnostics)
        {
            List<IntPtr> suspended = new List<IntPtr>();
            bool cleared = true;
            try
            {
                Process process;
                try
                {
                    process = Process.GetProcessById((int)processId);
                    if (process.HasExited) return true;
                }
                catch (ArgumentException)
                {
                    return true;
                }
                foreach (ProcessThread processThread in process.Threads)
                {
                    IntPtr retained;
                    if (!ConfigureThread((uint)processThread.Id, new uint[0], 0, true, out retained))
                    {
                        cleared = false;
                        diagnostics.Add("CLEAR_THREAD_FAILED:" + processThread.Id.ToString(CultureInfo.InvariantCulture));
                    }
                    else if (retained != IntPtr.Zero)
                    {
                        suspended.Add(retained);
                    }
                }
                return cleared;
            }
            finally
            {
                ResumeAndClose(suspended);
            }
        }

        public static ObserverResult Observe(uint processId, uint imageBase,
            uint imageSize, CandidateSpec[] candidates, int observeSeconds,
            int maximumObservationsPerCandidate)
        {
            ObserverResult result = new ObserverResult();
            List<CandidateRuntimeObservation> observations = new List<CandidateRuntimeObservation>();
            List<string> diagnostics = new List<string>();
            result.MemoryWritten = false;
            result.Observations = new CandidateRuntimeObservation[0];
            result.Diagnostics = new string[0];
            if (IntPtr.Size != 8)
            {
                result.Status = "EVIDENCE_BLOCKED_WOW64_OBSERVER_REQUIRES_X64_HOST";
                return result;
            }
            if (candidates == null || candidates.Length == 0 ||
                candidates.Length > MaximumCandidates)
                throw new ArgumentOutOfRangeException("candidates");
            if (observeSeconds < 1 || observeSeconds > 600)
                throw new ArgumentOutOfRangeException("observeSeconds");
            if (maximumObservationsPerCandidate < 1 || maximumObservationsPerCandidate > 64)
                throw new ArgumentOutOfRangeException("maximumObservationsPerCandidate");

            result.CandidateCount = candidates.Length;
            uint[] addresses = new uint[candidates.Length];
            for (int index = 0; index < candidates.Length; ++index)
                addresses[index] = checked(imageBase + candidates[index].Rva);
            byte[] salt = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(salt);
            using (SHA256 sha = SHA256.Create())
                result.TokenSaltSha256 = Hex(sha.ComputeHash(salt));

            IntPtr processHandle = OpenProcess(ProcessQueryInformation | ProcessVmRead,
                false, processId);
            if (processHandle == IntPtr.Zero)
            {
                result.Status = "EVIDENCE_BLOCKED_PROCESS_READ_UNAVAILABLE";
                diagnostics.Add(new Win32Exception(Marshal.GetLastWin32Error()).Message);
                result.Diagnostics = diagnostics.ToArray();
                return result;
            }

            bool debuggerAttached = false;
            Dictionary<uint, ThreadState> threads = new Dictionary<uint, ThreadState>();
            int[] entries = new int[candidates.Length];
            int batchCount = (candidates.Length + MaximumActiveBreakpoints - 1) /
                MaximumActiveBreakpoints;
            int activeBatch = 0;
            int activeStart = 0;
            uint[] activeAddresses = ActiveAddressBatch(addresses, activeStart);
            long invocationSequence = 0;
            IntPtr debugEvent = Marshal.AllocHGlobal(192);
            try
            {
                for (int offset = 0; offset < 192; offset += 4)
                    Marshal.WriteInt32(debugEvent, offset, 0);
                if (!DebugActiveProcess(processId))
                {
                    result.Status = "EVIDENCE_BLOCKED_DEBUG_ATTACH_UNAVAILABLE";
                    diagnostics.Add(new Win32Exception(Marshal.GetLastWin32Error()).Message);
                    return result;
                }
                debuggerAttached = true;
                result.Attached = true;
                if (!DebugSetProcessKillOnExit(false))
                    diagnostics.Add("DEBUG_KILL_POLICY_NOT_CONFIRMED:" +
                        Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));

                Process process = Process.GetProcessById((int)processId);
                foreach (ProcessThread processThread in process.Threads)
                {
                    uint threadId = (uint)processThread.Id;
                    threads[threadId] = new ThreadState();
                    IntPtr retained;
                    if (!ConfigureThread(threadId, activeAddresses, 0, true, out retained))
                        result.ThreadConfigurationFailureCount++;
                    if (retained != IntPtr.Zero)
                    {
                        ResumeThread(retained);
                        CloseHandle(retained);
                    }
                }

                DateTime deadline = DateTime.UtcNow.AddSeconds(observeSeconds);
                int batchMilliseconds = Math.Max(1000,
                    (observeSeconds * 1000) / batchCount);
                DateTime batchDeadline = DateTime.UtcNow.AddMilliseconds(batchMilliseconds);
                DateTime? rotationGraceDeadline = null;
                bool processExited = false;
                while (DateTime.UtcNow < deadline && !processExited)
                {
                    bool receivedDebugEvent = WaitForDebugEvent(debugEvent, 25);
                    if (!receivedDebugEvent)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 121) diagnostics.Add("WAIT_FAILED:" + error.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        uint eventCode = unchecked((uint)Marshal.ReadInt32(debugEvent, 0));
                        uint eventProcessId = unchecked((uint)Marshal.ReadInt32(debugEvent, 4));
                        uint threadId = unchecked((uint)Marshal.ReadInt32(debugEvent, 8));
                        uint continueStatus = DbgContinue;
                        try
                        {
                            if (eventCode == CreateThreadDebugEvent || eventCode == CreateProcessDebugEvent)
                            {
                                if (!threads.ContainsKey(threadId)) threads[threadId] = new ThreadState();
                                IntPtr ignored;
                                if (!ConfigureThread(threadId, activeAddresses, 0, false, out ignored))
                                    result.ThreadConfigurationFailureCount++;
                            }
                            else if (eventCode == ExitThreadDebugEvent)
                            {
                                threads.Remove(threadId);
                            }
                            else if (eventCode == ExitProcessDebugEvent)
                            {
                                diagnostics.Add("EXIT_PROCESS_EVENT");
                                processExited = true;
                            }
                            else if (eventCode == ExceptionDebugEvent)
                            {
                                int unionOffset = IntPtr.Size == 8 ? 16 : 12;
                                uint exceptionCode = unchecked((uint)Marshal.ReadInt32(debugEvent, unionOffset));
                                if (exceptionCode == ExceptionSingleStep ||
                                    exceptionCode == StatusWx86SingleStep)
                                {
                                    IntPtr thread = OpenThread(ThreadGetContext | ThreadSetContext |
                                        ThreadQueryInformation, false, threadId);
                                    if (thread == IntPtr.Zero)
                                    {
                                        result.ThreadConfigurationFailureCount++;
                                    }
                                    else
                                    {
                                        try
                                        {
                                            Wow64Context context = NewContext();
                                            if (!Wow64GetThreadContext(thread, ref context))
                                            {
                                                result.ThreadConfigurationFailureCount++;
                                            }
                                            else
                                            {
                                                ThreadState state;
                                                if (!threads.TryGetValue(threadId, out state))
                                                {
                                                    state = new ThreadState();
                                                    threads[threadId] = state;
                                                }
                                                bool handled = false;
                                                bool processorSingleStep = (context.Dr6 & (1u << 14)) != 0;
                                                if (processorSingleStep && state.PendingConsumerFrame != null)
                                                {
                                                    InvocationFrame consumerFrame = state.PendingConsumerFrame;
                                                    uint[] consumerWords;
                                                    if (!ReadStackWords(processHandle, context.Esp, out consumerWords))
                                                    {
                                                        result.ReadFailureCount++;
                                                        consumerWords = new uint[0];
                                                    }
                                                    CandidateRuntimeObservation consumer = BuildObservation(processHandle,
                                                        candidates[consumerFrame.CandidateIndex], consumerFrame.CandidateIndex,
                                                        threadId, consumerFrame, context, "ConsumerStep", consumerWords,
                                                        imageBase, imageSize, salt);
                                                    consumer.ConsumerStepIndex = ++state.ConsumerStepIndex;
                                                    observations.Add(consumer);
                                                    handled = true;
                                                    if (state.ConsumerStepIndex >= 4)
                                                    {
                                                        state.PendingConsumerFrame = null;
                                                        state.ConsumerStepIndex = 0;
                                                    }
                                                }
                                                if ((context.Dr6 & (1u << 3)) != 0 && state.Frames.Count > 0)
                                                {
                                                    InvocationFrame frame = state.Frames[state.Frames.Count - 1];
                                                    state.Frames.RemoveAt(state.Frames.Count - 1);
                                                    uint[] exitWords;
                                                    if (!ReadStackWords(processHandle, context.Esp, out exitWords))
                                                    {
                                                        result.ReadFailureCount++;
                                                        exitWords = new uint[0];
                                                    }
                                                    CandidateRuntimeObservation exit = BuildObservation(processHandle,
                                                        candidates[frame.CandidateIndex], frame.CandidateIndex,
                                                        threadId, frame, context, "Return", exitWords,
                                                        imageBase, imageSize, salt);
                                                    exit.SameCandidateRecursion = state.Frames.Exists(delegate(InvocationFrame prior)
                                                    {
                                                        return prior.CandidateIndex == frame.CandidateIndex;
                                                    });
                                                    observations.Add(exit);
                                                    if (candidates[frame.CandidateIndex].CaptureConsumerSteps)
                                                    {
                                                        state.PendingConsumerFrame = frame;
                                                        state.ConsumerStepIndex = 0;
                                                    }
                                                    handled = true;
                                                }
                                                for (int slot = 0; slot < activeAddresses.Length; ++slot)
                                                {
                                                    if ((context.Dr6 & (1u << slot)) == 0) continue;
                                                    handled = true;
                                                    int candidateIndex = activeStart + slot;
                                                    if (entries[candidateIndex] >= maximumObservationsPerCandidate) continue;
                                                    uint[] words;
                                                    if (!ReadStackWords(processHandle, context.Esp, out words))
                                                    {
                                                        result.ReadFailureCount++;
                                                        words = new uint[0];
                                                    }
                                                    uint returnAddress = words.Length > 0 ? words[0] : 0;
                                                    InvocationFrame frame = new InvocationFrame();
                                                    frame.CandidateIndex = candidateIndex;
                                                    frame.InvocationId = ++invocationSequence;
                                                    frame.Depth = state.Frames.Count + 1;
                                                    frame.EntryEsp = context.Esp;
                                                    frame.ReturnAddress = returnAddress;
                                                    QueryPerformanceCounter(out frame.EntryQpc);
                                                    CandidateRuntimeObservation entry = BuildObservation(processHandle,
                                                        candidates[candidateIndex], candidateIndex, threadId, frame, context,
                                                        "Entry", words, imageBase, imageSize, salt);
                                                    entry.SameCandidateRecursion = state.Frames.Exists(delegate(InvocationFrame prior)
                                                    {
                                                        return prior.CandidateIndex == candidateIndex;
                                                    });
                                                    observations.Add(entry);
                                                    entries[candidateIndex]++;
                                                    if (returnAddress >= imageBase && returnAddress - imageBase < imageSize)
                                                        state.Frames.Add(frame);
                                                }
                                                context.Dr6 = 0;
                                                // Resume past the instruction that raised this
                                                // execution breakpoint before re-arming DR0-DR3.
                                                 context.EFlags |= 0x00010000u;
                                                 if (state.PendingConsumerFrame != null)
                                                     context.EFlags |= 0x00000100u;
                                                 else
                                                     context.EFlags &= ~0x00000100u;
                                                context.Dr3 = state.Frames.Count > 0 ?
                                                    state.Frames[state.Frames.Count - 1].ReturnAddress : 0;
                                                context.Dr7 = 0;
                                                for (int slot = 0; slot < activeAddresses.Length; ++slot)
                                                    context.Dr7 |= 1u << (slot * 2);
                                                if (context.Dr3 != 0) context.Dr7 |= 1u << 6;
                                                if (!Wow64SetThreadContext(thread, ref context))
                                                    result.ThreadConfigurationFailureCount++;
                                                if (!handled) result.NonCandidateSingleStepCount++;
                                            }
                                        }
                                        finally
                                        {
                                            CloseHandle(thread);
                                        }
                                    }
                                }
                                else if (exceptionCode == ExceptionBreakpoint ||
                                    exceptionCode == StatusWx86Breakpoint)
                                {
                                    continueStatus = DbgContinue;
                                }
                                else
                                {
                                    if (diagnostics.Count < 32)
                                        diagnostics.Add("UNHANDLED_EXCEPTION:" +
                                            exceptionCode.ToString("X8", CultureInfo.InvariantCulture));
                                    continueStatus = DbgExceptionNotHandled;
                                }
                            }
                        }
                        finally
                        {
                            if (!ContinueDebugEvent(eventProcessId, threadId, continueStatus))
                                diagnostics.Add("CONTINUE_FAILED:" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
                        }
                    }
                    bool complete = true;
                    for (int index = 0; index < entries.Length; ++index)
                        complete = complete && entries[index] >= maximumObservationsPerCandidate;
                    bool pendingReturn = false;
                    foreach (ThreadState state in threads.Values)
                        pendingReturn = pendingReturn || state.Frames.Count != 0;
                    if (activeBatch + 1 < batchCount && DateTime.UtcNow >= batchDeadline)
                    {
                        if (pendingReturn && rotationGraceDeadline == null)
                        {
                            rotationGraceDeadline = DateTime.UtcNow.AddMilliseconds(500);
                        }
                        else if (!pendingReturn || DateTime.UtcNow >= rotationGraceDeadline.Value)
                        {
                            foreach (ThreadState state in threads.Values)
                            {
                                result.UnmatchedReturnCount += state.Frames.Count;
                                state.Frames.Clear();
                            }
                            activeBatch++;
                            activeStart = activeBatch * MaximumActiveBreakpoints;
                            activeAddresses = ActiveAddressBatch(addresses, activeStart);
                            foreach (uint configuredThreadId in new List<uint>(threads.Keys))
                            {
                                IntPtr retained;
                                if (!ConfigureThread(configuredThreadId, activeAddresses, 0,
                                        true, out retained))
                                    result.ThreadConfigurationFailureCount++;
                                if (retained != IntPtr.Zero)
                                {
                                    ResumeThread(retained);
                                    CloseHandle(retained);
                                }
                            }
                            diagnostics.Add("ROTATED_ACTIVE_BATCH:" +
                                activeBatch.ToString(CultureInfo.InvariantCulture));
                            batchDeadline = DateTime.UtcNow.AddMilliseconds(batchMilliseconds);
                            rotationGraceDeadline = null;
                        }
                    }
                    if (complete && !pendingReturn) break;
                }
            }
            finally
            {
                if (debuggerAttached)
                {
                    try
                    {
                        result.DebugRegistersCleared = ClearBreakpoints(processId, addresses, diagnostics);
                    }
                    catch (Exception error)
                    {
                        result.DebugRegistersCleared = false;
                        diagnostics.Add("DEBUG_CLEAR_EXCEPTION:" + error.GetType().Name);
                    }
                    try
                    {
                        result.Detached = DebugActiveProcessStop(processId);
                        if (!result.Detached)
                            diagnostics.Add("DEBUG_DETACH_FAILED:" + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
                    }
                    catch (Exception error)
                    {
                        result.Detached = false;
                        diagnostics.Add("DEBUG_DETACH_EXCEPTION:" + error.GetType().Name);
                    }
                }
                Marshal.FreeHGlobal(debugEvent);
                CloseHandle(processHandle);
            }

            foreach (ThreadState state in threads.Values)
                result.UnmatchedReturnCount += state.Frames.Count;
            result.ThreadCount = threads.Count;
            result.Observations = observations.ToArray();
            result.RuntimeObservationCount = observations.Count;
            foreach (CandidateRuntimeObservation observation in observations)
            {
                if (observation.Phase == "Entry") result.EntryObservationCount++;
                if (observation.Phase == "Return") result.ReturnObservationCount++;
                if (observation.Phase == "ConsumerStep") result.ConsumerObservationCount++;
            }
            result.Status = result.Attached && result.Detached &&
                result.DebugRegistersCleared && result.ThreadConfigurationFailureCount == 0 ?
                (result.EntryObservationCount > 0 ?
                    "CONTRACT_ACQUISITION_RUNTIME_OBSERVED" :
                    "EVIDENCE_BLOCKED_CANDIDATES_NOT_TRIGGERED_IN_SESSION") :
                "EVIDENCE_BLOCKED_HARDWARE_OBSERVER_INCOMPLETE";
            result.Diagnostics = diagnostics.ToArray();
            return result;
        }
    }
}
