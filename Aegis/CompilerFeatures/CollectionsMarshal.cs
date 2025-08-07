using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices2;

namespace System.Runtime.InteropServices2
{
    public static class CollectionsMarshal<T>
    {
        //[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_items")]
        //extern static ref T[] PullBuffer(List<T> @this);

        private static FieldInfo ItemsField = typeof(List<T>).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);

        public static T[] PullBuffer(List<T> instance)
        {
            return (T[])ItemsField.GetValue(instance);
        }

        /// <summary>
        /// Get a <see cref="Span{T}"/> view over a <see cref="List{T}"/>'s data.
        /// Items should not be added or removed from the <see cref="List{T}"/> while the <see cref="Span{T}"/> is in use.
        /// </summary>
        /// <param name="list">The list to get the data view over.</param>
        /// <typeparam name="T">The type of the elements in the list.</typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Span<T> AsSpan(List<T>? list)
        {
            Span<T> span = default;

            if (list is not null)
            {
                var buffer = PullBuffer(list);
                span = buffer.AsSpan();
            }

            return span;
        }
    }
}

namespace Aegis
{
    public static class ListExtensions
    {
        public static T[] GetArray<T>(this List<T> list)
            => CollectionsMarshal<T>.PullBuffer(list);

        public static Span<T> AsSpan<T>(this List<T>? list)
            => CollectionsMarshal<T>.AsSpan(list);

        public static Memory<T> AsMemory<T>(this List<T> list)
            => list.GetArray().AsMemory(0, list.Count);
    }
}