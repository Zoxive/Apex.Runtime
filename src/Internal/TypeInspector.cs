
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Xamarin.Apex.Runtime.Internal
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

            if (TryCreateInstanceSafe(type, out var instance) && instance != null)
                return (int)CSharpRuntimeHelpers.GetRawObjectDataSize(instance);

            return IntPtr.Size;
        }

        /// <summary>
        /// Computes size for <paramref name="type"/>.
        /// </summary>
        private static int GetSizeOfValueTypeInstance(Type type)
        {
            Debug.Assert(type.IsValueType);

            if (TryCreateInstanceSafe(type, out var instance) && instance != null)
                return (int)CSharpRuntimeHelpers.GetRawObjectDataSize(instance);

            return 0;
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
        private static bool TryCreateInstanceSafe(Type t, out object? result)
        {
            if (!CanCreateInstance(t))
            {
                result = null;
                return false;
            }

            // Value types are handled separately
            if (t.IsValueType)
            {
                result = Activator.CreateInstance(t);
                return result != null;
            }

            // String is handled separately as well due to security restrictions
            if (t == typeof(string))
            {
                result = string.Empty;
                return true;
            }

            // It is actually possible that GetUnitializedObject will return null.
            // I've got null for some security related types.
            result = GetUninitializedObject(t);
            return result != null;
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
        private static bool CanCreateInstance(this Type t)
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
}