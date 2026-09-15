using God2.ServerV2.Protocol;

namespace God2.ServerV2.Protocol.Tests;

public sealed class LengthPrefixedFrameReaderTests
{
    [Fact]
    public async Task Reads_complete_little_endian_length_prefixed_frame()
    {
        byte[] expected = [0x06, 0x00, 0x92, 0x02, 0xCE, 0x97];
        await using var stream = new MemoryStream(expected);

        var actual = await LengthPrefixedFrameReader.ReadAsync(
            stream,
            CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Returns_null_when_remote_closes_before_next_header()
    {
        await using var stream = new MemoryStream([]);

        var actual = await LengthPrefixedFrameReader.ReadAsync(
            stream,
            CancellationToken.None);

        Assert.Null(actual);
    }

    [Fact]
    public async Task Rejects_frame_length_below_header_size()
    {
        await using var stream = new MemoryStream([0x01, 0x00]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => LengthPrefixedFrameReader.ReadAsync(
                stream,
                CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_truncated_payload()
    {
        await using var stream = new MemoryStream(
            [0x06, 0x00, 0x92, 0x02]);

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => LengthPrefixedFrameReader.ReadAsync(
                stream,
                CancellationToken.None));
    }

    [Fact]
    public async Task Reads_verified_208_byte_login_request_without_logging_contents()
    {
        var expected = new byte[208];
        expected[0] = 0xD0;
        expected[1] = 0x00;

        await using var stream = new MemoryStream(expected);

        var actual = await LengthPrefixedFrameReader.ReadAsync(
            stream,
            CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal(208, actual.Length);
        Assert.Equal(expected, actual);
    }
}
