using System;

namespace SNet.ClientEngine
{
    /// <summary>
    ///     ArraySegment.Slice 메서드는 .netstandard 2.1 에서 추가된다.
    ///     유니티는 .netstandard 2.0 이라서 사용이 불가능해 추가했다.
    /// </summary>
    internal static class ArraySegmentExtensions
    {
        public static ArraySegment<T> Slice<T>(this ArraySegment<T> segment, int index)
        {
            return new ArraySegment<T>(segment.Array, segment.Offset + index, segment.Count - index);
        }

        public static ArraySegment<T> Slice<T>(this ArraySegment<T> segment, int index, int count)
        {
            return new ArraySegment<T>(segment.Array, segment.Offset + index, count);
        }
    }
}
