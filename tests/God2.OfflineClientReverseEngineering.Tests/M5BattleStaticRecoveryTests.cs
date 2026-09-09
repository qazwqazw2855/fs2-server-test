using God2.OfflineClientReverseEngineering;

namespace God2.OfflineClientReverseEngineering.Tests;

public sealed class M5BattleStaticRecoveryTests
{
    public static TheoryData<byte, uint, string> PinnedDispatchIdentities => new()
    {
        { 0x82, 0x0009111E, "8479AE559D5BF766AB5DB8A728FADB71F6F55A28E9A5F7241EC7A16C6C09612A" },
        { 0x83, 0x00147092, "9EDA4902430AB81E9141505C77C67F4B85882020965DEF162DA4070A404614D2" },
        { 0x85, 0x00147092, "9EDA4902430AB81E9141505C77C67F4B85882020965DEF162DA4070A404614D2" },
        { 0x86, 0x00147092, "9EDA4902430AB81E9141505C77C67F4B85882020965DEF162DA4070A404614D2" },
        { 0x88, 0x00147092, "9EDA4902430AB81E9141505C77C67F4B85882020965DEF162DA4070A404614D2" },
        { 0x89, 0x001470AA, "69C353715B9B01716D9E50137DDFE9A46E8EC3823FED86D896A6A2A5D3B494F2" },
        { 0xE5, 0x0008EBE4, "DF530C8BAFED50253886D4CBDEF86EFF4A7E14B56799D89F1E8CF5626EE123C7" }
    };

    [Fact]
    public void BuildIdentity_RequiresExactOfficialClientAndRuntimeHashes()
    {
        M5BattleStaticRecovery.ValidateBuildIdentity(
            M5BattleStaticRecovery.ExpectedRuntimeCodeSha256,
            M5BattleStaticRecovery.ExpectedOfficialClientSha256);

        Assert.Throws<InvalidDataException>(() => M5BattleStaticRecovery.ValidateBuildIdentity(
            new string('0', 64),
            M5BattleStaticRecovery.ExpectedOfficialClientSha256));
        Assert.Throws<InvalidDataException>(() => M5BattleStaticRecovery.ValidateBuildIdentity(
            M5BattleStaticRecovery.ExpectedRuntimeCodeSha256,
            new string('0', 64)));
    }

    [Fact]
    public void LegacyManifest_CannotSelfPromoteCaptureTimeBuildAttestation()
    {
        M5BattleStaticRecovery.ValidateBuildAssociation(
            captureAttested: false,
            M5BattleStaticRecovery.ExpectedLegacyBuildAssociationStatus);

        Assert.Throws<InvalidDataException>(() => M5BattleStaticRecovery.ValidateBuildAssociation(
            captureAttested: true,
            M5BattleStaticRecovery.ExpectedLegacyBuildAssociationStatus));
        Assert.Throws<InvalidDataException>(() => M5BattleStaticRecovery.ValidateBuildAssociation(
            captureAttested: false,
            "VerifiedByMutableManifestFlag"));
    }

    [Theory]
    [MemberData(nameof(PinnedDispatchIdentities))]
    public void DispatchIdentity_AcceptsEveryPinnedTargetAndHandlerWindow(
        byte opcode,
        uint targetRva,
        string targetSha256)
    {
        M5BattleStaticRecovery.ValidateDispatchIdentity(opcode, targetRva, targetSha256);
    }

    [Fact]
    public void DispatchIdentity_RejectsChangedTargetOrHandlerBytes()
    {
        Assert.Throws<InvalidDataException>(() => M5BattleStaticRecovery.ValidateDispatchIdentity(
            0x83,
            0x00147093,
            "9EDA4902430AB81E9141505C77C67F4B85882020965DEF162DA4070A404614D2"));
        Assert.Throws<InvalidDataException>(() => M5BattleStaticRecovery.ValidateDispatchIdentity(
            0x83,
            0x00147092,
            new string('0', 64)));
    }
}
