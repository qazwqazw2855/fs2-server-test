namespace God2.ServerV2.Protocol;

public static class OfficialWorldMovementCodec
{
    public const int FrameLength = 10;

    private static readonly byte[] VerifiedRequest =
        Convert.FromHexString("0A0080BAD7C34DA69488");

    public static bool IsVerifiedRequest(ReadOnlySpan<byte> frame) =>
        frame.Length == FrameLength &&
        frame.SequenceEqual(VerifiedRequest);
}
