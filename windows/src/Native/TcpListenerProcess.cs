#nullable enable
using System;
using System.Net;
using System.Runtime.InteropServices;

namespace YTray.Native
{
    internal static class TcpListenerProcess
    {
        private const int AddressFamilyInet = 2;
        private const int OwnerPidListenerTable = 3;
        private const uint ErrorInsufficientBuffer = 122;
        private const int RowSize = 24;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size,
            [MarshalAs(UnmanagedType.Bool)] bool order, int addressFamily, int tableClass, uint reserved);

        internal static int FindOwnerProcessId(int port)
        {
            if (port < 1 || port > 65535) return 0;
            var size = 0;
            if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, AddressFamilyInet,
                    OwnerPidListenerTable, 0) != ErrorInsufficientBuffer || size <= sizeof(int)) return 0;

            var table = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(table, ref size, false, AddressFamilyInet,
                        OwnerPidListenerTable, 0) != 0) return 0;
                var count = Marshal.ReadInt32(table);
                for (var index = 0; index < count; index++)
                {
                    var row = IntPtr.Add(table, sizeof(int) + index * RowSize);
                    var localPort = unchecked((ushort)IPAddress.NetworkToHostOrder(
                        unchecked((short)Marshal.ReadInt32(row, 8))));
                    if (localPort == port) return Marshal.ReadInt32(row, 20);
                }
                return 0;
            }
            finally { Marshal.FreeHGlobal(table); }
        }
    }
}
