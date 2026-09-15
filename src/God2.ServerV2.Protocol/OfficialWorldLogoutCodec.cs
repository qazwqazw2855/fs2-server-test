namespace God2.ServerV2.Protocol;

public static class OfficialWorldLogoutCodec
{
    public const int FrameLength = 5;

    private static readonly byte[] VerifiedRequest =
        Convert.FromHexString("0500AC9D30");

    public static bool IsVerifiedRequest(ReadOnlySpan<byte> frame) =>
        frame.Length == FrameLength &&
        frame.SequenceEqual(VerifiedRequest);
}
