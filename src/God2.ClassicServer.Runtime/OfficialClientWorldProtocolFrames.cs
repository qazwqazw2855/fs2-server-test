using System.Buffers.Binary;
using God2.ClassicServer.Protocol;

namespace God2.ClassicServer.Runtime;

public static class OfficialClientWorldProtocolFrames
{
    public const int LegacyPlayerNameFieldLength = 12;
    // The current-build 0x1F player bootstrap keeps the character identifier at
    // offset 19, so the fixed name/padding field remains bytes 7..18 (12 bytes).
    // Bytes after the first NUL were non-deterministic in retained Client frames
    // and must not be mistaken for a shorter name field: doing so made valid
    // 8-11 byte names fall back to the legacy mounted appearance profile.
    public const int CurrentCreatePlayerNameFieldLength = 12;
    public const int MaximumProductionCharacterNameLength = LegacyPlayerNameFieldLength - 1;
    public enum OfficialWorldMovementMode
    {
        Unknown,
        DeferredUntilOfficialWalkingEvidence,
        VisualMountedBaseline
    }

    public enum OfficialWorldDirection
    {
        Unknown,
        North,
        South,
        East,
        West,
        NorthEast,
        NorthWest,
        SouthEast,
        SouthWest
    }

    public readonly record struct OfficialWorldMovementCandidate(
        string EncodedHex,
        string DecodedHex,
        byte Sequence,
        ushort XCandidate,
        ushort YCandidate,
        byte StateCandidate,
        OfficialWorldMovementMode MovementMode,
        OfficialWorldDirection Direction);

    public readonly record struct OfficialCharacterCreateCandidate(
        string Name,
        string Class,
        string Gender,
        string LifeSkill,
        string Appearance,
        string SelectionBytesHex,
        string DecodedHex);

    public readonly record struct OfficialWorldBootstrapFrame(
        int Index,
        int Offset,
        int Length,
        byte EncodedOpcodeCandidate,
        string Purpose);

    public readonly record struct OfficialWorldPlayerSpawn(
        byte Opcode,
        string CharacterName,
        uint CharacterIdCandidate,
        byte AppearanceModeCandidate,
        byte StateFlagsCandidate,
        OfficialWorldMovementMode MovementMode);

    public const string EvidenceId = "World-ba1f987593024d32be06b5485b16c9ab-server-to-client";
    public const string TargetClientBuild = "God2_opt official client world stream captured from successful WorldConnectionOrIdle evidence";
    public const byte WorldMovementOpcode = 0x2E;
    public const byte WorldMovementAcknowledgementOpcode = 0x5D;
    public const byte WorldMovementMountedState = 0xFF;
    public const int WorldMovementFrameLength = 10;
    public const int WorldMovementAcknowledgementFrameLength = 5;

    private static readonly byte[] WorldServerToClientStream =
        Convert.FromHexString(
            "1300FD9FCAC842A869FEA1BEC83A23DACF52490600A96DB7E880008FB939F4B46A74EE3D2AC53720D7CC4F4F6AA5B5542B7319434EFCCC5C0EE6405306F74BF821C3B9D63B773780CFC56F7B867352191F72607AAC6D9581203DE3709B771997145B5D8274E30FAFD25D972DA2084F9AF282736A8B3955FE49F8041C4A496C91E197D8F85260CB9781AE61A97DCB98E5DA8A118D2BDFB5A5914001AC9EC7C53FA566FB9EBBC53720D7CC4F4F96C863F8287333C6277C4C5703FF595508F749F79E40DA355DCD0FBF3F93A7C22A7614990813487A444377C3F939D4C93C6981E9BE5B5D8273E40DAFD35A982CD6ACB9937A265D8B1B3F55FE62FAD7DC843866FDD1A74F8492D07A44F63523F250A798E5BF98F4D62A6FEEEB3914741F2902C052A767226B42681C85F0EA3BEDE1C2BEEFC91B2D391D544C55B3424FBF29732578288B4909126669F7E88AE6E138A03045D4DD94A3BC04607058C0167A51D0D02C26AFC4EED433DFEB878FAF0205D9134092F0C66039BCF9A2153EEA3B98B97A3CF893C57D87DA8C0B35D281398FA1A6554A1A4E213B9EAA9D769801C9C3D1C53FA566FB9E62A40358E8F25A4F96C863F828AAF07F86CBF1680EE6405308F794D719F78A02493EDE9FDFA4729D444F4CE8F9172E7A444377DA0EF002837B93C53FB184FD9E85FB3720129B4F4F125D01720D9C1E434EF4D45C0EE8425308F84AF81DBFB9D63C42E29FDFA47278657314D7C2092E7A444686DA173DE3709B791F9714496F8273DF12AFD375BD2CD6575D4451FD58B4153991CA70E4E91284384D52147BBA8A889C44F4A50623371FA798C7DF9AF4D630C432F60D3750349DA3F8335D2DFB944C662B53C02A4A553BCFD326F21FF50AEA4E4F56B35B6AE81D8F26421DDAC01212589BC61B6C4CF3028FC610D4DD88BDBE0474686C88FE6F4CD0D02C26ADC50F3124D3DCCC46F10F06EC4540257FCB5B65EBF2A2153EEA3B9864BF64C0A49C7587DA8C0BD8DA8964B340C62D4192D6213B2632789782F3FA9CC7C53FA5481D9EBBC53720D7E2794F96F6959A8E6A34434EC9FF3773DB4B56520049F81D4F16C37949DE9FDFA47278657314D7C20952DD505877EE4396BC709B771997145B5D8273E4F438A678D20BF87B74FE53FE6A7E153B57FE70E4C3E49E4167FDF679BA3FB99C44E88B0423F250A79D21E5BDF41305BD14C95F63431F2A02DA33A9781A9476302B53EFEA3B533BC1BD26F21FF52711524C55790C4FBFF49C1940174176243F8AAE08E7614DE6FFBA3141D4DDB6D1BC04B5BD8FE12546AD2828CC23BDC42734AD604430F9AE776DD599567A9C11A98B1F2EBD5937E3914C9103B24DB87BE5EBC3198554D155A5BB840D5764BF50BB50C506ACD9D2E21334E614B72D9AD3F9C61B8C8028AE7475F918679974AB3D434EFD2C9CEAE24F53088F9D00C165CA2273A00203BE2F80678784AEE40ACD88A9B75087D2BFDDE37044501997145B5D8288F11DB77CFB982CFD54ED7851FD6A7EE2266506D686D7DC5B1166FDF679BA3F3E8954E032A723F2878098E5BF98F4D6CDB31FD7A5C7431FF2DBD933A961FB94811D3B5B5C8F3B53E49ABD26F21FF50A9C24275AB3C7BC21F41B74127AA809465B2DAFE7F8212096068F10F674DD924BBE6D865E25D16C8C4EEEB0F1E7B5C43C6A7F8EDDB4CB22D3CA965E0795FD165682B604A2948F2C3B372BB5710D63D5C0AE1490279444000E0078D2C7C53FB6CCFB77BBC5282D003C9384CB3FBADB9B9EF9613991248B189AD1127714482AD2494EE7812CE3AFE82569440EB16882FFF65C02190078D2C7C53FB600FB77BBC50956D7CC4F405CE63CF828CE0E0078D2C7C53FB6CCFB77BBC5282D003C9384CB3FBADB9B9E19C13991248B189ADD0E4318482AD2494EE78184CF53482569440EB1688A07C056B02D003C9384CB3FBADB9B9EF9613991248B189AD5167912482AD2494EE7812CE3AFE82569440EB16882FFBE547C2D003C9384CB3FBADB9B9E59013991248B189ADD0E7B10482AD2494EE78184CF13882569440EB1688E0BBE542A2D003C9384CB3FBADB9B9ED98139912400189AC91A7B10482AD2494EE78184CF93082569440EB1688603C45A342D003C9384CB3F00DB9B9E79E13991248B189AD1127516482AD2494EE78184CF33682569440EB168920FC0567A2D003C9384CB3FBADB9B9EF9613991248B189ACD1E7912482A00494EE7819CBBB7E82569440EB16882FFF65C642D003C9384CB3FBADB9B9E79E13991248B189ADD0E4300482AD2494EE7812CE32F682569440EB1688A07C258062D003C9384CB3FBADB9B9EF9613991248B189AD5167714482AD2494EE78154F7ABE82569440EB1688603C2580007004DF895246D0E0078D2C7C53FB6CCFB77BBC5282D003C9384CB3FBADB9B9E59013991248B189AD1127714482AD2494EE7812CE30F882569440EB1688E0BBE54C62D003C9384CB3FBADB9B9EF9613991248B189AC91A0010482AD2494EE7812CE3AFE82569440EB1688603C2580609");

    private static readonly byte[] WorldCipherKey =
        Convert.FromHexString(
            "FD9FCAC842A869FEA1BEC83A23DACF525299CB66FB2B761C4651FFCF5F11E943560BFA4CFB20C2BCD93F41E1A2E2A7757B687617DAC50C317D47467ADD1A40E6739E7A1C9A175E608576E710B2D65D9B2FD97EF07B54006D81183C580173E7DADF873B6900F97CBD42BC9F47EB8E0726F553AA9BE8C29BF7D92DA912D2042646222C05DC36AC64FE9779332E56F2ED3E563EC4C029F522F80DED554F58B64552C2F79F1C431A5E490C15696CFAEB560AD803920644D7E097A6BF07B89C6F8B28494FD3D32F29B2C72AF80AAADF5F8CB20508DC16434E1CFD2B0D89F5A51841ED3E9B67A603C3A79F788ADD8F0E38D55F65B66C982E441D51243EA1AD7B9AAFCC");

    private static readonly byte[] CurrentCreateWorldCipherKey =
        Convert.FromHexString(
            "D0E113FFA0BCDADBBBA6B1DE5FF06F0D9FEC6E4F1A8362674448F677C7887A62A35E9D351A78AE07D73638890A593894C8BB1900F91DF87C7B3E3D224591D105C0F11D05B96F4AAB836DDEB81A4DEEBA7C2C21D99AACECB87F0F330069EA78F92CDADE521F51680840B396EF5305984542A64D84071A8742D724A0BA3A7BB7656F7FA8C55504504995702AD6BE697E5DA39167A9484D0E430BE44CF7C02DD6710F4A420562724A940A0C60146262E729255635EF632FCCE2A4B6FE6004E61C4796A276BC4E819E1228EF015247D61DD1525B7FFF62A608482904809D0D8FD20C8BEE0A8F221B93EA7681D43776AF667EB2090F814D9C099C22359855E31140EB");

    private static readonly byte[] CurrentCreateWorldServerHandshake =
        Convert.FromHexString("1300D0E113FFA0BCDADBBBA6B1DE5FF06F0D9F");

    private static readonly byte[] CurrentCreateWorldClientHandshake =
        Convert.FromHexString("1300E826BF0CF8A53D5447D2538C3D9CA6D295");

    private static readonly byte[] CurrentCreateWorldFirstFollowUp =
        Convert.FromHexString("06001A0F00D9");

    private static readonly byte[][] LegacyStaticEntityRecords =
    [
        Convert.FromHexString("72E00500008F62000081EA680FC2034A0146001000"),
        Convert.FromHexString("72BB06000015A0000002EA680FC2034A0146001E00"),
        Convert.FromHexString("60BB06000015A0E80302EA680FC2034A014A001C00"),
        Convert.FromHexString("721E1200008F420000A1EA680FC2034A013A001C00")
    ];

    // Existing current-build official evidence from the character-delete/create
    // session. The newly-created character reached the world and produced this
    // complete decoded 128-byte 0x1F frame. Raw network bytes remain in the
    // restricted historical trace. Decoded SHA-256:
    // CCC098F41F68D2C2836951935FBA067900CEB93A9FF98433FE9E3B2392E8CAF8.
    private static readonly byte[] CurrentCreateProfilePlayerSpawnDecoded =
        Convert.FromHexString(
            "80001F010000006B65726F00000000802E0E740800000000000000000000000000000001060000000000000200000000FC062BC1E1FD8114FB062B6C512C0100000000000000000000000001020000030030017CF135772A5BE175000000003F5BE17506A971B248A65D09488A4B1E48A65D092400000005E0FD8160FB062BB0");

    public static byte[] BuildWorldServerHandshake() => WorldServerToClientStream[..19].ToArray();

    public static byte[] BuildCurrentCreateWorldServerHandshake() => CurrentCreateWorldServerHandshake.ToArray();

    public static byte[] BuildCurrentCreateWorldClientHandshake() => CurrentCreateWorldClientHandshake.ToArray();

    public static byte[] BuildCurrentCreateWorldFirstFollowUp() => CurrentCreateWorldFirstFollowUp.ToArray();

    public static byte[] BuildWorldBootstrapAfterHandshake() => WorldServerToClientStream[19..].ToArray();

    public static byte[] BuildWorldBootstrapAfterHandshake(CharacterSummary character) =>
        BuildWorldBootstrapAfterHandshake(character, clientAreaId: 4);

    public static byte[] BuildWorldBootstrapAfterHandshake(CharacterSummary character, byte clientAreaId)
    {
        ArgumentNullException.ThrowIfNull(character);
        var bootstrap = BuildWorldBootstrapAfterHandshake();
        var playerSpawn = BuildActiveWorldPlayerSpawnFrame128(character);
        playerSpawn.CopyTo(bootstrap, 6);
        if (character.MapId == 19 && character.PositionX == 28 && character.PositionY == 34)
        {
            return bootstrap;
        }

        var restoration = OfficialPortalWireCodec.SerializeWorldProjectionDestination(
            OfficialPortalWireCodec.ClientBuildId,
            checked((ushort)character.MapId),
            clientAreaId,
            character.PositionX,
            character.PositionY);
        if (!restoration.Succeeded || restoration.Value is null)
        {
            throw new ArgumentException(
                "The persisted official world location could not be serialized with verified map and position-consumer evidence.",
                nameof(character));
        }

        var orderedFrames = restoration.Value.OrderedDecodedFrames
            .Select(frame => EncodeWorldServerPayload(frame.Span))
            .ToArray();
        var result = new byte[bootstrap.Length + orderedFrames.Sum(frame => frame.Length)];
        bootstrap.CopyTo(result, 0);
        var offset = bootstrap.Length;
        foreach (var frame in orderedFrames)
        {
            frame.CopyTo(result, offset);
            offset += frame.Length;
            Array.Clear(frame);
        }
        Array.Clear(bootstrap);
        return result;
    }

    public static byte[] BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(CharacterSummary character)
        => BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(character, clientAreaId: 4, []);

    public static byte[] BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(
        CharacterSummary character,
        IReadOnlyList<NpcState> initialNpcs)
        => BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(character, clientAreaId: 4, initialNpcs);

    public static byte[] BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(
        CharacterSummary character,
        byte clientAreaId,
        IReadOnlyList<NpcState> initialNpcs)
        => BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(
            character,
            clientAreaId,
            initialNpcs,
            []);

    public static byte[] BuildMariaDbAuthoritativeWorldBootstrapAfterHandshake(
        CharacterSummary character,
        byte clientAreaId,
        IReadOnlyList<NpcState> initialNpcs,
        IReadOnlyList<OfficialOwnedImmortalWireState> ownedImmortals)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(initialNpcs);
        ArgumentNullException.ThrowIfNull(ownedImmortals);
        var bootstrap = BuildWorldBootstrapAfterHandshake(character, clientAreaId);
        const int staticFrameOffset = 454;
        const int legacyStaticFrameLength = 752;
        const int entityTailLength = 84;

        if (bootstrap.Length < staticFrameOffset + legacyStaticFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(bootstrap.AsSpan(staticFrameOffset, sizeof(ushort))) != legacyStaticFrameLength)
        {
            Array.Clear(bootstrap);
            throw new InvalidOperationException("Frozen world bootstrap static-frame identity no longer matches the M3 authority cutover evidence.");
        }

        var decodedStatic = DecodeWorldServerPayload(bootstrap.AsSpan(staticFrameOffset, legacyStaticFrameLength));
        var recordOffset = 667;
        foreach (var record in LegacyStaticEntityRecords)
        {
            if (!decodedStatic.AsSpan(recordOffset, record.Length).SequenceEqual(record))
            {
                Array.Clear(decodedStatic);
                Array.Clear(bootstrap);
                throw new InvalidOperationException("Legacy world entity tail no longer matches the build-pinned application records.");
            }

            recordOffset += record.Length;
        }

        const int playerGodProgressOffset = 2;
        ReadOnlySpan<byte> frozenPlayerGodProgress =
            [0x2B, 0xCC, 0x00, 0x00, 0x00, 0x1C, 0x02, 0x00, 0x00, 0x36, 0x00,
             0x00, 0x00, 0xCF, 0x00, 0x00, 0x00, 0x8C, 0x1F, 0x83, 0x09];
        if (!decodedStatic.AsSpan(
                playerGodProgressOffset,
                PublicBetaCompatibilityGameplayWireAdapter.PlayerGodProgressRecordLength)
            .SequenceEqual(frozenPlayerGodProgress))
        {
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(
                "Frozen world bootstrap player/god progress record no longer matches the exact-build evidence.");
        }

        var frozenProgress = decodedStatic.AsSpan(
            playerGodProgressOffset,
            PublicBetaCompatibilityGameplayWireAdapter.PlayerGodProgressRecordLength);
        uint playerCurrentHp;
        uint playerCurrentMp;
        try
        {
            playerCurrentHp = character.CurrentHitPoints.HasValue
                ? checked((uint)character.CurrentHitPoints.Value)
                : BinaryPrimitives.ReadUInt32LittleEndian(frozenProgress[1..]);
            playerCurrentMp = character.CurrentMagicPoints.HasValue
                ? checked((uint)character.CurrentMagicPoints.Value)
                : BinaryPrimitives.ReadUInt32LittleEndian(frozenProgress[9..]);
        }
        catch (OverflowException exception)
        {
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(
                "MariaDB player HP/MP cannot be represented by the exact current-build 0x2B fields.",
                exception);
        }

        var godCurrentHp = ownedImmortals.Count == 1
            ? checked((uint)ownedImmortals[0].CurrentHp)
            : BinaryPrimitives.ReadUInt32LittleEndian(frozenProgress[5..]);
        var godCurrentMp = ownedImmortals.Count == 1
            ? checked((uint)ownedImmortals[0].CurrentMp)
            : BinaryPrimitives.ReadUInt32LittleEndian(frozenProgress[13..]);
        var progressRecord = PublicBetaCompatibilityGameplayWireAdapter.EncodePlayerGodProgress(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            new OfficialPlayerGodProgressLayout(
                playerCurrentHp,
                godCurrentHp,
                playerCurrentMp,
                godCurrentMp,
                BinaryPrimitives.ReadUInt16LittleEndian(frozenProgress[17..]),
                BinaryPrimitives.ReadUInt16LittleEndian(frozenProgress[19..])));
        if (!progressRecord.LayoutDecoded || progressRecord.Value is null)
        {
            Array.Clear(progressRecord.Value ?? []);
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(
                progressRecord.FailureCode ?? "Player/god progress compatibility projection failed.");
        }

        progressRecord.Value.CopyTo(decodedStatic, playerGodProgressOffset);
        Array.Clear(progressRecord.Value);

        const int playerVitalsOffset = 84;
        ReadOnlySpan<byte> frozenPlayerVitals =
            [0x24, 0xCC, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00,
             0xCC, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00];
        if (!decodedStatic.AsSpan(
                playerVitalsOffset,
                PublicBetaCompatibilityGameplayWireAdapter.PlayerVitalsRecordLength)
            .SequenceEqual(frozenPlayerVitals))
        {
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(
                "Frozen world bootstrap player-vitals record no longer matches the exact-build evidence.");
        }

        if (character.CurrentHitPoints.HasValue && character.CurrentMagicPoints.HasValue &&
            character.MaximumHitPoints.HasValue && character.MaximumMagicPoints.HasValue)
        {
            OfficialPlayerSnapshotWireResult<byte[]> playerVitalsRecord;
            try
            {
                playerVitalsRecord = PublicBetaCompatibilityGameplayWireAdapter.EncodePlayerVitals(
                    OfficialBattleCommandWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    new OfficialPlayerVitalsLayout(
                        checked((uint)character.CurrentHitPoints.Value),
                        checked((uint)character.CurrentMagicPoints.Value),
                        checked((uint)character.MaximumHitPoints.Value),
                        checked((uint)character.MaximumMagicPoints.Value)));
            }
            catch (OverflowException exception)
            {
                Array.Clear(decodedStatic);
                Array.Clear(bootstrap);
                throw new InvalidOperationException(
                    "MariaDB player vitals cannot be represented by the exact current-build 0x24 fields.",
                    exception);
            }

            if (!playerVitalsRecord.LayoutDecoded || playerVitalsRecord.Value is null)
            {
                Array.Clear(playerVitalsRecord.Value ?? []);
                Array.Clear(decodedStatic);
                Array.Clear(bootstrap);
                throw new InvalidOperationException(
                    playerVitalsRecord.FailureCode ?? "Player-vitals compatibility projection failed.");
            }

            playerVitalsRecord.Value.CopyTo(decodedStatic, playerVitalsOffset);
            Array.Clear(playerVitalsRecord.Value);
        }

        const int playerStatusOffset = 118;
        ReadOnlySpan<byte> frozenPlayerStatus =
            [0x22, 0x02, 0x00, 0x00, 0x00, 0x1E, 0x00, 0x27, 0x00,
             0x14, 0x00, 0x15, 0x00, 0x8C, 0x1F, 0x00, 0x00,
             0xCC, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00];
        if (!decodedStatic.AsSpan(
                playerStatusOffset,
                PublicBetaCompatibilityGameplayWireAdapter.PlayerStatusRecordLength)
            .SequenceEqual(frozenPlayerStatus))
        {
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(
                "Frozen world bootstrap player-status record no longer matches the exact-build evidence.");
        }

        if (character.MaximumHitPoints.HasValue && character.MaximumMagicPoints.HasValue &&
            character.RemainingStatPoints.HasValue && character.Constitution.HasValue &&
            character.Strength.HasValue && character.Intelligence.HasValue && character.Speed.HasValue)
        {
            OfficialPlayerSnapshotWireResult<byte[]> playerStatusRecord;
            try
            {
                playerStatusRecord = PublicBetaCompatibilityGameplayWireAdapter.EncodePlayerStatus(
                    OfficialBattleCommandWireCodec.ClientBuildId,
                    GameplayProtocolState.World,
                    new OfficialPlayerStatusLayout(
                        checked((sbyte)character.Level),
                        frozenPlayerStatus[2],
                        checked((ushort)character.RemainingStatPoints.Value),
                        checked((ushort)character.Constitution.Value),
                        checked((ushort)character.Strength.Value),
                        checked((ushort)character.Intelligence.Value),
                        checked((ushort)character.Speed.Value),
                        BinaryPrimitives.ReadUInt16LittleEndian(frozenPlayerStatus[13..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(frozenPlayerStatus[15..]),
                        checked((uint)character.MaximumHitPoints.Value),
                        checked((uint)character.MaximumMagicPoints.Value)));
            }
            catch (OverflowException exception)
            {
                Array.Clear(decodedStatic);
                Array.Clear(bootstrap);
                throw new InvalidOperationException(
                    "MariaDB player status cannot be represented by the exact current-build 0x22 fields.",
                    exception);
            }

            if (!playerStatusRecord.LayoutDecoded || playerStatusRecord.Value is null)
            {
                Array.Clear(playerStatusRecord.Value ?? []);
                Array.Clear(decodedStatic);
                Array.Clear(bootstrap);
                throw new InvalidOperationException(
                    playerStatusRecord.FailureCode ?? "Player-status compatibility projection failed.");
            }

            playerStatusRecord.Value.CopyTo(decodedStatic, playerStatusOffset);
            Array.Clear(playerStatusRecord.Value);
        }

        const int immortalRecordOffset = 143;
        const int immortalRecordLength = OfficialImmortalReplicationWireCodec.FrozenLoginApplicationRecordLength;
        if (!OfficialImmortalReplicationWireCodec.IsFrozenLoginApplicationRecord(
                decodedStatic.AsSpan(immortalRecordOffset, immortalRecordLength)))
        {
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(
                "Frozen world bootstrap Wuji application-record identity no longer matches the exact-build evidence.");
        }

        ReadOnlySpan<int> vitalRecordOffsets = [101, 176];
        foreach (var vitalRecordOffset in vitalRecordOffsets)
        {
            if (!OfficialImmortalReplicationWireCodec.IsFrozenVitalApplicationRecord(
                    decodedStatic.AsSpan(
                        vitalRecordOffset,
                        OfficialImmortalReplicationWireCodec.FrozenVitalApplicationRecordLength)))
            {
                Array.Clear(decodedStatic);
                Array.Clear(bootstrap);
                throw new InvalidOperationException(
                    "Frozen world bootstrap vital application-record identity no longer matches the exact-build evidence.");
            }
        }

        const int immortalCatalogSelectorOffset = 172;
        const int immortalUiFlagsOffset = 174;
        if (!decodedStatic.AsSpan(
                immortalCatalogSelectorOffset,
                PublicBetaCompatibilityGameplayWireAdapter.ImmortalCatalogSelectorRecordLength)
                .SequenceEqual([PublicBetaCompatibilityGameplayWireAdapter.ImmortalCatalogSelectorOpcode, (byte)0x00]) ||
            !decodedStatic.AsSpan(
                immortalUiFlagsOffset,
                PublicBetaCompatibilityGameplayWireAdapter.ImmortalUiFlagsRecordLength)
                .SequenceEqual([PublicBetaCompatibilityGameplayWireAdapter.ImmortalUiFlagsOpcode, (byte)0x00]))
        {
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(
                "Frozen world bootstrap immortal selector/UI records no longer match the exact-build evidence.");
        }

        var catalogSelectorRecord = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalCatalogSelector(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decodedStatic[immortalCatalogSelectorOffset + 1]);
        var uiFlagsRecord = PublicBetaCompatibilityGameplayWireAdapter.EncodeImmortalUiFlags(
            OfficialBattleCommandWireCodec.ClientBuildId,
            GameplayProtocolState.World,
            decodedStatic[immortalUiFlagsOffset + 1]);
        if (!catalogSelectorRecord.LayoutDecoded || catalogSelectorRecord.Value is null ||
            !uiFlagsRecord.LayoutDecoded || uiFlagsRecord.Value is null)
        {
            Array.Clear(catalogSelectorRecord.Value ?? []);
            Array.Clear(uiFlagsRecord.Value ?? []);
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(
                catalogSelectorRecord.FailureCode ?? uiFlagsRecord.FailureCode ??
                "Immortal selector/UI compatibility projection failed.");
        }

        catalogSelectorRecord.Value.CopyTo(decodedStatic, immortalCatalogSelectorOffset);
        uiFlagsRecord.Value.CopyTo(decodedStatic, immortalUiFlagsOffset);
        Array.Clear(catalogSelectorRecord.Value);
        Array.Clear(uiFlagsRecord.Value);

        var immortalRecord = OfficialImmortalReplicationWireCodec.BuildAuthoritativeLoginApplicationRecord(ownedImmortals);
        if (!immortalRecord.Succeeded || immortalRecord.Value is null)
        {
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(immortalRecord.Error?.Message ?? "Owned immortal login projection failed.");
        }

        var vitalRecord = OfficialImmortalReplicationWireCodec.BuildAuthoritativeVitalApplicationRecord(ownedImmortals);
        if (!vitalRecord.Succeeded || vitalRecord.Value is null)
        {
            Array.Clear(immortalRecord.Value);
            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw new InvalidOperationException(vitalRecord.Error?.Message ?? "Owned immortal vital projection failed.");
        }

        var appendNpcsAfterMapRestoration = character.MapId != 19;
        var embeddedRecords = new List<byte[]>(initialNpcs.Count);
        var postRestorationFrames = new List<byte[]>(initialNpcs.Count);
        try
        {
            foreach (var npc in initialNpcs)
            {
                var serialized = OfficialNpcReplicationWireCodec.SerializeSpawn(npc);
                if (serialized.Status != OfficialSerializerStatus.Ready || serialized.Frame.Length != 24)
                {
                    throw new InvalidOperationException($"Initial NPC cannot be embedded in the authoritative world bootstrap: {serialized.Reason}");
                }

                if (appendNpcsAfterMapRestoration)
                {
                    postRestorationFrames.Add(serialized.Frame.ToArray());
                }
                else
                {
                    var decodedSpawn = DecodeWorldServerPayload(serialized.Frame);
                    embeddedRecords.Add(decodedSpawn.AsSpan(2, decodedSpawn.Length - 3).ToArray());
                    Array.Clear(decodedSpawn);
                }
                Array.Clear(serialized.Frame);
            }
        }
        catch
        {
            foreach (var record in embeddedRecords)
            {
                Array.Clear(record);
            }
            foreach (var frame in postRestorationFrames)
            {
                Array.Clear(frame);
            }

            Array.Clear(decodedStatic);
            Array.Clear(bootstrap);
            throw;
        }

        const int staticPrefixLength = legacyStaticFrameLength - entityTailLength - 1;
        var authoritativePrefixLength = immortalRecord.Value.Length == 0
            ? staticPrefixLength - immortalRecordLength
            : staticPrefixLength;
        var authoritativePrefix = new byte[authoritativePrefixLength];
        if (immortalRecord.Value.Length == 0)
        {
            decodedStatic.AsSpan(0, immortalRecordOffset).CopyTo(authoritativePrefix);
            decodedStatic.AsSpan(
                    immortalRecordOffset + immortalRecordLength,
                    staticPrefixLength - immortalRecordOffset - immortalRecordLength)
                .CopyTo(authoritativePrefix.AsSpan(immortalRecordOffset));
        }
        else
        {
            decodedStatic.AsSpan(0, staticPrefixLength).CopyTo(authoritativePrefix);
            immortalRecord.Value.CopyTo(authoritativePrefix, immortalRecordOffset);
            foreach (var vitalRecordOffset in vitalRecordOffsets)
            {
                vitalRecord.Value.CopyTo(authoritativePrefix, vitalRecordOffset);
            }
        }
        Array.Clear(immortalRecord.Value);
        Array.Clear(vitalRecord.Value);

        var authoritativeStaticFrameLength = authoritativePrefixLength + embeddedRecords.Sum(record => record.Length) + 1;
        var authoritativeStatic = new byte[authoritativeStaticFrameLength];
        authoritativePrefix.CopyTo(authoritativeStatic, 0);
        Array.Clear(authoritativePrefix);
        var embeddedOffset = authoritativePrefixLength;
        foreach (var record in embeddedRecords)
        {
            record.CopyTo(authoritativeStatic, embeddedOffset);
            embeddedOffset += record.Length;
            Array.Clear(record);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(authoritativeStatic, checked((ushort)authoritativeStaticFrameLength));
        authoritativeStatic[^1] = OfficialLoginWireTransform.ComputeChecksum(authoritativeStatic);
        var encodedStatic = EncodeWorldServerPayload(authoritativeStatic);

        var coreLength = bootstrap.Length - legacyStaticFrameLength + authoritativeStaticFrameLength;
        var result = new byte[coreLength + postRestorationFrames.Sum(frame => frame.Length)];
        bootstrap.AsSpan(0, staticFrameOffset).CopyTo(result);
        encodedStatic.CopyTo(result, staticFrameOffset);
        bootstrap.AsSpan(staticFrameOffset + legacyStaticFrameLength).CopyTo(
            result.AsSpan(staticFrameOffset + authoritativeStaticFrameLength));
        var postRestorationOffset = coreLength;
        foreach (var frame in postRestorationFrames)
        {
            frame.CopyTo(result, postRestorationOffset);
            postRestorationOffset += frame.Length;
            Array.Clear(frame);
        }

        Array.Clear(decodedStatic);
        Array.Clear(authoritativeStatic);
        Array.Clear(encodedStatic);
        Array.Clear(bootstrap);
        return result;
    }

    public static byte[] BuildWorldFirstFollowUp() => WorldServerToClientStream[19..25].ToArray();

    public static byte[] BuildGoldBalanceFrame(uint balance)
    {
        Span<byte> decoded = stackalloc byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, checked((ushort)decoded.Length));
        decoded[2] = 0x27;
        BinaryPrimitives.WriteUInt32LittleEndian(decoded[3..], balance);
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        return EncodeWorldServerPayload(decoded);
    }

    public static byte[] BuildPlayerSpawnFrame128() => BuildWorldBootstrapAfterHandshake().AsSpan(6, 128).ToArray();

    public static byte[] BuildPlayerSpawnFrame128(CharacterSummary character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return BuildPlayerSpawnFrame128(character.CharacterId, character.Name);
    }

    public static byte[] BuildPlayerSpawnFrame128(long characterId, string characterName)
    {
        var decoded = DecodeWorldServerFrame(BuildPlayerSpawnFrame128());
        ProjectPlayerIdentity(decoded, characterId, characterName, LegacyPlayerNameFieldLength);
        return EncodeWorldServerFrame(decoded);
    }

    public static byte[] BuildCurrentCreateProfilePlayerSpawnFrame128(long characterId, string characterName)
    {
        var decoded = CurrentCreateProfilePlayerSpawnDecoded.ToArray();
        ProjectPlayerIdentity(decoded, characterId, characterName, CurrentCreatePlayerNameFieldLength);
        return EncodeWorldServerFrame(decoded, CurrentCreateWorldCipherKey, 0x4C);
    }

    public static byte[] BuildActiveWorldPlayerSpawnFrame128(CharacterSummary character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (IsVerifiedCleanFemaleSwordsmanProfile(character))
        {
            var decoded = CurrentCreateProfilePlayerSpawnDecoded.ToArray();
            ProjectPlayerIdentity(decoded, character.CharacterId, character.Name, CurrentCreatePlayerNameFieldLength);

            // The recovered profile used another handshake; active sessions must use
            // the current world stream cipher around the verified plaintext frame.
            return EncodeWorldServerFrame(decoded);
        }

        return BuildPlayerSpawnFrame128(character);
    }

    private static bool IsVerifiedCleanFemaleSwordsmanProfile(CharacterSummary character) =>
        string.Equals(character.Class, "Swordsman", StringComparison.Ordinal) &&
        string.Equals(character.Gender, "Female", StringComparison.Ordinal) &&
        string.Equals(character.Appearance, "Appearance1", StringComparison.Ordinal) &&
        character.Name.Length < CurrentCreatePlayerNameFieldLength;

    private static void ProjectPlayerIdentity(byte[] decoded, long characterId, string characterName, int nameFieldLength)
    {
        if (characterId is <= 0 or > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), "The official player-spawn identifier must fit an unsigned 32-bit field.");
        }

        ArgumentNullException.ThrowIfNull(characterName);
        if (characterName.Any(value => value is < '\u0021' or > '\u007E'))
        {
            throw new ArgumentException("The official player-spawn name must contain only printable ASCII characters.", nameof(characterName));
        }

        var name = System.Text.Encoding.ASCII.GetBytes(characterName);
        if (name.Length < 1 || name.Length >= nameFieldLength)
        {
            throw new ArgumentException(
                $"The official player-spawn name must contain 1-{nameFieldLength - 1} printable ASCII bytes.",
                nameof(characterName));
        }

        decoded.AsSpan(7, nameFieldLength).Clear();
        name.CopyTo(decoded, 7);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            decoded.AsSpan(19, sizeof(uint)),
            checked((uint)characterId));
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
    }

    public static IReadOnlyList<OfficialWorldBootstrapFrame> GetWorldBootstrapSequence() =>
    [
        new(0, 0, 6, 0xA9, "WorldFirstFollowUp"),
        new(1, 6, 128, 0x8F, "PlayerSpawn"),
        new(2, 134, 320, 0xAC, "WorldMapAndSceneBootstrap"),
        new(3, 454, 752, 0x83, "WorldStaticStateBootstrap"),
        new(4, 1206, 68, 0x0E, "WorldAcceptedParserFrame"),
        new(5, 1274, 182, 0xFB, "WorldStateBootstrap"),
        new(6, 1456, 36, 0x18, "WorldUiBootstrap"),
        new(7, 1492, 63, 0xDB, "WorldStateBootstrap"),
        new(8, 1555, 42, 0x49, "WorldStateBootstrap"),
        new(9, 1597, 67, 0x48, "WorldStateBootstrap"),
        new(10, 1664, 88, 0x07, "WorldStateBootstrap"),
        new(11, 1752, 26, 0x10, "WorldBootstrapTerminator")
    ];

    public static OfficialWorldPlayerSpawn ParseCurrentPlayerSpawn()
    {
        var decodedPrefix = DecodeWorldServerFrame(BuildPlayerSpawnFrame128());

        var nameLength = Array.IndexOf(decodedPrefix, (byte)0x00, 7) - 7;
        var name = System.Text.Encoding.ASCII.GetString(decodedPrefix, 7, nameLength);
        return new OfficialWorldPlayerSpawn(
            decodedPrefix[2],
            name,
            BitConverter.ToUInt32(decodedPrefix, 19),
            decodedPrefix[35],
            decodedPrefix[36],
            OfficialWorldMovementMode.VisualMountedBaseline);
    }

    public static bool TryDecodeWorldMovement(ReadOnlySpan<byte> frame, out OfficialWorldMovementCandidate movement)
    {
        movement = default;
        if (frame.Length != WorldMovementFrameLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(frame) != WorldMovementFrameLength)
        {
            return false;
        }

        var decoded = DecodeWorldTransportFrame(frame);
        if (decoded[2] != WorldMovementOpcode ||
            decoded[8] != WorldMovementMountedState ||
            decoded[^1] != OfficialLoginWireTransform.ComputeChecksum(decoded))
        {
            return false;
        }

        var x = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(3));
        var y = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(5));
        if (x > OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate ||
            y > OfficialClientWorldCoordinateGrid.MaximumPackedCoordinate)
        {
            return false;
        }

        movement = new OfficialWorldMovementCandidate(
            Convert.ToHexString(frame),
            Convert.ToHexString(decoded),
            decoded[7],
            x,
            y,
            decoded[8],
            OfficialWorldMovementMode.VisualMountedBaseline,
            OfficialWorldDirection.Unknown);
        return true;
    }

    public static bool TryDecodeCharacterCreate(
        ReadOnlySpan<byte> frame,
        out OfficialCharacterCreateCandidate candidate)
    {
        candidate = default;
        if (frame.Length != 48 || BinaryPrimitives.ReadUInt16LittleEndian(frame) != frame.Length)
        {
            return false;
        }

        var decoded = DecodeWorldServerFrame(frame);
        try
        {
            if (TryDecodeCharacterCreatePlaintext(decoded, out candidate))
            {
                return true;
            }
        }
        finally
        {
            Array.Clear(decoded);
        }

        decoded = OfficialLoginWireTransform.Decode(frame);
        try
        {
            return TryDecodeCharacterCreatePlaintext(decoded, out candidate);
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    private static bool TryDecodeCharacterCreatePlaintext(
        ReadOnlySpan<byte> decoded,
        out OfficialCharacterCreateCandidate candidate)
    {
        candidate = default;
        if (decoded.Length != 48 ||
            decoded[2] != 0x17 ||
            decoded[^1] != OfficialLoginWireTransform.ComputeChecksum(decoded))
        {
            return false;
        }

        var nameField = decoded.Slice(3, 29);
        var terminator = nameField.IndexOf((byte)0);
        if (terminator is < 3 or > 11 ||
            nameField[..terminator].IndexOfAnyExceptInRange((byte)'!', (byte)'~') >= 0)
        {
            return false;
        }

        var selection = decoded.Slice(32, 15);
        if (!IsVerifiedFemaleSwordsmanSelection(selection))
        {
            return false;
        }

        candidate = new OfficialCharacterCreateCandidate(
            System.Text.Encoding.ASCII.GetString(nameField[..terminator]),
            "Swordsman",
            "Female",
            "LifeSkill1",
            "Appearance1",
            Convert.ToHexString(selection),
            Convert.ToHexString(decoded));
        return true;
    }

    private static bool IsVerifiedFemaleSwordsmanSelection(ReadOnlySpan<byte> selection) =>
        selection.Length == 15 &&
        selection[0] == 0x02 &&
        selection[1] == 0x00 &&
        selection[2] == 0x01 &&
        selection[3] <= 0x06 &&
        selection[4] == 0x01 &&
        selection[5] == 0x00 &&
        selection[6] == 0x00 &&
        selection[7] <= 0xC0 && (selection[7] & 0x1F) == 0x00 &&
        selection[8] == 0x00 &&
        selection[9] <= 0xC1 && (selection[9] & 0x0F) == 0x01 &&
        selection[10] is >= 0x04 and <= 0x1C && (selection[10] & 0x03) == 0x00 &&
        selection[11] == 0x04 &&
        selection[12] == 0x00 &&
        selection[13] == 0x00 &&
        selection[14] == 0x00;

    public static bool TryDecodeCharacterSlotAction(ReadOnlySpan<byte> frame, out byte slot)
    {
        slot = 0;
        if (frame.Length != 5 || BinaryPrimitives.ReadUInt16LittleEndian(frame) != frame.Length)
        {
            return false;
        }

        var decoded = DecodeWorldServerFrame(frame);
        try
        {
            if (decoded[2] != 0x18 ||
                decoded[^1] != OfficialLoginWireTransform.ComputeChecksum(decoded))
            {
                return false;
            }

            slot = decoded[3];
            return slot is 0 or 1;
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    public static byte[] BuildWorldMovementAcknowledgement(byte sequence)
    {
        Span<byte> decoded = stackalloc byte[WorldMovementAcknowledgementFrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(decoded, WorldMovementAcknowledgementFrameLength);
        decoded[2] = WorldMovementAcknowledgementOpcode;
        decoded[3] = sequence;
        decoded[^1] = OfficialLoginWireTransform.ComputeChecksum(decoded);
        return EncodeWorldServerPayload(decoded);
    }

    public static byte[] DecodeWorldTransportFrame(ReadOnlySpan<byte> frame) =>
        DecodeWorldServerFrame(frame);

    public static byte[] EncodeWorldTransportFrame(ReadOnlySpan<byte> decodedFrame) =>
        EncodeWorldServerFrame(decodedFrame);

    public static byte[] DecodeWorldClientFrame(ReadOnlySpan<byte> frame)
    {
        var decoded = frame.ToArray();
        var key = decoded[2];
        for (var index = 3; index < decoded.Length; index++)
        {
            decoded[index] ^= key;
            key = decoded[index - 1];
        }

        return decoded;
    }

    public static byte[] EncodeWorldClientFrame(ReadOnlySpan<byte> decodedFrame)
    {
        var encoded = decodedFrame.ToArray();
        var key = decodedFrame[2];
        for (var index = 3; index < encoded.Length; index++)
        {
            encoded[index] = (byte)(decodedFrame[index] ^ key);
            key = decodedFrame[index - 1];
        }

        return encoded;
    }

    public static byte[] DecodeWorldServerPayload(ReadOnlySpan<byte> frame) =>
        DecodeWorldServerFrame(frame);

    public static byte[] DecodeCurrentCreateWorldServerPayload(ReadOnlySpan<byte> frame) =>
        DecodeWorldServerFrame(frame, CurrentCreateWorldCipherKey, 0x4C);

    public static byte[] EncodeWorldServerPayload(ReadOnlySpan<byte> decodedFrame) =>
        EncodeWorldServerFrame(decodedFrame);

    private static byte[] DecodeWorldServerFrame(ReadOnlySpan<byte> frame)
        => DecodeWorldServerFrame(frame, WorldCipherKey, 0xB0);

    private static byte[] DecodeWorldServerFrame(
        ReadOnlySpan<byte> frame,
        ReadOnlySpan<byte> cipherKey,
        byte initialPreviousPlain)
    {
        var decoded = frame.ToArray();
        var previousPlain = (int)initialPreviousPlain;
        for (var i = 2; i < decoded.Length; i++)
        {
            var encrypted = (decoded[i] + 3 - previousPlain) & 0xFF;
            var plain = cipherKey[(i - 2) & 0xFF] ^ encrypted;
            decoded[i] = (byte)plain;
            previousPlain = plain;
        }

        return decoded;
    }

    private static byte[] EncodeWorldServerFrame(ReadOnlySpan<byte> decodedFrame)
        => EncodeWorldServerFrame(decodedFrame, WorldCipherKey, 0xB0);

    private static byte[] EncodeWorldServerFrame(
        ReadOnlySpan<byte> decodedFrame,
        ReadOnlySpan<byte> cipherKey,
        byte initialPreviousPlain)
    {
        var encoded = decodedFrame.ToArray();
        var previousPlain = (int)initialPreviousPlain;
        for (var i = 2; i < encoded.Length; i++)
        {
            var plain = decodedFrame[i];
            encoded[i] = (byte)(((cipherKey[(i - 2) & 0xFF] ^ plain) + previousPlain - 3) & 0xFF);
            previousPlain = plain;
        }

        return encoded;
    }
}
