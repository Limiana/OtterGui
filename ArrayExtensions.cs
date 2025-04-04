namespace OtterGui;

public static class ArrayExtensions
{
    /// <summary> Iterate over enumerables with additional index. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static IEnumerable<(T Value, int Index)> WithIndex<T>(this IEnumerable<T> list)
        => list.Select((x, i) => (x, i));

    /// <summary> Remove an added index from an indexed enumerable. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static IEnumerable<T> WithoutIndex<T>(this IEnumerable<(T Value, int Index)> list)
        => list.Select(x => x.Value);

    /// <summary> Remove the value and only keep the index from an indexed enumerable. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static IEnumerable<int> WithoutValue<T>(this IEnumerable<(T Value, int Index)> list)
        => list.Select(x => x.Index);


    // Find the index of the first object fulfilling predicate's criteria in the given list.
    // Returns -1 if no such object is found.
    public static int IndexOf<T>(this IEnumerable<T> array, Predicate<T> predicate)
    {
        var i = 0;
        foreach (var obj in array)
        {
            if (predicate(obj))
                return i;

            ++i;
        }

        return -1;
    }

    // Find the index of the first occurrence of needle in the given list.
    // Returns -1 if needle is not contained in the list.
    public static int IndexOf<T>(this IEnumerable<T> array, T needle) where T : notnull
    {
        var i = 0;
        foreach (var obj in array)
        {
            if (needle.Equals(obj))
                return i;

            ++i;
        }

        return -1;
    }

    // Find the first object fulfilling predicate's criteria in the given list, if one exists.
    // Returns true if an object is found, false otherwise.
    public static bool FindFirst<T>(this IEnumerable<T> array, Predicate<T> predicate, [NotNullWhen(true)] out T? result)
    {
        foreach (var obj in array)
        {
            if (predicate(obj))
            {
                result = obj!;
                return true;
            }
        }

        result = default;
        return false;
    }

    // Find the first occurrence of needle in the given list and return the value contained in the list in result.
    // Returns true if an object is found, false otherwise.
    public static bool FindFirst<T>(this IEnumerable<T> array, T needle, [NotNullWhen(true)] out T? result) where T : notnull
    {
        foreach (var obj in array)
        {
            if (obj.Equals(needle))
            {
                result = obj;
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary> Wrapper for optional selection. </summary>
    public static IEnumerable<TOut> SelectWhere<TIn, TOut>(this IEnumerable<TIn> enumerable, Func<TIn, (bool, TOut?)> filterMap)
        => enumerable.Select(filterMap).Where(p => p.Item1).Select(p => p.Item2!);

    // Find the first object fulfilling predicate's criteria in the given Span, if one exists.
    // Returns true if an object is found, false otherwise.
    public static bool FindFirst<T>(this Span<T> array, Predicate<T> predicate, [NotNullWhen(true)] out T? result)
        => ((ReadOnlySpan<T>)array).FindFirst(predicate, out result);

    // Find the first object fulfilling predicate's criteria in the given ReadOnlySpan, if one exists.
    // Returns true if an object is found, false otherwise.
    public static bool FindFirst<T>(this ReadOnlySpan<T> array, Predicate<T> predicate, [NotNullWhen(true)] out T? result)
    {
        foreach (var obj in array)
        {
            if (predicate(obj))
            {
                result = obj!;
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary> Write a byte span as a list of hexadecimal bytes separated by spaces. </summary>
    public static string WriteHexBytes(this ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        for (var i = 0; i < bytes.Length - 1; ++i)
            sb.Append($"{bytes[i]:X2} ");
        sb.Append($"{bytes[^1]:X2}");
        return sb.ToString();
    }

    /// <inheritdoc cref="WriteHexBytes(ReadOnlySpan{byte})"/>
    public static string WriteHexBytes(this Span<byte> bytes)
        => ((ReadOnlySpan<byte>)bytes).WriteHexBytes();

    /// <summary> Write only the difference of a byte span as a list of hexadecimal bytes separated by spaces, keeping equal bytes as double spaces. </summary>
    public static string WriteHexByteDiff(this ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> diff)
    {
        var shorter = Math.Min(bytes.Length, diff.Length);
        var sb      = new StringBuilder(shorter * 3);
        for (var i = 0; i < shorter - 1; ++i)
        {
            var d = (byte) (bytes[i] ^ diff[i]);
            if (d == 0)
                sb.Append("   ");
            else
                sb.Append($"{d:X2} ");
        }

        var last = (byte) (bytes[shorter - 1] ^ diff[shorter - 1]);
        if (last == 0)
            sb.Append("   ");
        else
            sb.Append($"{last:X2}");
        return sb.ToString();
    }

    /// <inheritdoc cref="WriteHexByteDiff(ReadOnlySpan{byte},ReadOnlySpan{byte})"/>
    public static string WriteHexByteDiff(this Span<byte> bytes, ReadOnlySpan<byte> diff)
        => ((ReadOnlySpan<byte>)bytes).WriteHexByteDiff(diff);
}
