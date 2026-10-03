using LinuxInstallHelper.Core.Writing;

namespace LinuxInstallHelper.Core.Tests.Writing;

/// <summary>Large disk that only stores the blocks actually written (unwritten bytes read as 0xAA).</summary>
public sealed class SparseBlockDevice(long size, int sectorSize = 512) : IBlockDevice
{
    private const int BlockSize = 64 * 1024;
    private readonly Dictionary<long, byte[]> _blocks = new();

    public int SectorSize { get; } = sectorSize;

    public long Size { get; } = size;

    public bool Opened { get; set; }

    public bool Disposed { get; private set; }

    public void Write(long offset, ReadOnlySpan<byte> data)
    {
        Check(offset, data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            Block(offset + i)[(offset + i) % BlockSize] = data[i];
        }
    }

    public void Read(long offset, Span<byte> buffer)
    {
        Check(offset, buffer.Length);
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Block(offset + i)[(offset + i) % BlockSize];
        }
    }

    public byte[] ReadRange(long offset, int length)
    {
        var result = new byte[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = Block(offset + i)[(offset + i) % BlockSize];
        }

        return result;
    }

    public void Flush()
    {
    }

    public void Dispose() => Disposed = true;

    private byte[] Block(long position)
    {
        var key = position / BlockSize;
        if (!_blocks.TryGetValue(key, out var block))
        {
            block = new byte[BlockSize];
            Array.Fill(block, (byte)0xAA);
            _blocks[key] = block;
        }

        return block;
    }

    private void Check(long offset, int length)
    {
        if (offset % SectorSize != 0 || length % SectorSize != 0 || offset + length > Size)
        {
            throw new InvalidOperationException($"Unaligned or out of range I/O: offset {offset}, length {length}.");
        }
    }
}
