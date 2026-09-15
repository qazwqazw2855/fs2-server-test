namespace God2.ServerV2.Protocol;

public enum OfficialLoginFailureCode : byte
{
    AccountNotRegistered = 0,
    DuplicateLogin = 1,
    AccountStopped = 2,
    CredentialsRejected = 3,
    NetworkDisconnected = 4,
    IllegalCharacter = 5,
    VersionMismatch = 6,
    EntitlementUnavailable = 7,
    ServerFull = 9
}

public static class OfficialLoginResponseCodec
{
    public const int FailureFrameLength = 5;

    public static byte[] EncodeFailure(
        OfficialLoginFailureCode failureCode)
    {
        var decoded = new byte[FailureFrameLength];
        decoded[0] = FailureFrameLength;
        decoded[1] = 0x00;
        decoded[2] = 0x1E;
        decoded[3] = (byte)failureCode;
        decoded[^1] =
            OfficialLoginWireTransform.ComputeChecksum(decoded);

        var encoded =
            OfficialLoginWireTransform.Encode(decoded);

        Array.Clear(decoded);
        return encoded;
    }
}
