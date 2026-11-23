namespace FleetBridge;

/// <summary>
/// UTF-16 buffer helpers matching the C++ bridge's wstr.cpp semantics.
/// MQL passes strings as NUL-terminated UTF-16 pointers and receives strings
/// through caller-allocated buffers.
/// </summary>
internal static unsafe class Wstr
{
    /// <summary>Reads a NUL-terminated UTF-16 string; null becomes "".</summary>
    public static string FromUtf16(ushort* p)
    {
        if (p == null)
        {
            return string.Empty;
        }
        int len = 0;
        while (p[len] != 0)
        {
            len++;
        }
        return new string((char*)p, 0, len);
    }

    /// <summary>
    /// Copies <paramref name="s"/> into a caller-supplied UTF-16 buffer,
    /// truncating to capacity-1 units and always NUL-terminating. Returns the
    /// number of UTF-16 units written (excluding the terminator), or 0 when
    /// the buffer is null/empty — same contract as the C++ write_utf16.
    /// </summary>
    public static int WriteUtf16(string s, ushort* buf, int capacity)
    {
        if (buf == null || capacity <= 0)
        {
            return 0;
        }
        int written = Math.Min(s.Length, capacity - 1);
        for (int i = 0; i < written; i++)
        {
            buf[i] = s[i];
        }
        buf[written] = 0;
        return written;
    }
}
