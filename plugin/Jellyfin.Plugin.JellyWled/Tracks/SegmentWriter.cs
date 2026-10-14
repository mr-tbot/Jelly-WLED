using System;
using System.IO;

namespace Jellyfin.Plugin.JellyWled.Tracks;

/// <summary>
/// Writes one segment: header first, frames appended, finalized by rewriting the header and renaming
/// ".seg.part" to ".seg". Readers may open the part file while it grows (the writer flushes regularly).
/// </summary>
public sealed class SegmentWriter : IDisposable
{
    private readonly SegmentHeader _header;
    private readonly string _finalPath;
    private readonly string _partPath;
    private FileStream? _stream;
    private uint _frames;
    private int _sinceFlush;
    private bool _done;

    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentWriter"/> class.
    /// </summary>
    /// <param name="finalPath">The ".seg" path.</param>
    /// <param name="header">The header (StartFrame and parameters set; FrameCount and flags are managed here).</param>
    public SegmentWriter(string finalPath, SegmentHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        _header = header;
        _header.Flags = 0;
        _header.FrameCount = 0;
        _finalPath = finalPath;
        _partPath = finalPath + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath) ?? ".");
        _stream = new FileStream(_partPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        _stream.Write(_header.ToBytes());
        _stream.Flush();
    }

    /// <summary>Gets the frames written so far.</summary>
    public uint Frames => _frames;

    /// <summary>Gets the path of the growing part file.</summary>
    public string PartPath => _partPath;

    /// <summary>
    /// Appends one ring frame.
    /// </summary>
    /// <param name="frame">Exactly FrameBytes bytes.</param>
    public void WriteFrame(ReadOnlySpan<byte> frame)
    {
        ObjectDisposedException.ThrowIf(_stream is null, this);
        if (frame.Length != _header.FrameBytes)
        {
            throw new ArgumentException("Frame has the wrong size.", nameof(frame));
        }

        _stream.Write(frame);
        _frames++;
        if (++_sinceFlush >= 5)
        {
            _stream.Flush();
            _sinceFlush = 0;
        }
    }

    /// <summary>
    /// Flushes buffered frames so readers can see them.
    /// </summary>
    public void Flush()
    {
        _stream?.Flush();
        _sinceFlush = 0;
    }

    /// <summary>
    /// Completes the segment: writes the final header and renames the part file into place.
    /// </summary>
    /// <returns>The final path.</returns>
    public string Complete()
    {
        ObjectDisposedException.ThrowIf(_stream is null, this);
        _header.FrameCount = _frames;
        _header.Flags |= SegmentHeader.FlagFinalized;
        _stream.Flush();
        _stream.Position = 0;
        _stream.Write(_header.ToBytes());
        _stream.Flush(true);
        _stream.Dispose();
        _stream = null;
        File.Move(_partPath, _finalPath, overwrite: true);
        _done = true;
        return _finalPath;
    }

    /// <summary>
    /// Deletes the part file (on failure or cancellation).
    /// </summary>
    public void Abort()
    {
        _stream?.Dispose();
        _stream = null;
        try
        {
            File.Delete(_partPath);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }

        _done = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_done)
        {
            Abort();
        }
    }
}
