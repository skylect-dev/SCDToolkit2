using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SCDToolkit.Desktop.Services
{
    public sealed class Kh2HookService
    {
        private const int PointerOffsetFromMarkerString = 0x20;

        private int _cachedProcessId = -1;
        private nint _cachedMusicApply;
        private nint _cachedFieldPath;
        private nint _cachedBattlePath;

        // Fallbacks from docs/topaznotes.md
        private const long FallbackMusicApplyOffset = 0x7B00A0;
        private const long FallbackFieldPathOffset = 0x7B00E0;
        private const long FallbackBattlePathOffset = 0x7B0210;

        public bool TryGetKh2Process(out Process? process)
        {
            process = null;

            try
            {
                var candidates = Process.GetProcesses();
                foreach (var p in candidates)
                {
                    try
                    {
                        var name = p.ProcessName ?? string.Empty;
                        if (IsLikelyKh2ProcessName(name))
                        {
                            process = p;
                            return true;
                        }
                    }
                    catch
                    {
                        // ignore process we cannot query
                    }
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        private static bool IsLikelyKh2ProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            var n = processName.Trim();

            // Common Steam/Epic process names (no extension)
            if (n.Contains("KINGDOM HEARTS II", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.Contains("KH2", StringComparison.OrdinalIgnoreCase) && n.Contains("FINAL", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.Contains("KINGDOMHEARTSII", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        public Kh2HookResult ApplyScdPaths(string? fieldScdPath, string? battleScdPath)
        {
            if (!TryGetKh2Process(out var proc) || proc == null)
            {
                return new Kh2HookResult(false, "KH2 process not found.");
            }

            try
            {
                using var mem = ProcessMemory.Open(proc);

                if (!TryResolveAddressesCached(proc.Id, mem, out var musicApplyAddr, out var fieldPathAddr, out var battlePathAddr, out var resolveMessage))
                {
                    return new Kh2HookResult(false, resolveMessage);
                }

                // Write paths (empty => do not change according to mod behavior)
                WriteFixedString(mem, fieldPathAddr, fieldScdPath, 256);
                WriteFixedString(mem, battlePathAddr, battleScdPath, 256);

                // Apply
                mem.WriteByte(musicApplyAddr, 0x01);

                return new Kh2HookResult(true, "Applied SCDHook paths.");
            }
            catch (Exception ex)
            {
                return new Kh2HookResult(false, ex.Message);
            }
        }

        public bool TryWarmupPointersMarkerOnly(out string message)
        {
            message = string.Empty;

            if (!TryGetKh2Process(out var proc) || proc == null)
            {
                message = "KH2 process not found.";
                return false;
            }

            try
            {
                using var mem = ProcessMemory.Open(proc);
                if (TryResolveAddressesCachedMarkerOnly(proc.Id, mem, out _, out _, out _, out message))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private bool TryResolveAddressesCached(int processId, ProcessMemory mem, out nint musicApply, out nint fieldPath, out nint battlePath, out string message)
        {
            // Reuse cached pointers for the same process to avoid repeated full memory scans.
            if (_cachedProcessId == processId && _cachedMusicApply != 0 && _cachedFieldPath != 0 && _cachedBattlePath != 0)
            {
                musicApply = _cachedMusicApply;
                fieldPath = _cachedFieldPath;
                battlePath = _cachedBattlePath;
                message = "Resolved SCDHook pointers via cache.";
                return true;
            }

            if (TryResolveAddresses(mem, out musicApply, out fieldPath, out battlePath, out message))
            {
                _cachedProcessId = processId;
                _cachedMusicApply = musicApply;
                _cachedFieldPath = fieldPath;
                _cachedBattlePath = battlePath;
                return true;
            }

            _cachedProcessId = -1;
            _cachedMusicApply = 0;
            _cachedFieldPath = 0;
            _cachedBattlePath = 0;
            return false;
        }

        private bool TryResolveAddressesCachedMarkerOnly(int processId, ProcessMemory mem, out nint musicApply, out nint fieldPath, out nint battlePath, out string message)
        {
            // Reuse cached pointers for the same process to avoid repeated full memory scans.
            if (_cachedProcessId == processId && _cachedMusicApply != 0 && _cachedFieldPath != 0 && _cachedBattlePath != 0)
            {
                musicApply = _cachedMusicApply;
                fieldPath = _cachedFieldPath;
                battlePath = _cachedBattlePath;
                message = "Resolved SCDHook pointers via cache.";
                return true;
            }

            if (TryResolveAddressesMarkerOnly(mem, out musicApply, out fieldPath, out battlePath, out message))
            {
                _cachedProcessId = processId;
                _cachedMusicApply = musicApply;
                _cachedFieldPath = fieldPath;
                _cachedBattlePath = battlePath;
                return true;
            }

            // Do not overwrite cache on marker-only failures.
            return false;
        }

        private static void WriteFixedString(ProcessMemory mem, nint address, string? value, int maxBytes)
        {
            var bytes = Array.Empty<byte>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                var normalized = value.Trim();
                bytes = Encoding.UTF8.GetBytes(normalized);
                if (bytes.Length >= maxBytes)
                {
                    bytes = bytes.Take(maxBytes - 1).ToArray();
                }
            }

            var buffer = new byte[maxBytes];
            if (bytes.Length > 0)
            {
                Buffer.BlockCopy(bytes, 0, buffer, 0, bytes.Length);
            }
            buffer[Math.Min(bytes.Length, maxBytes - 1)] = 0;

            mem.WriteBytes(address, buffer);
        }

        private static bool TryResolveAddresses(ProcessMemory mem, out nint musicApply, out nint fieldPath, out nint battlePath, out string message)
        {
            musicApply = 0;
            fieldPath = 0;
            battlePath = 0;

            // Preferred: scan for marker strings and read pointers at +0x20
            if (TryResolvePointerFromMarker(mem, "MUSIC_APPLY", out musicApply)
                && TryResolvePointerFromAnyMarker(mem, new[] { "FIELD_PATH", "FIELD_MUSIC" }, out fieldPath)
                && TryResolvePointerFromAnyMarker(mem, new[] { "BATTLE_PATH", "BATTLE_MUSIC" }, out battlePath))
            {
                message = "Resolved SCDHook pointers via marker scan.";
                return true;
            }

            // Fallback: base + offsets (may differ across versions; use only if marker scan fails)
            if (mem.TryGetMainModuleBase(out var baseAddr))
            {
                musicApply = baseAddr + (nint)FallbackMusicApplyOffset;
                fieldPath = baseAddr + (nint)FallbackFieldPathOffset;
                battlePath = baseAddr + (nint)FallbackBattlePathOffset;

                message = "Resolved SCDHook pointers via fallback EXE offsets.";
                return true;
            }

            message = "Unable to resolve SCDHook pointers (marker scan and fallback base+offset failed).";
            return false;
        }

        private static bool TryResolveAddressesMarkerOnly(ProcessMemory mem, out nint musicApply, out nint fieldPath, out nint battlePath, out string message)
        {
            musicApply = 0;
            fieldPath = 0;
            battlePath = 0;

            if (TryResolvePointerFromMarker(mem, "MUSIC_APPLY", out musicApply)
                && TryResolvePointerFromAnyMarker(mem, new[] { "FIELD_PATH", "FIELD_MUSIC" }, out fieldPath)
                && TryResolvePointerFromAnyMarker(mem, new[] { "BATTLE_PATH", "BATTLE_MUSIC" }, out battlePath))
            {
                message = "Resolved SCDHook pointers via marker scan.";
                return true;
            }

            message = "Waiting for KH2 markers...";
            return false;
        }

        private static bool TryResolvePointerFromAnyMarker(ProcessMemory mem, IEnumerable<string> markers, out nint targetAddress)
        {
            targetAddress = 0;
            foreach (var marker in markers)
            {
                if (TryResolvePointerFromMarker(mem, marker, out targetAddress))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolvePointerFromMarker(ProcessMemory mem, string marker, out nint targetAddress)
        {
            targetAddress = 0;
            var markerBytes = Encoding.ASCII.GetBytes(marker);
            if (!mem.TryFindAsciiBytes(markerBytes, out var markerAddress))
            {
                return false;
            }

            var ptrLocation = markerAddress + PointerOffsetFromMarkerString;
            if (!mem.TryReadIntPtr(ptrLocation, out var ptrValue) || ptrValue == 0)
            {
                return false;
            }

            targetAddress = ptrValue;
            return true;
        }

        public readonly record struct Kh2HookResult(bool Success, string Message);

        private sealed class ProcessMemory : IDisposable
        {
            private const uint PROCESS_QUERY_INFORMATION = 0x0400;
            private const uint PROCESS_VM_READ = 0x0010;
            private const uint PROCESS_VM_WRITE = 0x0020;
            private const uint PROCESS_VM_OPERATION = 0x0008;

            private const uint MEM_COMMIT = 0x1000;
            private const uint PAGE_GUARD = 0x100;
            private const uint PAGE_NOACCESS = 0x01;

            private readonly IntPtr _handle;
            private readonly Process _process;

            private ProcessMemory(Process process, IntPtr handle)
            {
                _process = process;
                _handle = handle;
            }

            public static ProcessMemory Open(Process process)
            {
                var handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION, false, process.Id);
                if (handle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open KH2 process.");
                }
                return new ProcessMemory(process, handle);
            }

            public bool TryGetMainModuleBase(out nint baseAddress)
            {
                baseAddress = 0;
                try
                {
                    var module = _process.MainModule;
                    if (module?.BaseAddress != null)
                    {
                        baseAddress = module.BaseAddress;
                        return baseAddress != 0;
                    }
                }
                catch
                {
                    // ignore
                }

                try
                {
                    var module = _process.Modules.Cast<ProcessModule?>().FirstOrDefault();
                    if (module?.BaseAddress != null)
                    {
                        baseAddress = module.BaseAddress;
                        return baseAddress != 0;
                    }
                }
                catch
                {
                    // ignore
                }

                return false;
            }

            public void WriteByte(nint address, byte value)
            {
                WriteBytes(address, new[] { value });
            }

            public void WriteBytes(nint address, byte[] bytes)
            {
                if (bytes.Length == 0) return;

                if (!WriteProcessMemory(_handle, address, bytes, bytes.Length, out var written) || written != bytes.Length)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to write process memory.");
                }
            }

            public bool TryReadIntPtr(nint address, out nint value)
            {
                value = 0;
                var size = IntPtr.Size;
                var buffer = new byte[size];
                if (!ReadProcessMemory(_handle, address, buffer, buffer.Length, out var read) || read != buffer.Length)
                {
                    return false;
                }

                value = size == 8
                    ? (nint)BitConverter.ToInt64(buffer, 0)
                    : (nint)BitConverter.ToInt32(buffer, 0);

                return true;
            }

            public bool TryReadBytes(nint address, Span<byte> buffer)
            {
                if (buffer.Length == 0) return true;
                if (!ReadProcessMemory(_handle, address, buffer, out var bytesRead))
                {
                    return false;
                }
                return bytesRead == buffer.Length;
            }

            public bool TryFindAsciiBytes(byte[] needle, out nint address)
            {
                address = 0;
                if (needle.Length == 0) return false;

                nint current = 0;
                var mbiSize = Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();

                while (true)
                {
                    var res = VirtualQueryEx(_handle, current, out var mbi, (nuint)mbiSize);
                    if (res == 0)
                    {
                        break;
                    }

                    var regionBase = (nint)mbi.BaseAddress;
                    var regionSize = (nint)mbi.RegionSize;

                    var isCommitted = mbi.State == MEM_COMMIT;
                    var isReadable = (mbi.Protect & PAGE_NOACCESS) == 0 && (mbi.Protect & PAGE_GUARD) == 0;

                    if (isCommitted && isReadable && regionSize > 0)
                    {
                        if (TrySearchRegion(regionBase, regionSize, needle, out address))
                        {
                            return true;
                        }
                    }

                    // advance
                    var next = regionBase + regionSize;
                    if (next <= current) break;
                    current = next;
                }

                return false;
            }

            private bool TrySearchRegion(nint baseAddress, nint regionSize, byte[] needle, out nint foundAt)
            {
                foundAt = 0;

                const int chunkSize = 1024 * 1024; // 1MB
                var overlap = Math.Max(needle.Length - 1, 0);
                var buffer = new byte[chunkSize + overlap];

                nint offset = 0;
                var carry = 0;

                while (offset < regionSize)
                {
                    var toRead = (int)Math.Min(chunkSize, regionSize - offset);

                    // shift overlap from previous chunk
                    if (carry > 0)
                    {
                        Buffer.BlockCopy(buffer, chunkSize, buffer, 0, carry);
                    }

                    if (!ReadProcessMemory(_handle, baseAddress + offset, buffer.AsSpan(carry, toRead), out var bytesRead) || bytesRead <= 0)
                    {
                        offset += toRead;
                        carry = 0;
                        continue;
                    }

                    var span = buffer.AsSpan(0, carry + bytesRead);
                    var idx = span.IndexOf(needle);
                    if (idx >= 0)
                    {
                        foundAt = baseAddress + offset - carry + idx;
                        return true;
                    }

                    carry = overlap;
                    if (carry > span.Length) carry = 0;
                    if (carry > 0)
                    {
                        Buffer.BlockCopy(buffer, span.Length - carry, buffer, chunkSize, carry);
                    }

                    offset += bytesRead;
                }

                return false;
            }

            public void Dispose()
            {
                CloseHandle(_handle);
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MEMORY_BASIC_INFORMATION
            {
                public nuint BaseAddress;
                public nuint AllocationBase;
                public uint AllocationProtect;
                public nuint RegionSize;
                public uint State;
                public uint Protect;
                public uint Type;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(IntPtr hObject);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern nuint VirtualQueryEx(IntPtr hProcess, nint lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, nuint dwLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool ReadProcessMemory(IntPtr hProcess, nint lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

            private static bool ReadProcessMemory(IntPtr hProcess, nint lpBaseAddress, Span<byte> buffer, out int bytesRead)
            {
                bytesRead = 0;
                var tmp = new byte[buffer.Length];
                if (!ReadProcessMemory(hProcess, lpBaseAddress, tmp, tmp.Length, out bytesRead) || bytesRead <= 0)
                {
                    return false;
                }
                tmp.AsSpan(0, Math.Min(bytesRead, buffer.Length)).CopyTo(buffer);
                return true;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool WriteProcessMemory(IntPtr hProcess, nint lpBaseAddress, [In] byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);
        }
    }
}
