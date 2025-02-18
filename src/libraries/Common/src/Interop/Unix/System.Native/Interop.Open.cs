// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static partial class Interop
{
    internal static partial class Sys
    {
        [GeneratedDllImport(Libraries.SystemNative, EntryPoint = "SystemNative_Open", SetLastError = true, CharSet = CharSet.Ansi)]
        internal static partial SafeFileHandle Open(string filename, OpenFlags flags, int mode);
    }
}
