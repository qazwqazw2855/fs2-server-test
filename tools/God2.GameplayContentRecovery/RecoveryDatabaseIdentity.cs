namespace God2.GameplayContentRecovery;

public static class RecoveryDatabaseIdentity
{
    public static string ForStorage(string value, int maximumLength = 512)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var suffix = ":sha256:" + ContentHash.Sha256(value)[..24];
        var prefixLength = maximumLength - suffix.Length;
        if (prefixLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        if (char.IsHighSurrogate(value[prefixLength - 1]))
        {
            prefixLength--;
        }

        return value[..prefixLength] + suffix;
    }
}
