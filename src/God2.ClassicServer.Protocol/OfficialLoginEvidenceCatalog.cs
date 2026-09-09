using System.Security.Cryptography;

namespace God2.ClassicServer.Protocol;

public static class OfficialLoginEvidenceCatalog
{
    public const string LoginRequestKnowledgeId = "login-request-client-208-opaque";
    public const string LoginSuccessResponseKnowledgeId = "login-success-character-bootstrap-server-417-opaque";

    private static readonly IReadOnlyDictionary<string, string> KnowledgeIdBySha256 =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["3399F8D051F7749386B6C0507FCCCE72D74D0EBDFDFF9BA1B4A237EDDDF9AE05"] = LoginRequestKnowledgeId,
            ["BC4EEB76F105D21FD55EB90B20326A83C0727BCAF0E1636A5680B2A7C64542E3"] = LoginRequestKnowledgeId,
            ["CA4F32D2125CEA3B8543BE3A25AE6FBDD7C8B094791828A411949FB1F6471A0A"] = LoginRequestKnowledgeId,
            ["D8E99AFEAD7EDFBBFB3E544BC75A5D73B864D7627B513928B8DCE81FFDA46BA1"] = LoginRequestKnowledgeId,
            ["04104E82B860E3EBD036F0002344D1940F14342CAE03EF592A5FBE8763E48EB5"] = LoginSuccessResponseKnowledgeId,
            ["41DE0956D91E72AE0B376128F9F21634489906497454F91A0FC73B87376FD839"] = LoginSuccessResponseKnowledgeId,
            ["4425D35DB2BE08B94178FC59513CA8318D0F3995F1C15A3538B7C01FE31DF5DF"] = LoginSuccessResponseKnowledgeId,
            ["EAAB54F5893D29976BA2F514653C937AB2A96BCFBD406D969801BD552EC8E3E8"] = LoginSuccessResponseKnowledgeId
        };

    public static bool TryIdentify(ReadOnlySpan<byte> frame, out string knowledgeId)
    {
        var sha256 = Convert.ToHexString(SHA256.HashData(frame));
        return KnowledgeIdBySha256.TryGetValue(sha256, out knowledgeId!);
    }

    public static bool Matches(string knowledgeId, ReadOnlySpan<byte> frame) =>
        TryIdentify(frame, out var identified) &&
        string.Equals(identified, knowledgeId, StringComparison.Ordinal);
}

public sealed class VerifiedPacketRoleMatcher
{
    private readonly PacketSerializer _serializer = new();

    public bool Matches(PacketEnvelope packet, string knowledgeId)
    {
        if (!string.Equals(packet.Knowledge?.Id, knowledgeId, StringComparison.Ordinal))
        {
            return false;
        }

        var serialized = _serializer.Write(packet);
        return serialized.Succeeded &&
               OfficialLoginEvidenceCatalog.Matches(knowledgeId, serialized.Value.Span);
    }
}
