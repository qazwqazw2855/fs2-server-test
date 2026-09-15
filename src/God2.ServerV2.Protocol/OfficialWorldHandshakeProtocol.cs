namespace God2.ServerV2.Protocol;

public static class OfficialWorldHandshakeProtocol
{
    public const int HandshakeLength = 19;
    public const int FirstFollowUpLength = 6;

    private static readonly byte[] ServerHandshake =
        Convert.FromHexString(
            "1300FD9FCAC842A869FEA1BEC83A23DACF5249");

    private static readonly byte[] ExpectedClientHandshake =
        Convert.FromHexString(
            "1300D20A8DD62AEC4D6FF6F3F5D97E5C26B962");

    private static readonly byte[] FirstFollowUp =
        Convert.FromHexString(
            "0600A96DB7E8");

    public static ReadOnlyMemory<byte> ServerHandshakeFrame =>
        ServerHandshake;

    public static ReadOnlyMemory<byte> ExpectedClientHandshakeFrame =>
        ExpectedClientHandshake;

    public static ReadOnlyMemory<byte> FirstFollowUpFrame =>
        FirstFollowUp;

    public static bool IsExpectedClientHandshake(
        ReadOnlySpan<byte> frame) =>
        frame.Length == HandshakeLength &&
        frame.SequenceEqual(ExpectedClientHandshake);
}
