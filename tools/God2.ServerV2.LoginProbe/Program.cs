using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using God2.ServerV2.Protocol;

var password = Environment.GetEnvironmentVariable("GOD2_TEST_PASSWORD");

if (string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("GOD2_TEST_PASSWORD 未設定。");
    return 1;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
using var client = new TcpClient();

await client.ConnectAsync("127.0.0.1", 2592, timeout.Token);
await using var stream = client.GetStream();

var serverHandshake = await ReadFrameAsync(stream, timeout.Token);
Require(
    serverHandshake.AsSpan().SequenceEqual(
        OfficialLoginHandshakeProtocol.ServerHandshakeFrame.Span),
    "Server Handshake 不符");

await stream.WriteAsync(
    OfficialLoginHandshakeProtocol.ExpectedClientHandshakeFrame,
    timeout.Token);

var versionFollowUp = await ReadFrameAsync(stream, timeout.Token);
Require(
    versionFollowUp.AsSpan().SequenceEqual(
        OfficialLoginHandshakeProtocol.VersionFollowUpFrame.Span),
    "Version Follow-up 不符");

var loginRequest = BuildLoginRequest("god2test", password);

try
{
    await stream.WriteAsync(loginRequest, timeout.Token);
}
finally
{
    Array.Clear(loginRequest);
    password = null;
}

var loginSuccess = await ReadFrameAsync(stream, timeout.Token);

Require(
    loginSuccess.Length == OfficialLoginSuccessCodec.FrameLength,
    $"登入失敗或回應長度錯誤：{loginSuccess.Length}");

await stream.WriteAsync(
    Convert.FromHexString("06009202CE97"),
    timeout.Token);

var characterList = await ReadFrameAsync(stream, timeout.Token);
var decoded = OfficialLoginWireTransform.Decode(characterList);

try
{
    Require(
        decoded.Length ==
        OfficialServerSelectionCodec.ResponseFrameLength,
        $"角色列表長度錯誤：{decoded.Length}");

    Require(
        decoded[2] == OfficialServerSelectionCodec.ResponseOpcode,
        $"角色列表 Opcode 錯誤：0x{decoded[2]:X2}");

    Require(
        decoded[^1] ==
        OfficialLoginWireTransform.ComputeChecksum(decoded),
        "角色列表 Checksum 錯誤");

    var count =
        BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(3, 2));

    var nameField = decoded.AsSpan(
        OfficialServerSelectionCodec.CharacterNameOffset,
        OfficialServerSelectionCodec.CharacterNameLength);

    var terminator = nameField.IndexOf((byte)0);
    var nameLength = terminator >= 0
        ? terminator
        : nameField.Length;

    var name = Encoding.ASCII.GetString(nameField[..nameLength]);

    Require(count == 1, $"角色數量錯誤：{count}");
    Require(name == "test001", $"角色名稱錯誤：{name}");

    Console.WriteLine("無畫面登入測試成功");
    Console.WriteLine($"角色數量：{count}");
    Console.WriteLine($"角色名稱：{name}");
}
finally
{
    Array.Clear(decoded);
    Array.Clear(characterList);
    Array.Clear(loginSuccess);
}

using var worldClient = new TcpClient();
await worldClient.ConnectAsync("127.0.0.1", 2592, timeout.Token);
await using var worldStream = worldClient.GetStream();

var worldServerHandshake =
    await ReadFrameAsync(worldStream, timeout.Token);

Require(
    worldServerHandshake.AsSpan().SequenceEqual(
        OfficialWorldHandshakeProtocol.ServerHandshakeFrame.Span),
    "World Server Handshake 不符");

await worldStream.WriteAsync(
    OfficialWorldHandshakeProtocol.ExpectedClientHandshakeFrame,
    timeout.Token);

var worldFirstFollowUp =
    await ReadFrameAsync(worldStream, timeout.Token);

Require(
    worldFirstFollowUp.AsSpan().SequenceEqual(
        OfficialWorldHandshakeProtocol.FirstFollowUpFrame.Span),
    "World First Follow-up 不符");

Console.WriteLine("World Handshake 測試成功");

return 0;

static byte[] BuildLoginRequest(string account, string password)
{
    var decoded = new byte[OfficialLoginRequestCodec.FrameLength];

    BinaryPrimitives.WriteUInt16LittleEndian(
        decoded,
        OfficialLoginRequestCodec.FrameLength);

    Encoding.ASCII.GetBytes(account).CopyTo(
        decoded,
        OfficialLoginRequestCodec.AccountOffset);

    Encoding.ASCII.GetBytes(password).CopyTo(
        decoded,
        OfficialLoginRequestCodec.PasswordOffset);

    decoded[^1] =
        OfficialLoginWireTransform.ComputeChecksum(decoded);

    var encoded = OfficialLoginWireTransform.Encode(decoded);
    Array.Clear(decoded);
    return encoded;
}

static async Task<byte[]> ReadFrameAsync(
    Stream stream,
    CancellationToken cancellationToken)
{
    var header = new byte[2];
    await stream.ReadExactlyAsync(header, cancellationToken);

    var length = BinaryPrimitives.ReadUInt16LittleEndian(header);
    Require(length >= 2, $"無效封包長度：{length}");

    var frame = new byte[length];
    header.CopyTo(frame, 0);

    await stream.ReadExactlyAsync(
        frame.AsMemory(2),
        cancellationToken);

    return frame;
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
