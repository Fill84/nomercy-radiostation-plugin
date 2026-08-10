// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Phillippe Pelzer - https://github.com/Fill84

using System.Text;

namespace NoMercy.Plugin.InternetRadio;

/// <summary>
/// Reads an Icecast/SHOUTcast body that carries interleaved metadata and hands the
/// caller only the audio.
///
/// A station that is asked for metadata answers with an <c>icy-metaint</c> and then
/// splices a metadata block into the audio every that many bytes: one length byte
/// (in units of sixteen), then that many bytes of <c>StreamTitle='...';</c> padded
/// with nulls. A browser knows nothing about any of that - passing the bytes through
/// untouched puts the title inside the MP3 frames and the audio breaks up - so the
/// blocks are removed here and reported to whoever wants the title.
/// </summary>
public sealed class IcyMetadataStream(
    Stream inner,
    int metaInterval,
    Action<string> onTitle
) : Stream
{
    private readonly int _interval = metaInterval;
    private int _untilMetadata = metaInterval;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken ct = default)
    {
        // Never more than what is left before the next metadata block, so the block
        // boundary is always the start of a read rather than something to find in
        // the middle of one.
        while (_untilMetadata == 0)
        {
            if (!await SkipMetadataAsync(ct))
            {
                return 0;
            }
        }

        int wanted = Math.Min(buffer.Length, _untilMetadata);
        int read = await inner.ReadAsync(buffer[..wanted], ct);
        _untilMetadata -= read;

        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken ct) =>
        await ReadAsync(buffer.AsMemory(offset, count), ct);

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Consumes one metadata block. Returns false when the stream ended inside it,
    /// which is the station hanging up rather than an error to raise.
    /// </summary>
    private async Task<bool> SkipMetadataAsync(CancellationToken ct)
    {
        byte[] lengthByte = new byte[1];
        if (!await ReadExactlyAsync(lengthByte, ct))
        {
            return false;
        }

        _untilMetadata = _interval;

        // Zero is the ordinary case: a station sends the length byte on every
        // boundary and only fills the block in when the title actually changed.
        int blockLength = lengthByte[0] * 16;
        if (blockLength == 0)
        {
            return true;
        }

        byte[] block = new byte[blockLength];
        if (!await ReadExactlyAsync(block, ct))
        {
            return false;
        }

        if (ReadTitle(block) is { } title)
        {
            onTitle(title);
        }

        return true;
    }

    private async Task<bool> ReadExactlyAsync(byte[] target, CancellationToken ct)
    {
        int filled = 0;
        while (filled < target.Length)
        {
            int read = await inner.ReadAsync(target.AsMemory(filled), ct);
            if (read == 0)
            {
                return false;
            }

            filled += read;
        }

        return true;
    }

    /// <summary>
    /// The title out of one metadata block, or null when the block carries none.
    ///
    /// Latin-1 rather than UTF-8: the protocol predates it and stations send single
    /// bytes, so decoding as UTF-8 turns every accented character into a question
    /// mark. A station that sends UTF-8 anyway is the rarer case and still renders,
    /// just mojibake, which beats dropping the title.
    /// </summary>
    internal static string? ReadTitle(ReadOnlySpan<byte> block)
    {
        string text = Encoding.Latin1.GetString(block).TrimEnd('\0');

        const string key = "StreamTitle='";
        int start = text.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += key.Length;

        // The value ends at the first "';" - a title may itself contain a bare
        // apostrophe (Guns N' Roses), and ending at that one truncates the name.
        int end = text.IndexOf("';", start, StringComparison.Ordinal);
        string title = (end < 0 ? text[start..] : text[start..end]).Trim();

        return title.Length == 0 ? null : title;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
