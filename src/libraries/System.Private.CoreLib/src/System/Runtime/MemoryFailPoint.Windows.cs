// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;

namespace System.Runtime
{
    public sealed partial class MemoryFailPoint
    {
        private static ulong GetTopOfMemory()
        {
            Interop.Kernel32.SYSTEM_INFO info;
            unsafe
            {
                Interop.Kernel32.GetSystemInfo(&info);
            }

            return (ulong)info.lpMaximumApplicationAddress;
        }

        private static unsafe bool CheckForAvailableMemory(out ulong availPageFile, out ulong totalAddressSpaceFree)
        {
            Interop.Kernel32.MEMORYSTATUSEX memoryStatus = default;
            memoryStatus.dwLength = (uint)sizeof(Interop.Kernel32.MEMORYSTATUSEX);
            if (!Interop.Kernel32.GlobalMemoryStatusEx(ref memoryStatus))
            {
                availPageFile = default;
                totalAddressSpaceFree = default;
                return false;
            }
            availPageFile = memoryStatus.ullAvailPageFile;
            totalAddressSpaceFree = memoryStatus.ullAvailVirtual;
            // Console.WriteLine($"Memory gate:  Mem load: {memory.memoryLoad}%  Available memory (physical + page file): {(memory.availPageFile >> 20)} MB  Total free address space: {memory.availVirtual >> 20} MB  GC Heap: {(GC.GetTotalMemory(true) >> 20)} MB");
            return true;
        }

        // Based on the shouldThrow parameter, this will throw an exception.
        // In all cases, we update our last known free address space, hopefully
        // avoiding needing to probe again.
        private static unsafe void CheckForFreeAddressSpace(ulong size, bool shouldThrow)
        {
            // Start walking the address space at 0.  VirtualAlloc may wrap
            // around the address space.  We don't need to find the exact
            // pages that VirtualAlloc would return - we just need to
            // know whether VirtualAlloc could succeed.
            ulong freeSpaceAfterGCHeap = MemFreeAfterAddress(null, size);

            // Console.WriteLine($"MemoryFailPoint: Checked for free VA space.  Found enough? {(freeSpaceAfterGCHeap >= size)}  Asked for: {size}  Found: {freeSpaceAfterGCHeap}");

            // We may set these without taking a lock - I don't believe
            // this will hurt, as long as we never increment this number in
            // the Dispose method.  If we do an extra bit of checking every
            // once in a while, but we avoid taking a lock, we may win.
            LastKnownFreeAddressSpace = (long)freeSpaceAfterGCHeap;
            LastTimeCheckingAddressSpace = Environment.TickCount;

            if (freeSpaceAfterGCHeap < size && shouldThrow)
                throw new InsufficientMemoryException(SR.InsufficientMemory_MemFailPoint_VAFrag);
        }

        // Returns the amount of consecutive free memory available in a block
        // of pages.  If we didn't have enough address space, we still return
        // a positive value < size, to help potentially avoid the overhead of
        // this check if we use a MemoryFailPoint with a smaller size next.
        private static unsafe ulong MemFreeAfterAddress(void* address, ulong size)
        {
            if (size >= s_topOfMemory)
                return 0;

            ulong largestFreeRegion = 0;
            Interop.Kernel32.MEMORY_BASIC_INFORMATION memInfo = default;
            UIntPtr sizeOfMemInfo = (UIntPtr)sizeof(Interop.Kernel32.MEMORY_BASIC_INFORMATION);

            while (((ulong)address) + size < s_topOfMemory)
            {
                UIntPtr r = Interop.Kernel32.VirtualQuery(address, ref memInfo, sizeOfMemInfo);
                if (r == UIntPtr.Zero)
                    throw Win32Marshal.GetExceptionForLastWin32Error();

                ulong regionSize = memInfo.RegionSize.ToUInt64();
                if (memInfo.State == Interop.Kernel32.MemOptions.MEM_FREE)
                {
                    if (regionSize >= size)
                        return regionSize;
                    else
                        largestFreeRegion = Math.Max(largestFreeRegion, regionSize);
                }
                address = (void*)((ulong)address + regionSize);
            }
            return largestFreeRegion;
        }

        // Allocate a specified number of bytes, commit them and free them. This should enlarge
        // page file if necessary and possible.
        private static void GrowPageFileIfNecessaryAndPossible(UIntPtr numBytes)
        {
            unsafe
            {
                void* pMemory = Interop.Kernel32.VirtualAlloc(null, numBytes, Interop.Kernel32.MemOptions.MEM_COMMIT, Interop.Kernel32.PageOptions.PAGE_READWRITE);
                if (pMemory != null)
                {
                    bool r = Interop.Kernel32.VirtualFree(pMemory, UIntPtr.Zero, Interop.Kernel32.MemOptions.MEM_RELEASE);
                    if (!r)
                        throw Win32Marshal.GetExceptionForLastWin32Error();
                }
            }
        }
    }
}
