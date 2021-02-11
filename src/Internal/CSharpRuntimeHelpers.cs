using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Apex.Runtime.Internal
{
    public static class CSharpRuntimeHelpers
    {
        private static readonly MethodInfo GetRawObjectDataSizeMethod;
        private static MethodInfo GetMethodTableMethod;

        static CSharpRuntimeHelpers()
        {
            GetRawObjectDataSizeMethod = typeof(RuntimeHelpers).GetMethod("GetRawObjectDataSize", BindingFlags.Static | BindingFlags.NonPublic)!;
            GetMethodTableMethod = typeof(RuntimeHelpers).GetMethod("GetMethodTable", BindingFlags.Static | BindingFlags.NonPublic)!;
        }

        public static ulong GetRawObjectDataSize(object obj)
        {
            var intPtr = (UIntPtr)GetRawObjectDataSizeMethod.Invoke(null, new []{ obj })!;
            return (ulong)intPtr;
        }

		private static (uint baseSize, int componentSize) ReadMethodTableSizes(object obj)
		{
			unsafe
			{
				var result = (Pointer)GetMethodTableMethod.Invoke(null, new [] { obj })!;
				var methodTable = (MethodTable*) Pointer.Unbox(result);
				return (methodTable->BaseSize, methodTable->ComponentSize);
			}
		}

        internal static bool IsString<T>() => typeof(T) == typeof(string);

        internal static bool IsString<T>(T value) => value is string;

        internal static bool IsArray<T>() => typeof(T).IsArray || typeof(T) == typeof(Array);

        internal static bool IsArray<T>(T value) => value is Array;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long HeapSize<T>(T value) where T : class
		{
			// Sanity check
			//Conditions.Require(!Runtime.Info.IsStruct(value));
			if (value is null)
			{
				return 0;
			}

			// TODO
			if (value?.GetType().IsValueType == true)
			{
				return 0;
			}


			// By manually reading the MethodTable*, we can calculate the size correctly if the reference
			// is boxed or cloaked
			//var methodTable = ReadMetaType(value);
			var (baseSize, componentSize) = ReadMethodTableSizes(value);

			// Value of GetSizeField()
			int length = 0;

			/**
			 * Type			x86 size				x64 size
			 *
			 * object		12						24
			 * object[]		16 + length * 4			32 + length * 8
			 * int[]		12 + length * 4			28 + length * 4
			 * byte[]		12 + length				24 + length
			 * string		14 + length * 2			26 + length * 2
			 */

			// From object.h line 65:

			/* 	  The size of the object in the heap must be able to be computed
			 *    very, very quickly for GC purposes.   Restrictions on the layout
			 *    of the object guarantee this is possible.
			 *
			 *    Any object that inherits from Object must be able to
			 *    compute its complete size by using the first 4 bytes of
			 *    the object following the Object part and constants
			 *    reachable from the MethodTable...
			 *
			 *    The formula used for this calculation is:
			 *        MT->GetBaseSize() + ((OBJECTTYPEREF->GetSizeField() * MT->GetComponentSize())
			 *
			 *    So for Object, since this is of fixed size, the ComponentSize is 0, which makes the right side
			 *    of the equation above equal to 0 no matter what the value of GetSizeField(), so the size is just the base size.
			 *
			 */

			if (IsArray(value))
			{
				var arr = value as Array;

				// ReSharper disable once PossibleNullReferenceException
				// We already know it's not null because the type is an array.
				length = arr?.Length ?? 1;
			}
			else if (IsString(value))
			{
				var str = value as string;

				length = str?.Length ?? 1;
			}

			return baseSize + length * componentSize;
		}
    }

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