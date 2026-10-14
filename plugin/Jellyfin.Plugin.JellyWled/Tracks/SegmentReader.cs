using System;
using System.IO;

namespace Jellyfin.Plugin.JellyWled.Tracks;

/// <summary>
/// Random-access reader over a segment file; tolerates a file that is still growing.
/// </summary>
public sealed class SegmentReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentReader"/> class.
    /// </summary>
    /// <param name="path">Segment path.</param>
    public SegmentReader(string path)
    {
        Path = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 12);
        var head = new byte[SegmentHeader.Size];
        try
        {
            if (_stream.Read(head, 0, head.Length) != head.Length)
            {
                throw new InvalidDataException("Segment too short.");
            }

            Header = SegmentHeader.Parse(head);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    /// <summary>Gets the file path.</summary>
    public string Path { get; }

    /// <summary>Gets the header as read when the file was opened.</summary>
    public SegmentHeader Header { get; }

    /// <summary>Gets the frames readable right now.</summary>
    public long ReadableFrames
    {
        get
        {
            lock (_lock)
            {
                var length = _stream.Length; // refreshed from the handle, so a renamed file still reads
                var n = Math.Max(0, (length - SegmentHeader.Size) / Header.FrameBytes);
                return Header.IsFinalized ? Math.Min(n, Header.FrameCount) : n;
            }
        }
    }

    /// <summary>
    /// Reads one frame by absolute index.
    /// </summary>
    /// <param name="frame">Absolute frame index.</param>
    /// <param name="destination">Receives FrameBytes bytes.</param>
    /// <returns>True when the frame was readable.</returns>
    public bool TryRead(long frame, Span<byte> destination)
    {
        var local = frame - Header.StartFrame;
        if (local < 0 || destination.Length < Header.FrameBytes)
        {
            return false;
        }

        lock (_lock)
        {
            var offset = SegmentHeader.Size + (local * Header.FrameBytes);
            if (offset + Header.FrameBytes > _stream.Length)
            {
                return false;
            }

            _stream.Position = offset;
            var dest = destination[..Header.FrameBytes];
            var read = 0;
            while (read < dest.Length)
            {
                var n = _stream.Read(dest[read..]);
                if (n <= 0)
                {
                    return false;
                }

                read += n;
            }

            return true;
        }
    }

    /// <summary>
    /// Reads a run of consecutive frames.
    /// </summary>
    /// <param name="frame">First absolute frame index.</param>
    /// <param name="destination">Receives up to destination.Length / FrameBytes frames.</param>
    /// <returns>The number of frames read.</returns>
    public int ReadRun(long frame, Span<byte> destination)
    {
        var local = frame - Header.StartFrame;
        if (local < 0)
        {
            return 0;
        }

        lock (_lock)
        {
            var offset = SegmentHeader.Size + (local * Header.FrameBytes);
            var available = Math.Min(ReadableFrames - local, destination.Length / Header.FrameBytes);
            if (available <= 0)
            {
                return 0;
            }

            _stream.Position = offset;
            var want = (int)available * Header.FrameBytes;
            var read = 0;
            while (read < want)
            {
                var n = _stream.Read(destination.Slice(read, want - read));
                if (n <= 0)
                {
                    break;
                }

                read += n;
            }

            return read / Header.FrameBytes;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _stream.Dispose();
}
