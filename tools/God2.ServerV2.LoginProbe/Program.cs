using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using God2.ServerV2.Protocol;

var password = Environment.GetEnvironmentVariable("GOD2_TEST_PASSWORD");
var verifyIdleTimeout =
    string.Equals(
        Environment.GetEnvironmentVariable("GOD2_PROBE_VERIFY_IDLE_TIMEOUT"),
        "1",
        StringComparison.Ordinal);
var verifyLogout =
    string.Equals(
        Environment.GetEnvironmentVariable("GOD2_PROBE_VERIFY_LOGOUT"),
        "1",
        StringComparison.Ordinal);
var verifyMovement =
    string.Equals(
        Environment.GetEnvironmentVariable("GOD2_PROBE_VERIFY_MOVEMENT"),
        "1",
        StringComparison.Ordinal);

if ((verifyIdleTimeout ? 1 : 0) +
    (verifyLogout ? 1 : 0) +
    (verifyMovement ? 1 : 0) > 1)
{
    Console.Error.WriteLine(
        "閒置逾時、登出與移動模式只能啟用一種。");
    return 1;
}

if (string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("GOD2_TEST_PASSWORD 未設定。");
    return 1;
}

using var timeout = new CancellationTokenSource(
    TimeSpan.FromSeconds(verifyIdleTimeout ? 45 : 10));
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

var playerSpawn = await ReadFrameAsync(worldStream, timeout.Token);
var decodedSpawn =
    OfficialWorldBootstrapCodec.DecodeFirstPlayerSpawn(playerSpawn);

try
{
    var worldNameField = decodedSpawn.AsSpan(
        OfficialWorldBootstrapCodec.PlayerNameOffset,
        OfficialWorldBootstrapCodec.PlayerNameLength);

    var worldNameTerminator = worldNameField.IndexOf((byte)0);
    var worldNameLength = worldNameTerminator >= 0
        ? worldNameTerminator
        : worldNameField.Length;

    var worldName =
        Encoding.ASCII.GetString(worldNameField[..worldNameLength]);

    var worldCharacterId =
        BinaryPrimitives.ReadUInt32LittleEndian(
            decodedSpawn.AsSpan(
                OfficialWorldBootstrapCodec.PlayerIdOffset,
                sizeof(uint)));

    Require(worldName == "test001", $"World 角色名稱錯誤：{worldName}");
    Require(worldCharacterId == 1, $"World 角色 ID 錯誤：{worldCharacterId}");

    Console.WriteLine("World Handshake 測試成功");
    Console.WriteLine($"World 角色：{worldName} / ID={worldCharacterId}");
}
finally
{
    Array.Clear(decodedSpawn);
    Array.Clear(playerSpawn);
}

var remainingWorldFrameLengths =
    new[] { 320, 752, 68, 182, 36, 63, 42, 67, 88, 26 };

var remainingBytes = 0;

foreach (var expectedLength in remainingWorldFrameLengths)
{
    var worldFrame = await ReadFrameAsync(worldStream, timeout.Token);

    Require(
        worldFrame.Length == expectedLength,
        $"World Bootstrap Frame 長度錯誤：{worldFrame.Length}");

    remainingBytes += worldFrame.Length;
    Array.Clear(worldFrame);
}

Require(
    remainingBytes +
    OfficialWorldBootstrapCodec.PlayerSpawnFrameLength ==
    OfficialWorldBootstrapCodec.PayloadLength,
    "World Bootstrap 總長度錯誤");

Console.WriteLine(
    $"World Bootstrap 完整接收：{OfficialWorldBootstrapCodec.PayloadLength} bytes");

if (verifyIdleTimeout)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var eofProbe = new byte[1];
    var bytesRead = await worldStream.ReadAsync(eofProbe, timeout.Token);
    stopwatch.Stop();

    Require(bytesRead == 0, "World 閒置逾時後連線仍未關閉");
    Require(
        stopwatch.Elapsed >= TimeSpan.FromSeconds(28),
        $"World 閒置連線過早關閉：{stopwatch.Elapsed.TotalSeconds:F1} 秒");

    Console.WriteLine(
        $"World 30 秒閒置逾時測試成功：{stopwatch.Elapsed.TotalSeconds:F1} 秒");
}
else if (verifyMovement)
{
    var movementRequest =
        Convert.FromHexString("0A0080BAD7C34DA69488");

    Require(
        OfficialWorldMovementCodec.IsVerifiedRequest(movementRequest),
        "測試移動樣本未通過協定辨識");

    await worldStream.WriteAsync(movementRequest, timeout.Token);
    Array.Clear(movementRequest);

    await Task.Delay(100, timeout.Token);

    var logoutRequest = Convert.FromHexString("0500AC9D30");
    await worldStream.WriteAsync(logoutRequest, timeout.Token);
    Array.Clear(logoutRequest);

    var eofProbe = new byte[1];
    var bytesRead = await worldStream.ReadAsync(eofProbe, timeout.Token);

    Require(bytesRead == 0, "移動封包後 World 連線狀態異常");
    Console.WriteLine("World 移動封包辨識與連線維持測試成功");
}
else if (verifyLogout)
{
    var logoutRequest = Convert.FromHexString("0500AC9D30");

    Require(
        OfficialWorldLogoutCodec.IsVerifiedRequest(logoutRequest),
        "測試登出樣本未通過協定辨識");

    await worldStream.WriteAsync(logoutRequest, timeout.Token);
    Array.Clear(logoutRequest);

    var eofProbe = new byte[1];
    var bytesRead = await worldStream.ReadAsync(eofProbe, timeout.Token);

    Require(bytesRead == 0, "登出後 World 連線仍未關閉");
    Console.WriteLine("World 正式登出測試成功");
}
else
{
    var verifiedHeartbeat = Convert.FromHexString("05003D09A5");

    Require(
        OfficialWorldHeartbeatCodec.Classify(verifiedHeartbeat) ==
        OfficialWorldFrameClassification.KeepAlive,
        "測試 Heartbeat 樣本未通過協定分類");

    await worldStream.WriteAsync(
        verifiedHeartbeat,
        timeout.Token);

    Array.Clear(verifiedHeartbeat);

    await Task.Delay(100, timeout.Token);
    Console.WriteLine("World 連線持續接收測試成功");
}

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
