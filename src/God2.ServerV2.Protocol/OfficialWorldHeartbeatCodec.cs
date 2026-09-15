using System.Buffers.Binary;

namespace God2.ServerV2.Protocol;

public enum OfficialWorldFrameClassification
{
    Unknown,
    KeepAlive
}

public static class OfficialWorldHeartbeatCodec
{
    public const int FrameLength = 5;

    private static readonly byte[][] VerifiedFrames =
    [
        Convert.FromHexString("05003D09A5"),
        Convert.FromHexString("05007ACCEC")
    ];

    public static OfficialWorldFrameClassification Classify(
        ReadOnlySpan<byte> frame)
    {
        if (frame.Length != FrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(frame) != FrameLength)
        {
            return OfficialWorldFrameClassification.Unknown;
        }

        foreach (var verifiedFrame in VerifiedFrames)
        {
            if (frame.SequenceEqual(verifiedFrame))
            {
                return OfficialWorldFrameClassification.KeepAlive;
            }
        }

        return OfficialWorldFrameClassification.Unknown;
    }
}
