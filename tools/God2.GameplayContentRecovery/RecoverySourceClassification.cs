namespace God2.GameplayContentRecovery;

public static class RecoverySourceClassification
{
    public static string FromFile(string sourceFile)
    {
        if (sourceFile.StartsWith("https://forum.gamer.com.tw/", StringComparison.OrdinalIgnoreCase))
        {
            return "BahamutSupplementalEvidence";
        }

        if (sourceFile.StartsWith("https://xjz.17173.com/", StringComparison.OrdinalIgnoreCase))
        {
            return "17173SupplementalEvidence";
        }

        if (sourceFile.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            sourceFile.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "GuideSupplementalEvidence";
        }

        if (sourceFile.StartsWith("historical:", StringComparison.OrdinalIgnoreCase))
        {
            return "HistoricalGameplayObservation";
        }

        return "OfficialClientResource";
    }
}
