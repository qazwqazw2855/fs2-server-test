namespace God2.ServerV2.Protocol;

public static class OfficialWorldMovementCodec
{
    public const int FrameLength = 10;

    private static readonly byte[][] VerifiedRequests =
    [
        Convert.FromHexString("0A0080BAD7C34DA69488"),
        Convert.FromHexString("0A0080BAD7C44EA79586")
    ];

    public static bool IsVerifiedRequest(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != FrameLength)
        {
            return false;
        }

        foreach (var verifiedRequest in VerifiedRequests)
        {
            if (frame.SequenceEqual(verifiedRequest))
            {
                return true;
            }
        }

        return false;
    }
}
