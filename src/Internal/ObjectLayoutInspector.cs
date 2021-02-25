﻿
/*

MIT License

Copyright (c) 2017 Sergey Teplyakov

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

 * */

using Apex.Runtime.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace ObjectLayoutInspector
{
    /// <summary>
    /// Provides helper methods for inspecting type layouts.
    /// </summary>
    public static class TypeInspector
    {
        private static readonly Dictionary<Type, int> SupportedPrimitiveTypesAndSizes = new Dictionary<Type, int>
        {
            { typeof(Int16), sizeof(Int16) },
            { typeof(UInt16), sizeof(UInt16) },
            { typeof(Int32), sizeof(Int32) },
            { typeof(UInt32), sizeof(UInt32) },
            { typeof(Int64), sizeof(Int64) },
            { typeof(UInt64), sizeof(UInt64) },
            { typeof(Double), sizeof(Double) },
            { typeof(Decimal), sizeof(Decimal) },
            { typeof(Guid), 16 }
        };

        /// <summary>
        /// Returns an instance size and the overhead for a given type.
        /// </summary>
        /// <remarks>
        /// If <paramref name="type"/> is value type then the overhead is 0.
        /// Otherwise the overhead is 2 * PtrSize.
        /// </remarks>
        public static (int size, int overhead) GetSize(Type type)
        {
            if (type.IsValueType)
            {
                if (type.IsPrimitive && SupportedPrimitiveTypesAndSizes.TryGetValue(type, out var primitiveSize))
                {
                    return (size: primitiveSize, overhead: 0);
                }

                return (size: GetSizeOfValueTypeInstance(type), overhead: 0);
            }

            var size = GetSizeOfReferenceTypeInstance(type);
            return (size, overhead: 2 * IntPtr.Size);
        }

        /// <summary>
        /// Return s the size of a reference type instance excluding the overhead.
        /// </summary>
        private static int GetSizeOfReferenceTypeInstance(Type type)
        {
            Debug.Assert(!type.IsValueType);

            var (instance, success) = TryCreateInstanceSafe(type);
            if (success)
                return (int)CSharpRuntimeHelpers.GetRawObjectDataSize(instance);

            return IntPtr.Size;
            /*
            var fields = GetFieldOffsets(type);

            if (fields.Length == 0)
            {
                // Special case: the size of an empty class is 1 Ptr size
                return IntPtr.Size;
            }

            // The size of the reference type is computed in the following way:
            // MaxFieldOffset + SizeOfThatField
            // and round that number to closest point size boundary
            var maxValue = fields.MaxBy(tpl => tpl.offset);
            int sizeCandidate = maxValue.offset + GetFieldSize(maxValue.fieldInfo.FieldType);

            // Rounding this stuff to the nearest ptr-size boundary
            int roundTo = IntPtr.Size - 1;
            return (sizeCandidate + roundTo) & (~roundTo);
            */
        }

        /// <summary>
        /// Returns the size of the field if the field would be of type <paramref name="t"/>.
        /// </summary>
        /// <remarks>
        /// For reference types the size is always a PtrSize.
        /// </remarks>
        private static int GetFieldSize(Type t)
        {
            if (t.IsValueType)
            {
                return GetSizeOfValueTypeInstance(t);
            }

            return IntPtr.Size;
        }

        /// <summary>
        /// Computes size for <paramref name="type"/>.
        /// </summary>
        public static int GetSizeOfValueTypeInstance(Type type)
        {
            Debug.Assert(type.IsValueType);

            var (instance, success) = TryCreateInstanceSafe(type);
            if (success)
                return (int)CSharpRuntimeHelpers.GetRawObjectDataSize(instance);
                //return (int)CSharpRuntimeHelpers.HeapSize(instance);

            return 0;
            /*
            var generatedType = typeof(SizeComputer<>).MakeGenericType(type);
            // The offset of the second field is the size of the 'type'
            var fieldsOffsets = GetFieldOffsets(generatedType);
            return fieldsOffsets[1].offset;
            */
        }

        /// <summary>
        /// Helper struct that is used for computing the size of a struct.
        /// </summary>
        struct SizeComputer<T>
        {
            // Both fields should be of the same type because the CLR can rearrange the struct and 
            // the offset of the second field would be the offset of the 'dummyField' not the offset of the 'offset' field.
            public T dummyField;
            public T offset;

            public SizeComputer(T dummyField, T offset) => (this.dummyField, this.offset) = (dummyField, offset);
        }

        /// <summary>
        /// Gets an array of field information with their offsets for a given <paramref name="t"/>.
        /// </summary>
        private static (FieldInfo fieldInfo, int offset)[] GetFieldOffsets(Type t)
        {
            // GetFields does not return private fields from the base types.
            // Need to use a custom helper function.
            var fields = TypeFields.GetFields(t);
            //var fields2 = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

            var (instance, success) = TryCreateInstanceSafe(t);
            if (!success)
            {
                return Array.Empty<(FieldInfo, int)>();
            }

            var addresses = FieldOffsetInspection(instance!);

            if (addresses.Length <= 1)
            {
                return Array.Empty<(FieldInfo, int)>();
            }

            var baseLine = GetBaseLine(addresses[0]);

            // Converting field addresses to offsets using the first field as a baseline
            return fields
                .Select((field, index) => (field: field, offset: (int)(addresses[index + 1] - baseLine)))
                .OrderBy(tpl => tpl.offset)
                .ToArray();

            long GetBaseLine(long referenceAddress) => t.IsValueType ? referenceAddress : referenceAddress + IntPtr.Size;
        }

        private static long[] FieldOffsetInspection(object obj)
        {
            return Array.Empty<long>();
            /*
            var method = new DynamicMethod(
                name: "GetFieldOffsets",
                returnType: typeof(long[]),
                parameterTypes: new[] { typeof(object) },
                m: typeof(TypeInspector).Module,
                skipVisibility: true);

            ILGenerator ilGen = method.GetILGenerator();

            // Declaring local variable of type long[]
            ilGen.DeclareLocal(typeof(long[]));
            // Loading array size onto evaluation stack
            ilGen.Emit(OpCodes.Ldc_I4, fields.Count + 1);

            // Creating an array and storing it into the local
            ilGen.Emit(OpCodes.Newarr, typeof(long));
            ilGen.Emit(OpCodes.Stloc_0);

            // Loading the local with an array
            ilGen.Emit(OpCodes.Ldloc_0);

            // Loading an index of the array where we're going to store the element
            ilGen.Emit(OpCodes.Ldc_I4, 0);

            // Loading object instance onto evaluation stack
            ilGen.Emit(OpCodes.Ldarg_0);

            // Converting reference to long
            ilGen.Emit(OpCodes.Conv_I8);

            // Storing the reference in the array
            ilGen.Emit(OpCodes.Stelem_I8);

            for (int i = 0; i < fields.Count; i++)
            {
                // Loading the local with an array
                ilGen.Emit(OpCodes.Ldloc_0);

                // Loading an index of the array where we're going to store the element
                ilGen.Emit(OpCodes.Ldc_I4, i + 1);

                // Loading object instance onto evaluation stack
                ilGen.Emit(OpCodes.Ldarg_0);

                // Getting the address for a given field
                ilGen.Emit(OpCodes.Ldflda, fields[i]);

                // Converting field offset to long
                ilGen.Emit(OpCodes.Conv_I8);

                // Storing the offset in the array
                ilGen.Emit(OpCodes.Stelem_I8);
            }

            ilGen.Emit(OpCodes.Ldloc_0);
            ilGen.Emit(OpCodes.Ret);

            return (Func<object, long[]>)method.CreateDelegate(typeof(Func<object, long[]>));
            */
        }

        /// <summary>
        /// Tries to create an instance of a given type.
        /// </summary>
        /// <remarks>
        /// There is a limit of what types can be instantiated.
        /// The following types are not supported by this function:
        /// * Open generic types like <code>typeof(List&lt;&gt;)</code>
        /// * Abstract types
        /// </remarks>
        private static (object? result, bool success) TryCreateInstanceSafe(Type t)
        {
            if (!CanCreateInstance(t))
            {
                return (result: null, success: false);
            }

            // Value types are handled separately
            if (t.IsValueType)
            {
                return Success(Activator.CreateInstance(t));
            }

            // String is handled separately as well due to security restrictions
            if (t == typeof(string))
            {
                return Success(string.Empty);
            }

            // It is actually possible that GetUnitializedObject will return null.
            // I've got null for some security related types.
            return Success(GetUninitializedObject(t));

            static (object? result, bool success) Success(object? o) => (o, o != null);
        }

        private static object? GetUninitializedObject(Type t)
        {
            try
            {
                var result = FormatterServices.GetUninitializedObject(t);
                GC.SuppressFinalize(result);
                return result;
            }
            catch (TypeInitializationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true if the instance of type <paramref name="t"/> can be instantiated.
        /// </summary>
        public static bool CanCreateInstance(this Type t)
        {
            // Abstract types and generics are not supported
            if (t.IsAbstract || IsOpenGenericType(t) || t.IsCOMObject)
            {
                return false;
            }

            // TODO: check where ArgIterator is located
            if (// t == typeof(ArgIterator) || 
                t == typeof(RuntimeArgumentHandle) || t == typeof(TypedReference) || t.Name == "Void"
                || t == typeof(IsVolatile) || t == typeof(RuntimeFieldHandle) || t == typeof(RuntimeMethodHandle) ||
                t == typeof(RuntimeTypeHandle))
            {
                // This is a special type
                return false;
            }

            if (t.BaseType == typeof(ContextBoundObject))
            {
                return false;
            }

            return true;
            static bool IsOpenGenericType(Type type)
            {
                return type.IsGenericTypeDefinition && !type.IsConstructedGenericType;
            }
        }
    }

    internal static class EnumerableExtensions
    {
        public static T MaxBy<T>(this IEnumerable<T> sequence, Func<T, int> selector)
        {
            bool firstElement = false;
            T maxValue = default!;
            foreach (T e in sequence)
            {
                if (!firstElement)
                {
                    firstElement = true;
                    maxValue = e;
                }
                else
                {
                    int currentMax = selector(maxValue);
                    int maxCandidate = selector(e);

                    if (Math.Max(currentMax, maxCandidate) == maxCandidate)
                    {
                        maxValue = e;
                    }
                }
            }

            return maxValue;
        }
    }
}