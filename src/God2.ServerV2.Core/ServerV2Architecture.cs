namespace God2.ServerV2.Core;

public enum ServerContentMode
{
    ClassicCompatibility,
    CustomExpansion
}

public enum ConnectionStage
{
    Connected,
    LoginHandshake,
    Login,
    CharacterSelect,
    WorldHandshake,
    InWorld,
    Closing,
    Closed
}

public enum ProtocolEvidenceStatus
{
    Verified,
    StaticEvidence,
    Captured,
    NeedsRecovery,
    Blocked
}

public static class ServerV2Architecture
{
    public const string Version = "0.1.0-foundation";

    public static readonly string[] Milestones =
    [
        "M1 Core: login -> character -> world -> movement -> logout -> relogin",
        "M2 World: entity -> AOI -> replication -> NPC -> map -> portal",
        "M3 Playable: inventory -> equipment -> monster -> combat -> skill -> drop -> shop -> quest",
        "M4 Complete: pet -> craft -> warehouse -> party -> trade -> guild -> mail -> PvP -> instance"
    ];
}
