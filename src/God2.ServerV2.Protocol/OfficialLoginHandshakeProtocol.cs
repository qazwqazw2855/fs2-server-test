namespace God2.ServerV2.Protocol;

public sealed record LoginHandshakeResult(
    bool Succeeded,
    string Detail,
    byte[] ClientHandshake);

public static class OfficialLoginHandshakeProtocol
{
    public const int HandshakeLength = 19;

    private static readonly byte[] ServerHandshake =
        Convert.FromHexString("1300405FD0401BB55367D34D90DF1D929883DD");

    private static readonly byte[] ExpectedClientHandshake =
        Convert.FromHexString("1300E10638FA2835845B9FE9528DB9BCDF70BC");

    private static readonly byte[] VersionFollowUp =
        Convert.FromHexString("0600ED7CEE11");

    public static ReadOnlyMemory<byte> ServerHandshakeFrame => ServerHandshake;
    public static ReadOnlyMemory<byte> ExpectedClientHandshakeFrame => ExpectedClientHandshake;
    public static ReadOnlyMemory<byte> VersionFollowUpFrame => VersionFollowUp;

    public static async Task<LoginHandshakeResult> PerformAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(ServerHandshake, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var clientHandshake = new byte[HandshakeLength];
        var received = await ReadExactlyOrEofAsync(
            stream,
            clientHandshake,
            cancellationToken);

        if (received != HandshakeLength)
        {
            return new LoginHandshakeResult(
                false,
                $"Client closed during handshake ({received}/{HandshakeLength} bytes).",
                clientHandshake[..received]);
        }

        if (!clientHandshake.AsSpan().SequenceEqual(ExpectedClientHandshake))
        {
            return new LoginHandshakeResult(
                false,
                "Client handshake does not match the verified official-client frame.",
                clientHandshake);
        }

        await stream.WriteAsync(VersionFollowUp, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return new LoginHandshakeResult(
            true,
            "Verified Login 19/19/6 handshake completed.",
            clientHandshake);
    }

    private static async Task<int> ReadExactlyOrEofAsync(
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
