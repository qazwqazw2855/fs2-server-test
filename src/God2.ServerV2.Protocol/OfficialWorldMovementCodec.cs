using System.Buffers.Binary;

namespace God2.ServerV2.Protocol;

public readonly record struct OfficialWorldMovement(
    ushort X,
    ushort Y,
    byte Sequence,
    byte State);

public static class OfficialWorldMovementCodec
{
    public const int FrameLength = 10;
    public const int AcknowledgementFrameLength = 5;
    public const byte Opcode = 0x2E;
    public const byte AcknowledgementOpcode = 0x5D;
    public const byte MountedState = 0xFF;
    public const int MaximumPackedCoordinate = 0x7FFF;

    private static readonly byte[] CipherKeyPrefix =
        Convert.FromHexString("FD9FCAC842A869FE");

    public static bool TryDecode(
        ReadOnlySpan<byte> frame,
        out OfficialWorldMovement movement)
    {
        movement = default;

        if (frame.Length != FrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(frame) != FrameLength)
        {
            return false;
        }

        var decoded = Decode(frame);

        try
        {
            if (decoded[2] != Opcode ||
                decoded[8] != MountedState ||
                decoded[^1] !=
                    OfficialLoginWireTransform.ComputeChecksum(decoded))
            {
                return false;
            }

            var x = BinaryPrimitives.ReadUInt16LittleEndian(
                decoded.AsSpan(3, sizeof(ushort)));
            var y = BinaryPrimitives.ReadUInt16LittleEndian(
                decoded.AsSpan(5, sizeof(ushort)));

            if (x > MaximumPackedCoordinate ||
                y > MaximumPackedCoordinate)
            {
                return false;
            }

            movement = new OfficialWorldMovement(
                x,
                y,
                decoded[7],
                decoded[8]);
            return true;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static bool IsVerifiedRequest(ReadOnlySpan<byte> frame) =>
        TryDecode(frame, out _);

    public static byte[] EncodeAcknowledgement(byte sequence)
    {
        Span<byte> decoded =
            stackalloc byte[AcknowledgementFrameLength];

        BinaryPrimitives.WriteUInt16LittleEndian(
            decoded,
            AcknowledgementFrameLength);
        decoded[2] = AcknowledgementOpcode;
        decoded[3] = sequence;
        decoded[^1] =
            OfficialLoginWireTransform.ComputeChecksum(decoded);

        return Encode(decoded);
    }

    private static byte[] Decode(ReadOnlySpan<byte> frame)
    {
        var decoded = frame.ToArray();
        var previousPlain = 0xB0;

        for (var index = 2; index < decoded.Length; index++)
        {
            var encrypted =
                (decoded[index] + 3 - previousPlain) & 0xFF;
            var plain =
                CipherKeyPrefix[index - 2] ^ encrypted;

            decoded[index] = (byte)plain;
            previousPlain = plain;
        }

        return decoded;
    }

    private static byte[] Encode(ReadOnlySpan<byte> decoded)
    {
        var encoded = decoded.ToArray();
        var previousPlain = 0xB0;

        for (var index = 2; index < encoded.Length; index++)
        {
            var plain = decoded[index];
            encoded[index] = (byte)(
                ((CipherKeyPrefix[index - 2] ^ plain) +
                 previousPlain - 3) & 0xFF);
            previousPlain = plain;
        }

        return encoded;
    }
}
