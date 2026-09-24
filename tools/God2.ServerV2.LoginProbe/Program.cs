using God2.ServerV2.Application;
using God2.ServerV2.Core;
using God2.ServerV2.Persistence;
using God2.ServerV2.Protocol;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

if (args.Length != 0)
{
    Console.Error.WriteLine(
        "LoginProbe 不接受命令列參數；請使用 GOD2_PROBE_PORT 設定連線埠。");
    return 1;
}

var password = Environment.GetEnvironmentVariable("GOD2_TEST_PASSWORD");
var accountName =
    Environment.GetEnvironmentVariable("GOD2_TEST_ACCOUNT") ??
    "god2test";
var expectedCharacterName =
    Environment.GetEnvironmentVariable("GOD2_EXPECTED_CHARACTER") ??
    "test001";
var expectedCharacterIdText =
    Environment.GetEnvironmentVariable(
        "GOD2_EXPECTED_CHARACTER_ID");
var localAddress =
    Environment.GetEnvironmentVariable(
        "GOD2_PROBE_LOCAL_ADDRESS");
var holdSecondsText =
    Environment.GetEnvironmentVariable(
        "GOD2_PROBE_HOLD_SECONDS");

var expectDuplicateLogin =
    string.Equals(
        Environment.GetEnvironmentVariable(
            "GOD2_PROBE_EXPECT_DUPLICATE_LOGIN"),
        "1",
        StringComparison.Ordinal);
var verifyPendingOwnership =
    string.Equals(
        Environment.GetEnvironmentVariable(
            "GOD2_PROBE_VERIFY_PENDING_OWNERSHIP"),
        "1",
        StringComparison.Ordinal);
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
var verifyDuplicateMovement =
    string.Equals(
        Environment.GetEnvironmentVariable(
            "GOD2_PROBE_VERIFY_DUPLICATE_MOVEMENT"),
        "1",
        StringComparison.Ordinal);
var verifyWorldLoginMap =
    string.Equals(
        Environment.GetEnvironmentVariable(
            "GOD2_PROBE_VERIFY_WORLD_LOGIN_MAP"),
        "1",
        StringComparison.Ordinal);
var verifyPortal =
    string.Equals(
        Environment.GetEnvironmentVariable(
            "GOD2_PROBE_VERIFY_PORTAL"),
        "1",
        StringComparison.Ordinal);
var verifyNpcInteraction =
    string.Equals(
        Environment.GetEnvironmentVariable(
            "GOD2_PROBE_VERIFY_NPC_INTERACTION"),
        "1",
        StringComparison.Ordinal);
var verifyNpcDialog =
    string.Equals(
        Environment.GetEnvironmentVariable(
            "GOD2_PROBE_VERIFY_NPC_DIALOG"),
        "1",
        StringComparison.Ordinal);
var verifyNpcInteractionOwnership =
    string.Equals(
        Environment.GetEnvironmentVariable(
            "GOD2_PROBE_VERIFY_NPC_INTERACTION_OWNERSHIP"),
        "1",
        StringComparison.Ordinal);

if ((expectDuplicateLogin ? 1 : 0) +
    (verifyPendingOwnership ? 1 : 0) +
    (verifyIdleTimeout ? 1 : 0) +
    (verifyLogout ? 1 : 0) +
    (verifyMovement ? 1 : 0) +
    (verifyDuplicateMovement ? 1 : 0) +
    (verifyPortal ? 1 : 0) +
    (verifyWorldLoginMap ? 1 : 0) +
    (verifyNpcInteraction ? 1 : 0) +
    (verifyNpcDialog ? 1 : 0) +
    (verifyNpcInteractionOwnership ? 1 : 0) > 1)
{
    Console.Error.WriteLine(
        "測試模式只能啟用一種（包含登入地圖檢查）。");
    return 1;
}

if (string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("GOD2_TEST_PASSWORD 未設定。");
    return 1;
}

if (string.IsNullOrWhiteSpace(accountName) ||
    string.IsNullOrWhiteSpace(expectedCharacterName))
{
    Console.Error.WriteLine(
        "測試帳號與預期角色名稱不可為空白。");
    return 1;
}

var expectedCharacterId =
    string.IsNullOrWhiteSpace(expectedCharacterIdText)
        ? 1U
        : uint.TryParse(
            expectedCharacterIdText,
            out var parsedCharacterId) &&
            parsedCharacterId > 0
            ? parsedCharacterId
            : 0U;

if (expectedCharacterId == 0)
{
    Console.Error.WriteLine(
        "GOD2_EXPECTED_CHARACTER_ID 無效。");
    return 1;
}

var holdSeconds =
    string.IsNullOrWhiteSpace(holdSecondsText)
        ? 0
        : int.TryParse(
            holdSecondsText,
            out var parsedHoldSeconds) &&
            parsedHoldSeconds is >= 0 and <= 120
            ? parsedHoldSeconds
            : -1;

if (holdSeconds < 0)
{
    Console.Error.WriteLine(
        "GOD2_PROBE_HOLD_SECONDS 必須介於 0 到 120。");
    return 1;
}

var portText =
    Environment.GetEnvironmentVariable("GOD2_PROBE_PORT");

var port = string.IsNullOrWhiteSpace(portText)
    ? 2592
    : int.TryParse(portText, out var parsedPort) &&
        parsedPort is >= 1 and <= 65535
        ? parsedPort
        : 0;

if (port == 0)
{
    Console.Error.WriteLine("GOD2_PROBE_PORT 無效。");
    return 1;
}

using var timeout = new CancellationTokenSource(
    TimeSpan.FromSeconds(
        verifyIdleTimeout
            ? 45
            : Math.Max(10, holdSeconds + 10)));
if (verifyPortal)
{
    Require(
        expectedCharacterId > 0,
        "Portal Probe 必須設定有效的 GOD2_EXPECTED_CHARACTER_ID。");

    await PreparePortalTestStateAsync(
        accountName,
        password,
        expectedCharacterId,
        expectedCharacterName,
        timeout.Token);
}

TcpClient? loginClient = null;
var maxHandshakeAttempts = verifyPortal ? 2 : 1;

for (var attempt = 0; attempt < maxHandshakeAttempts; attempt++)
{
    var candidate = new TcpClient();

    try
    {
        BindLocalAddress(candidate, localAddress);
        await candidate.ConnectAsync("127.0.0.1", port, timeout.Token);

        var handshake = await ReadFrameAsync(
            candidate.GetStream(), timeout.Token);

        if (handshake.AsSpan().SequenceEqual(
                OfficialLoginHandshakeProtocol.ServerHandshakeFrame.Span))
        {
            loginClient = candidate;
            break;
        }

        if (verifyPortal &&
            attempt == 0 &&
            handshake.AsSpan().SequenceEqual(
                OfficialWorldHandshakeProtocol.ServerHandshakeFrame.Span))
        {
            Console.WriteLine(
                "偵測到前次測試留下的 World 票據，關閉連線後重試 Login。");
            candidate.Dispose();
            await Task.Delay(100, timeout.Token);
            continue;
        }

        throw new InvalidOperationException(
            "Server Handshake 不符；只允許 Portal Probe 清理一次已知 World 票據。");
    }
    catch
    {
        candidate.Dispose();
        throw;
    }
}

using var client = loginClient ??
    throw new InvalidOperationException("Portal Probe 無法取得 Login Handshake。");
await using var stream = client.GetStream();

await stream.WriteAsync(
    OfficialLoginHandshakeProtocol.ExpectedClientHandshakeFrame,
    timeout.Token);

var versionFollowUp = await ReadFrameAsync(stream, timeout.Token);
Require(
    versionFollowUp.AsSpan().SequenceEqual(
        OfficialLoginHandshakeProtocol.VersionFollowUpFrame.Span),
    "Version Follow-up 不符");

var loginRequest = BuildLoginRequest(accountName, password);
var pendingDuplicateLoginRequest =
    verifyPendingOwnership
        ? BuildLoginRequest(accountName, password)
        : null;

try
{
    await stream.WriteAsync(loginRequest, timeout.Token);
}
finally
{
    Array.Clear(loginRequest);

    if (!verifyPendingOwnership)
    {
        password = null;
    }
}

var loginSuccess = await ReadFrameAsync(stream, timeout.Token);

if (expectDuplicateLogin)
{
    var expectedFailure =
        OfficialLoginResponseCodec.EncodeFailure(
            OfficialLoginFailureCode.DuplicateLogin);

    try
    {
        Require(
            loginSuccess.AsSpan().SequenceEqual(expectedFailure),
            "未收到預期的 DuplicateLogin 回應");

        Console.WriteLine("重複登入拒絕測試成功");
        return 0;
    }
    finally
    {
        Array.Clear(expectedFailure);
        Array.Clear(loginSuccess);
    }
}

Require(
    loginSuccess.Length == OfficialLoginSuccessCodec.FrameLength,
    $"登入失敗或回應長度錯誤：{loginSuccess.Length}");

await stream.WriteAsync(
    Convert.FromHexString("06009202CE97"),
    timeout.Token);

var characterList = await ReadFrameAsync(stream, timeout.Token);
var decoded = CurrentClientServerWireTransform.Decode(characterList);

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
        CurrentClientServerWireTransform.ComputeChecksum(decoded),
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
    Require(
        name == expectedCharacterName,
        $"角色名稱錯誤：{name}，預期：{expectedCharacterName}");

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

if (verifyPendingOwnership)
{
    var loginEofProbe = new byte[1];
    var loginBytesRead =
        await stream.ReadAsync(loginEofProbe, timeout.Token);

    Require(
        loginBytesRead == 0,
        "角色選擇完成後 Login 連線仍未關閉");

    var duplicateLoginRequest =
        pendingDuplicateLoginRequest ??
        throw new InvalidOperationException(
            "Pending ownership 測試登入封包不存在");

    try
    {
        await VerifyDuplicateLoginAsync(
            port,
            duplicateLoginRequest,
            timeout.Token);
    }
    finally
    {
        Array.Clear(duplicateLoginRequest);
        password = null;
    }

    Console.WriteLine(
        "Pending ownership 阻擋第二次登入測試成功");
}

using var worldClient = new TcpClient();

BindLocalAddress(worldClient, localAddress);

await worldClient.ConnectAsync("127.0.0.1", port, timeout.Token);
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

    Require(
        worldName == expectedCharacterName,
        $"World 角色名稱錯誤：{worldName}，預期：{expectedCharacterName}");
    Require(
        worldCharacterId == expectedCharacterId,
        $"World 角色 ID 錯誤：{worldCharacterId}，預期：{expectedCharacterId}");

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

if (verifyWorldLoginMap)
{
    Console.WriteLine(
        $"World Login Map 驗證：Official Bootstrap " +
        $"{OfficialWorldBootstrapCodec.PayloadLength} bytes；" +
        "Portal projection 未附加");
}

var expectedNpcHandles =
    verifyWorldLoginMap ? Array.Empty<uint>() :
    verifyPortal
        ? Array.Empty<uint>()
        : verifyMovement
            ? new uint[] { 3793 }
            : verifyNpcDialog
            ? new uint[] { OfficialNpcDialogCodec.LiveDialogHandle }
            : new uint[] { 5042, 5096 };

foreach (var expectedNpcHandle in expectedNpcHandles)
{
    var npcSpawn =
        await ReadFrameAsync(worldStream, timeout.Token);

    try
    {
        Require(
            OfficialNpcSpawnCodec.TryDecodeHandle(
                npcSpawn,
                out var npcHandle),
            "NPC Spawn Frame 格式錯誤");

        Require(
            npcHandle == expectedNpcHandle,
            $"NPC Handle 錯誤：{npcHandle}，預期：{expectedNpcHandle}");
    }
    finally
    {
        Array.Clear(npcSpawn);
    }
}

Console.WriteLine(
    $"NPC Spawn 完整接收：{string.Join(", ", expectedNpcHandles)}");

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
else if (verifyNpcDialog)
{
    var openRequest =
        OfficialNpcInteractionCodec.EncodeOpen(
            OfficialNpcDialogCodec.LiveDialogHandle);

    try
    {
        Require(
            OfficialNpcInteractionCodec.TryDecode(
                openRequest,
                out var openInteraction,
                out var openFailure),
            $"NPC 3793 開啟樣本辨識失敗：{openFailure}");
        Require(
            openInteraction is not null &&
            openInteraction.Kind ==
                OfficialNpcInteractionKind.Open &&
            openInteraction.ClientEntityHandle ==
                OfficialNpcDialogCodec.LiveDialogHandle,
            "NPC 3793 開啟樣本內容錯誤");

        await worldStream.WriteAsync(
            openRequest,
            timeout.Token);
    }
    finally
    {
        Array.Clear(openRequest);
    }

    var dialogResponse =
        await ReadFrameAsync(
            worldStream,
            timeout.Token);

    try
    {
        Require(
            OfficialNpcDialogCodec.TryDecodeExactOpenResponse(
                dialogResponse,
                out var responseHandle),
            "NPC 3793 對話回應格式或證據內容錯誤");
        Require(
            responseHandle ==
                OfficialNpcDialogCodec.LiveDialogHandle,
            $"NPC 對話回應 Handle 錯誤：{responseHandle}");
    }
    finally
    {
        Array.Clear(dialogResponse);
    }

    var selectionRequest =
        OfficialNpcDialogSelectionCodec
            .EncodeStandaloneForProbe(0x11);

    try
    {
        Require(
            OfficialNpcDialogSelectionCodec.TryDecode(
                selectionRequest,
                out var selection,
                out var selectionFailure),
            $"NPC 3793 對話選項樣本辨識失敗：{selectionFailure}");
        Require(
            selection is not null &&
            selection.ClientEntityHandle ==
                OfficialNpcDialogSelectionCodec.LiveDialogHandle &&
            selection.Selector ==
                OfficialNpcDialogSelectionCodec.LiveDialogSelector &&
            selection.OpaqueClientValue == 0x11,
            "NPC 3793 對話選項樣本內容錯誤");

        await worldStream.WriteAsync(
            selectionRequest,
            timeout.Token);
    }
    finally
    {
        Array.Clear(selectionRequest);
    }

    await Task.Delay(100, timeout.Token);

    var reopenRequest =
        OfficialNpcInteractionCodec.EncodeOpen(
            OfficialNpcDialogCodec.LiveDialogHandle);

    try
    {
        await worldStream.WriteAsync(
            reopenRequest,
            timeout.Token);
    }
    finally
    {
        Array.Clear(reopenRequest);
    }

    var reopenedDialogResponse =
        await ReadFrameAsync(
            worldStream,
            timeout.Token);

    try
    {
        Require(
            OfficialNpcDialogCodec.TryDecodeExactOpenResponse(
                reopenedDialogResponse,
                out var reopenedHandle),
            "NPC 3793 選項後重新開啟未收到正確回應");
        Require(
            reopenedHandle ==
                OfficialNpcDialogCodec.LiveDialogHandle,
            $"NPC 重新開啟 Handle 錯誤：{reopenedHandle}");
    }
    finally
    {
        Array.Clear(reopenedDialogResponse);
    }

    var logoutRequest =
        Convert.FromHexString("0500AC9D30");
    await worldStream.WriteAsync(
        logoutRequest,
        timeout.Token);
    Array.Clear(logoutRequest);

    var eofProbe = new byte[1];
    var bytesRead =
        await worldStream.ReadAsync(
            eofProbe,
            timeout.Token);

    Require(
        bytesRead == 0,
        "NPC 3793 對話後正式登出未關閉 World 連線");

    Console.WriteLine(
        "NPC 3793 Open、Selection、Session 釋放、重新 Open 與登出測試成功");
}
else if (verifyNpcInteractionOwnership)
{
    var firstOpenRequest =
        Convert.FromHexString("0800776188D83FFD");

    Require(
        OfficialNpcInteractionCodec.TryDecode(
            firstOpenRequest,
            out var firstOpen,
            out var firstFailure),
        $"NPC 5042 開啟樣本辨識失敗：{firstFailure}");
    Require(
        firstOpen is not null &&
        firstOpen.Kind ==
            OfficialNpcInteractionKind.Open &&
        firstOpen.ClientEntityHandle == 5042,
        "NPC 5042 開啟樣本內容錯誤");

    await worldStream.WriteAsync(
        firstOpenRequest,
        timeout.Token);
    Array.Clear(firstOpenRequest);

    await Task.Delay(100, timeout.Token);

    var secondOpenRequest =
        Convert.FromHexString("080077ABBED83F73");

    Require(
        OfficialNpcInteractionCodec.TryDecode(
            secondOpenRequest,
            out var secondOpen,
            out var secondFailure),
        $"NPC 5096 開啟樣本辨識失敗：{secondFailure}");
    Require(
        secondOpen is not null &&
        secondOpen.Kind ==
            OfficialNpcInteractionKind.Open &&
        secondOpen.ClientEntityHandle == 5096,
        "NPC 5096 開啟樣本內容錯誤");

    await worldStream.WriteAsync(
        secondOpenRequest,
        timeout.Token);
    Array.Clear(secondOpenRequest);

    var eofProbe = new byte[1];
    var bytesRead =
        await worldStream.ReadAsync(
            eofProbe,
            timeout.Token);

    Require(
        bytesRead == 0,
        "第二個 NPC 互動未被拒絕，World 連線仍開啟");

    Console.WriteLine(
        "NPC Session 單一所有權拒絕與中斷清理測試成功");
}
else if (verifyNpcInteraction)
{
    var openRequest =
        Convert.FromHexString("0800776188D83FFD");

    Require(
        OfficialNpcInteractionCodec.TryDecode(
            openRequest,
            out var openInteraction,
            out var openFailure),
        $"NPC 開啟測試樣本辨識失敗：{openFailure}");
    Require(
        openInteraction is not null &&
        openInteraction.Kind ==
            OfficialNpcInteractionKind.Open &&
        openInteraction.ClientEntityHandle == 5042,
        "NPC 開啟測試樣本內容錯誤");

    await worldStream.WriteAsync(
        openRequest,
        timeout.Token);
    Array.Clear(openRequest);

    await Task.Delay(100, timeout.Token);

    var closeRequest =
        Convert.FromHexString("0800716388D83FFF");

    Require(
        OfficialNpcInteractionCodec.TryDecode(
            closeRequest,
            out var closeInteraction,
            out var closeFailure),
        $"NPC 關閉測試樣本辨識失敗：{closeFailure}");
    Require(
        closeInteraction is not null &&
        closeInteraction.Kind ==
            OfficialNpcInteractionKind.MerchantClose &&
        closeInteraction.ClientEntityHandle == 5042,
        "NPC 關閉測試樣本內容錯誤");

    await worldStream.WriteAsync(
        closeRequest,
        timeout.Token);
    Array.Clear(closeRequest);

    await Task.Delay(100, timeout.Token);

    var logoutRequest =
        Convert.FromHexString("0500AC9D30");

    await worldStream.WriteAsync(
        logoutRequest,
        timeout.Token);
    Array.Clear(logoutRequest);

    var eofProbe = new byte[1];
    var bytesRead =
        await worldStream.ReadAsync(
            eofProbe,
            timeout.Token);

    Require(
        bytesRead == 0,
        "NPC 互動後正式登出未關閉 World 連線");

    Console.WriteLine(
        "NPC 5042 開啟、Session 所有權釋放與正式登出測試成功");
}
else if (verifyPortal)
{
    const byte portalMovementSequence = 1;

    var movementRequest =
        OfficialWorldMovementCodec.EncodeRequest(
            249,
            246,
            portalMovementSequence);

    Require(
        OfficialWorldMovementCodec.TryDecode(
            movementRequest,
            out var movement),
        "Portal 前置移動封包未通過協定辨識");

    Require(
        movement.X == 249 &&
        movement.Y == 246 &&
        movement.Sequence == portalMovementSequence,
        $"Portal 前置移動內容錯誤：({movement.X},{movement.Y}) seq={movement.Sequence}");

    await worldStream.WriteAsync(
        movementRequest,
        timeout.Token);
    Array.Clear(movementRequest);

    var movementAcknowledgement =
        await ReadFrameAsync(
            worldStream,
            timeout.Token);

    Require(
        OfficialWorldMovementCodec.TryDecodeAcknowledgement(
            movementAcknowledgement,
            out var acknowledgedSequence),
        "Portal 前置移動 ACK 格式錯誤");

    Require(
        acknowledgedSequence == portalMovementSequence,
        $"Portal 前置移動 ACK Sequence 錯誤：{acknowledgedSequence}");

    Array.Clear(movementAcknowledgement);

    var activateDecoded =
        Convert.FromHexString(
            "08000CA82734FC9C");

    var activate =
        OfficialPortalWireCodec.DecodeActivate(
            OfficialPortalWireCodec.ClientBuildId,
            ConnectionStage.InWorld,
            activateDecoded);

    Require(
        activate.Succeeded &&
        activate.Value is not null,
        $"Portal Activate fixture 無法辨識：{activate.Code}");

    var serializedActivate =
        OfficialPortalWireCodec.SerializeActivate(
            activate.Value!);

    Require(
        serializedActivate.Succeeded,
        $"Portal Activate 無法序列化：{serializedActivate.Code}");

    var portalRequest =
        serializedActivate.Value.ToArray();

    await worldStream.WriteAsync(
        portalRequest,
        timeout.Token);

    Array.Clear(portalRequest);
    Array.Clear(activateDecoded);

    // Verified Stage 3 transition order: 0x61 -> 0xBB.
    var mapTransitionFrame =
        await ReadFrameAsync(
            worldStream,
            timeout.Token);

    var portalPreludeFrame =
        await ReadFrameAsync(
            worldStream,
            timeout.Token);

    var portalResult =
        OfficialPortalWireCodec.DecodeResult(
            portalPreludeFrame,
            mapTransitionFrame);

    Require(
        portalResult.Succeeded &&
        portalResult.Value is not null,
        $"Portal 回應無法辨識：{portalResult.Code}");

    Require(
        portalResult.Value!.ClientMapId == 7 &&
        portalResult.Value.AreaId == 15 &&
        portalResult.Value.X == 48 &&
        portalResult.Value.Y == 81,
        $"Portal 目的地錯誤：{portalResult.Value.ClientMapId}:{portalResult.Value.AreaId} / ({portalResult.Value.X},{portalResult.Value.Y})");

    Array.Clear(mapTransitionFrame);
    Array.Clear(portalPreludeFrame);

    Console.WriteLine(
        "Portal (249,246) -> 7:15 / (48,81) 驗證成功");

    var destinationNpcSpawn =
        await ReadFrameAsync(
            worldStream,
            timeout.Token);

    try
    {
        Require(
            OfficialNpcSpawnCodec.TryDecodeHandle(
                destinationNpcSpawn,
                out var destinationNpcHandle),
            "Portal 目的地 NPC Spawn Frame 格式錯誤");

        Require(
            destinationNpcHandle ==
                OfficialNpcDialogCodec.LiveDialogHandle,
            $"Portal 目的地 NPC Handle 錯誤：{destinationNpcHandle}，預期：{OfficialNpcDialogCodec.LiveDialogHandle}");
    }
    finally
    {
        Array.Clear(destinationNpcSpawn);
    }

    var logoutRequest =
        Convert.FromHexString("0500AC9D30");

    await worldStream.WriteAsync(
        logoutRequest,
        timeout.Token);

    Array.Clear(logoutRequest);

    var eofProbe = new byte[1];
    var bytesRead =
        await worldStream.ReadAsync(
            eofProbe,
            timeout.Token);

    Require(
        bytesRead == 0,
        "Portal 測試正式登出後 World 連線未關閉");

    Console.WriteLine(
        "Portal 閉環成功：170015000 -> 170015007，NPC 3793 Spawn，Logout 成功");
}
else if (verifyDuplicateMovement)
{
    const string movementHex = "0A0080BAD7C34DA69488";
    var movementRequest = Convert.FromHexString(movementHex);

    Require(
        OfficialWorldMovementCodec.TryDecode(
            movementRequest,
            out var movement),
        "重複移動測試樣本未通過協定辨識");
    Require(
        movement.Sequence == 1,
        $"重複移動測試 Sequence 錯誤：{movement.Sequence}");

    await worldStream.WriteAsync(movementRequest, timeout.Token);

    var acknowledgement =
        await ReadFrameAsync(worldStream, timeout.Token);

    Require(
        OfficialWorldMovementCodec.TryDecodeAcknowledgement(
            acknowledgement,
            out var acknowledgedSequence),
        "首次移動 ACK 格式錯誤");
    Require(
        acknowledgedSequence == movement.Sequence,
        $"首次移動 ACK Sequence 錯誤：{acknowledgedSequence}");

    Array.Clear(acknowledgement);

    await worldStream.WriteAsync(movementRequest, timeout.Token);
    Array.Clear(movementRequest);

    var eofProbe = new byte[1];
    var bytesRead = await worldStream.ReadAsync(eofProbe, timeout.Token);

    Require(
        bytesRead == 0,
        "重複移動序號未被拒絕，連線仍然開啟");
    Console.WriteLine(
        "World 重複移動序號拒絕、無第二次 ACK 測試成功");
}
else if (verifyMovement)
{
    var movementSamples = new[]
    {
        (Hex: "0A0080BAD7C34DA69488", Sequence: (byte)1),
        (Hex: "0A0080BAD7C44EA79586", Sequence: (byte)2)
    };

    foreach (var sample in movementSamples)
    {
        var movementRequest = Convert.FromHexString(sample.Hex);

        Require(
            OfficialWorldMovementCodec.TryDecode(
                movementRequest,
                out var movement),
            "測試移動樣本未通過協定辨識");
        Require(
            movement.Sequence == sample.Sequence,
            $"World 移動 Sequence 錯誤：{movement.Sequence}");

        await worldStream.WriteAsync(movementRequest, timeout.Token);
        Array.Clear(movementRequest);

        var movementAcknowledgement =
            await ReadFrameAsync(worldStream, timeout.Token);

        Require(
            OfficialWorldMovementCodec.TryDecodeAcknowledgement(
                movementAcknowledgement,
                out var acknowledgedSequence),
            "World 移動 ACK 格式錯誤");
        Require(
            acknowledgedSequence == sample.Sequence,
            $"World 移動 ACK Sequence 錯誤：{acknowledgedSequence}");

        Array.Clear(movementAcknowledgement);
    }

    var logoutRequest = Convert.FromHexString("0500AC9D30");
    await worldStream.WriteAsync(logoutRequest, timeout.Token);
    Array.Clear(logoutRequest);

    var eofProbe = new byte[1];
    var bytesRead = await worldStream.ReadAsync(eofProbe, timeout.Token);

    Require(bytesRead == 0, "移動封包後 World 連線狀態異常");
    Console.WriteLine(
        "World 連續移動、雙 ACK 與連線維持測試成功");
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
    var heartbeatCount =
        holdSeconds > 0
            ? holdSeconds
            : 1;

    for (var index = 0; index < heartbeatCount; index++)
    {
        var verifiedHeartbeat =
            Convert.FromHexString("05003D09A5");

        Require(
            OfficialWorldHeartbeatCodec.Classify(
                verifiedHeartbeat) ==
            OfficialWorldFrameClassification.KeepAlive,
            "測試 Heartbeat 樣本未通過協定分類");

        await worldStream.WriteAsync(
            verifiedHeartbeat,
            timeout.Token);

        Array.Clear(verifiedHeartbeat);

        await Task.Delay(
            holdSeconds > 0 ? 1000 : 100,
            timeout.Token);
    }

    Console.WriteLine(
        holdSeconds > 0
            ? $"World 保持在線測試成功：{holdSeconds} 秒"
            : "World 連線持續接收測試成功");
}

return 0;

static async Task PreparePortalTestStateAsync(
    string accountName,
    string password,
    long characterId,
    string expectedCharacterName,
    CancellationToken cancellationToken)
{
    const long sourceMapId = 170015000;
    const int sourceX = 248;
    const int sourceY = 246;

    var dbHost =
        Environment.GetEnvironmentVariable("GOD2_DB_HOST");
    var dbPortText =
        Environment.GetEnvironmentVariable("GOD2_DB_PORT");
    var dbUser =
        Environment.GetEnvironmentVariable("GOD2_DB_USER");
    var dbPassword =
        Environment.GetEnvironmentVariable("GOD2_DB_PASSWORD");

    Require(
        !string.IsNullOrWhiteSpace(dbHost) &&
        !string.IsNullOrWhiteSpace(dbPortText) &&
        !string.IsNullOrWhiteSpace(dbUser) &&
        !string.IsNullOrWhiteSpace(dbPassword),
        "Portal Probe Seeder 需要完整 GOD2_DB_HOST/PORT/USER/PASSWORD。");

    Require(
        int.TryParse(dbPortText, out var dbPort) &&
        dbPort is >= 1 and <= 65535,
        $"Portal Probe Seeder 的 GOD2_DB_PORT 無效：{dbPortText}");

    var options = new MariaDbAuthenticationOptions(
        dbHost!,
        dbPort,
        dbUser!,
        dbPassword!);

    var authenticator =
        new MariaDbAccountAuthenticator(
            options,
            new Pbkdf2Sha256PasswordHashVerifier());

    var authentication =
        await authenticator.ValidateCredentialsAsync(
            accountName,
            password.AsMemory(),
            cancellationToken);

    Require(
        authentication.Succeeded,
        $"Portal Probe Seeder 無法驗證測試帳號：{accountName}");

    if (authentication.AccountId is not long accountId)
    {
        throw new InvalidOperationException(
            $"Portal Probe Seeder 驗證成功但沒有 AccountId：{accountName}");
    }

    var repository =
        new MariaDbCharacterListRepository(options);

    var characters =
        await repository.ListByAccountAsync(
            accountId,
            cancellationToken);

    var matches = characters
        .Where(character =>
            character.CharacterId == characterId)
        .ToArray();

    Require(
        matches.Length == 1,
        $"Portal Probe Seeder 找不到唯一角色：" +
        $"accountId={accountId}; characterId={characterId}; " +
        $"matches={matches.Length}");

    var character = matches[0];

    Require(
        string.Equals(
            character.Name,
            expectedCharacterName,
            StringComparison.Ordinal),
        $"Portal Probe Seeder 角色名稱不符：" +
        $"expected={expectedCharacterName}; actual={character.Name}");

    if (character.MapId is not long currentMapId ||
        character.PositionX is not int currentX ||
        character.PositionY is not int currentY)
    {
        throw new InvalidOperationException(
            "Portal Probe Seeder 角色目前 Map/X/Y 不完整。");
    }

    Require(
        character.RuntimeVersion >= 0,
        $"Portal Probe Seeder runtime_version 無效：" +
        $"{character.RuntimeVersion}");

    Require(
        character.ConcurrencyToken.Length == 32,
        $"Portal Probe Seeder concurrency_token 長度無效：" +
        $"{character.ConcurrencyToken.Length}");

    Console.WriteLine(
        $"Portal Seeder 現況：帳號={accountName} / " +
        $"AccountId={accountId} / 角色={character.Name} / " +
        $"ID={character.CharacterId} / map={currentMapId} / " +
        $"pos=({currentX},{currentY}) / " +
        $"version={character.RuntimeVersion}");

    if (currentMapId == sourceMapId &&
        currentX == sourceX &&
        currentY == sourceY)
    {
        Console.WriteLine(
            $"Portal Seeder：角色已位於來源測試狀態 " +
            $"{sourceMapId} / ({sourceX},{sourceY})，略過寫入.");

        return;
    }

    var writer =
        new MariaDbCharacterMapTransitionWriter(options);

    var result =
        await writer.TryUpdateAsync(
            new CharacterMapTransitionWriteRequest(
                character.CharacterId,
                sourceMapId,
                sourceX,
                sourceY,
                character.RuntimeVersion,
                character.ConcurrencyToken),
            cancellationToken);

    Require(
        result.Updated,
        $"Portal Probe Seeder CAS 衝突：" +
        $"character={character.CharacterId}; " +
        $"expectedVersion={character.RuntimeVersion}");

    Console.WriteLine(
        $"Portal Seeder 完成：" +
        $"{currentMapId}/({currentX},{currentY}) -> " +
        $"{sourceMapId}/({sourceX},{sourceY}); " +
        $"version={character.RuntimeVersion}->{result.RuntimeVersion}");
}

static void BindLocalAddress(
    TcpClient client,
    string? addressText)
{
    if (string.IsNullOrWhiteSpace(addressText))
    {
        return;
    }

    if (!System.Net.IPAddress.TryParse(
            addressText,
            out var address) ||
        address.AddressFamily !=
            System.Net.Sockets.AddressFamily.InterNetwork)
    {
        throw new InvalidOperationException(
            $"無效的 Probe 來源 IPv4：{addressText}");
    }

    client.Client.Bind(
        new System.Net.IPEndPoint(address, 0));
}

static async Task VerifyDuplicateLoginAsync(
    int port,
    byte[] loginRequest,
    CancellationToken cancellationToken)
{
    using var duplicateClient = new TcpClient();

    duplicateClient.Client.Bind(
        new System.Net.IPEndPoint(
            System.Net.IPAddress.Parse("127.0.0.2"),
            0));

    await duplicateClient.ConnectAsync(
        "127.0.0.1",
        port,
        cancellationToken);

    await using var duplicateStream =
        duplicateClient.GetStream();

    var serverHandshake =
        await ReadFrameAsync(
            duplicateStream,
            cancellationToken);

    try
    {
        Require(
            serverHandshake.AsSpan().SequenceEqual(
                OfficialLoginHandshakeProtocol
                    .ServerHandshakeFrame
                    .Span),
            "第二次登入 Server Handshake 不符");
    }
    finally
    {
        Array.Clear(serverHandshake);
    }

    await duplicateStream.WriteAsync(
        OfficialLoginHandshakeProtocol
            .ExpectedClientHandshakeFrame,
        cancellationToken);

    var versionFollowUp =
        await ReadFrameAsync(
            duplicateStream,
            cancellationToken);

    try
    {
        Require(
            versionFollowUp.AsSpan().SequenceEqual(
                OfficialLoginHandshakeProtocol
                    .VersionFollowUpFrame
                    .Span),
            "第二次登入 Version Follow-up 不符");
    }
    finally
    {
        Array.Clear(versionFollowUp);
    }

    await duplicateStream.WriteAsync(
        loginRequest,
        cancellationToken);

    var duplicateResponse =
        await ReadFrameAsync(
            duplicateStream,
            cancellationToken);

    var expectedFailure =
        OfficialLoginResponseCodec.EncodeFailure(
            OfficialLoginFailureCode.DuplicateLogin);

    try
    {
        Require(
            duplicateResponse.AsSpan().SequenceEqual(
                expectedFailure),
            "Pending 期間第二次登入未收到 DuplicateLogin");
    }
    finally
    {
        Array.Clear(duplicateResponse);
        Array.Clear(expectedFailure);
    }
}

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
