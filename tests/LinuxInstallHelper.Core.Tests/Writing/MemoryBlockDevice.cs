using LinuxInstallHelper.Core.Writing;

namespace LinuxInstallHelper.Core.Tests.Writing;

/// <summary>In-memory disk that enforces sector alignment like unbuffered Windows I/O.</summary>
public sealed class MemoryBlockDevice(long size, int sectorSize = 512) : IBlockDevice
{
    public byte[] Data { get; } = CreateFilled(size);

    public int SectorSize { get; } = sectorSize;

    public long Size => Data.LongLength;

    public bool Flushed { get; private set; }

    public bool Disposed { get; private set; }

    public bool Opened { get; set; }

    /// <summary>Bytes to corrupt after writing, to simulate a faulty drive.</summary>
    public long? CorruptAt { get; set; }

    public void Write(long offset, ReadOnlySpan<byte> data)
    {
        CheckAlignment(offset, data.Length);
        data.CopyTo(Data.AsSpan((int)offset));
        if (CorruptAt is long at && at >= offset && at < offset + data.Length)
        {
            Data[at] ^= 0xFF;
        }
    }

    public void Read(long offset, Span<byte> buffer)
    {
        CheckAlignment(offset, buffer.Length);
        Data.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
    }

    public void Flush() => Flushed = true;

    public void Dispose() => Disposed = true;

    private void CheckAlignment(long offset, int length)
    {
        if (offset % SectorSize != 0 || length % SectorSize != 0 || offset + length > Data.LongLength)
        {
            throw new InvalidOperationException($"Unaligned or out of range I/O: offset {offset}, length {length}.");
        }
    }

    private static byte[] CreateFilled(long size)
    {
        var data = new byte[size];
        Array.Fill(data, (byte)0xAA);
        return data;
    }
}
