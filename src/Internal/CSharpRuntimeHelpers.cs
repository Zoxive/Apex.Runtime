using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Apex.Runtime.Internal
{
    public static class CSharpRuntimeHelpers
    {
        private static bool IsRunningOnMono { get; }

        static CSharpRuntimeHelpers()
        {
            IsRunningOnMono = Type.GetType("Mono.Runtime") != null;
        }

        internal static ref byte GetRawData(this object obj) => ref Unsafe.As<RawData>(obj)!.Data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe MethodTable* GetMethodTable(object obj)
        {
            var offset = IsRunningOnMono ? -2 : -1;
            return (MethodTable*) Unsafe.Add(ref Unsafe.As<byte, IntPtr>(ref obj.GetRawData()), offset);
        }

        // Taken from https://github.com/dotnet/runtime/blob/c5f3704cb1f98c9b133d547011241d1ee0b4694b/src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs#L227
        public static ulong GetRawObjectDataSize(object obj)
        {
            unsafe
            {
                MethodTable* methodTablePointer = GetMethodTable(obj);

                // See comment on RawArrayData for details
                nuint rawSize = methodTablePointer->BaseSize - (nuint)(2 * sizeof(IntPtr));
                if (methodTablePointer->HasComponentSize)
                    rawSize += (uint)Unsafe.As<RawArrayData>(obj)!.Length * (nuint)methodTablePointer->ComponentSize;

                GC.KeepAlive(obj); // Keep MethodTable alive

                return rawSize;
            }
        }
    }

    // Helper class to assist with unsafe pinning of arbitrary objects.
    // It's used by VM code.
    internal class RawData
    {
        public byte Data;
    }

    // CLR arrays are laid out in memory as follows (multidimensional array bounds are optional):
    // [ sync block || pMethodTable || num components || MD array bounds || array data .. ]
    //                 ^               ^                 ^                  ^ returned reference
    //                 |               |                 \-- ref Unsafe.As<RawArrayData>(array).Data
    //                 \-- array       \-- ref Unsafe.As<RawData>(array).Data
    // The BaseSize of an array includes all the fields before the array data,
    // including the sync block and method table. The reference to RawData.Data
    // points at the number of components, skipping over these two pointer-sized fields.
#pragma warning disable 649
    internal class RawArrayData
    {
        public uint Length; // Array._numComponents padded to IntPtr
#if TARGET_64BIT
        public uint Padding;
#endif
        public byte Data;
    }
#pragma warning restore 649

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct MethodTable
    {
        [FieldOffset(0)]
        public ushort ComponentSize;
        [FieldOffset(0)]
        private uint Flags;
        [FieldOffset(4)]
        public uint BaseSize;
        [FieldOffset(0x0e)]
        public ushort InterfaceCount;
        [FieldOffset(ParentMethodTableOffset)]
        public MethodTable* ParentMethodTable;
        [FieldOffset(ElementTypeOffset)]
        public void* ElementType;
        [FieldOffset(InterfaceMapOffset)]
        public MethodTable** InterfaceMap;

        // WFLAGS_HIGH_ENUM
        private const uint enum_flag_ContainsPointers = 0x01000000;
        private const uint enum_flag_HasComponentSize = 0x80000000;
        private const uint enum_flag_HasTypeEquivalence = 0x02000000;
        // Types that require non-trivial interface cast have this bit set in the category
        private const uint enum_flag_NonTrivialInterfaceCast = 0x00080000 // enum_flag_Category_Array
                                                             | 0x40000000 // enum_flag_ComObject
                                                             | 0x00400000 // enum_flag_ICastable;
                                                             | 0x00200000;// enum_flag_IDynamicInterfaceCastable;

        private const int DebugClassNamePtr = // adjust for debug_m_szClassName
#if DEBUG
#if TARGET_64BIT
            8
#else
            4
#endif
#else
            0
#endif
            ;

        private const int ParentMethodTableOffset = 0x10 + DebugClassNamePtr;

#if TARGET_64BIT
        private const int ElementTypeOffset = 0x30 + DebugClassNamePtr;
#else
        private const int ElementTypeOffset = 0x20 + DebugClassNamePtr;
#endif

#if TARGET_64BIT
        private const int InterfaceMapOffset = 0x38 + DebugClassNamePtr;
#else
        private const int InterfaceMapOffset = 0x24 + DebugClassNamePtr;
#endif

        public bool HasComponentSize
        {
            get
            {
                return (Flags & enum_flag_HasComponentSize) != 0;
            }
        }

        public bool ContainsGCPointers
        {
            get
            {
                return (Flags & enum_flag_ContainsPointers) != 0;
            }
        }

        public bool NonTrivialInterfaceCast
        {
            get
            {
                return (Flags & enum_flag_NonTrivialInterfaceCast) != 0;
            }
        }

        public bool HasTypeEquivalence
        {
            get
            {
                return (Flags & enum_flag_HasTypeEquivalence) != 0;
            }
        }

        public bool IsMultiDimensionalArray
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(HasComponentSize);
                // See comment on RawArrayData for details
                return BaseSize > (uint)(3 * sizeof(IntPtr));
            }
        }

        // Returns rank of multi-dimensional array rank, 0 for sz arrays
        public int MultiDimensionalArrayRank
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(HasComponentSize);
                // See comment on RawArrayData for details
                return (int)((BaseSize - (uint)(3 * sizeof(IntPtr))) / (uint)(2 * sizeof(int)));
            }
        }
    }
}