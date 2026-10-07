using System.Runtime.InteropServices;

namespace LinuxInstallHelper.Core.Writing;

/// <summary>Native memory aligned on the sector size, as required by unbuffered raw I/O.</summary>
internal sealed unsafe class AlignedBuffer : IDisposable
{
    private readonly void* _pointer;
    private readonly int _length;

    public AlignedBuffer(int length, int alignment)
    {
        var align = (nuint)Math.Max(alignment, 4096);
        _length = (int)ImageWriteEngine.AlignUp(length, (int)align);
        _pointer = NativeMemory.AlignedAlloc((nuint)_length, align);
    }

    public Span<byte> Span => new(_pointer, _length);

    public void Dispose() => NativeMemory.AlignedFree(_pointer);
}
