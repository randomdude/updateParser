using System;
using System.Runtime.InteropServices;

namespace updatequery
{
    public class interop
    {
        [DllImport("msdelta.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        public static extern UInt32 GetDeltaInfoB(DELTA_INPUT Delta, IntPtr lpHeaderInfo);

        [DllImport("msdelta.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        public static extern UInt32 ApplyDeltaW(UInt64 ApplyFlags, string src, string delta, string target);

        [StructLayout(LayoutKind.Explicit)]
        public class DELTA_HEADER_INFO
        {
            [FieldOffset(0x00)]
            public UInt64 FileTypeSet;

            [FieldOffset(0x08)]
            public UInt64 FileType;

            [FieldOffset(0x10)]
            public UInt64 Flags;

            [FieldOffset(0x18)]
            public UInt64 TargetSize;

            [FieldOffset(0x20)]
            public UInt32 TargetFileTimeLow;

            [FieldOffset(0x24)]
            public UInt32 TargetFileTimeHigh;

            [FieldOffset(0x28)]
            public UInt32 TargetHashAlgId;

            [FieldOffset(0x2C)]
            public Byte TargetHashA;
            [FieldOffset(0x2D)]
            public Byte TargetHashB;
            [FieldOffset(0x2E)]
            public Byte TargetHashC;
            [FieldOffset(0x2F)]
            public Byte TargetHashD;
            
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 32-4)]
            [FieldOffset(0x30)]
            public Byte[] TargetHash;
        }

        [StructLayout(LayoutKind.Explicit, Size =  0x18)]
        public class DELTA_INPUT
        {
            [FieldOffset(0x00)]
            public IntPtr lpStart;

            [FieldOffset(0x08)]
            public UInt64 uSize;

            [FieldOffset(0x10)]
            public bool Editable;
        }

    }
}