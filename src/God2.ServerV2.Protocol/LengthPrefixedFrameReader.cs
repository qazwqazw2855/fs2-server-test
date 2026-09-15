using System.Buffers.Binary;

namespace God2.ServerV2.Protocol;

public static class LengthPrefixedFrameReader
{
    public const int HeaderLength = 2;
    public const int DefaultMaximumFrameLength = 16 * 1024;

    public static async Task<byte[]?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken,
        int maximumFrameLength = DefaultMaximumFrameLength)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (maximumFrameLength < HeaderLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFrameLength));
        }

        var header = new byte[HeaderLength];
        var headerBytes = await ReadAvailableAsync(
            stream,
            header,
            cancellationToken);

        if (headerBytes == 0)
        {
            return null;
        }

        if (headerBytes != HeaderLength)
        {
            throw new EndOfStreamException(
                $"Connection closed during frame header ({headerBytes}/{HeaderLength} bytes).");
        }

        var frameLength = BinaryPrimitives.ReadUInt16LittleEndian(header);

        if (frameLength < HeaderLength || frameLength > maximumFrameLength)
        {
            throw new InvalidDataException(
                $"Invalid frame length {frameLength}; allowed range is {HeaderLength}..{maximumFrameLength}.");
        }

        var frame = new byte[frameLength];
        header.CopyTo(frame, 0);

        var payloadLength = frameLength - HeaderLength;
        if (payloadLength == 0)
        {
            return frame;
        }

        var payloadBytes = await ReadAvailableAsync(
            stream,
            frame.AsMemory(HeaderLength, payloadLength),
            cancellationToken);

        if (payloadBytes != payloadLength)
        {
            throw new EndOfStreamException(
                $"Connection closed during frame payload ({payloadBytes}/{payloadLength} bytes).");
        }

        return frame;
    }

    private static async Task<int> ReadAvailableAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var total = 0;

        while (total < destination.Length)
        {
            var read = await stream.ReadAsync(
                destination[total..],
                cancellationToken);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
