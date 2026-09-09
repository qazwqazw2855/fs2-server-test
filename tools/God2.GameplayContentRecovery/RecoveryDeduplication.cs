namespace God2.GameplayContentRecovery;

public static class RecoveryDeduplication
{
    public static void BeforeTransform(RecoveryWorkspace workspace)
    {
        Replace(workspace.Sources, workspace.Sources
            .GroupBy(source => RecoveryDatabaseIdentity.ForStorage(source.SourceIdentity), StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(source => source.RecordCount ?? 0).First()));
        Replace(workspace.Raw, workspace.Raw
            .GroupBy(row => row.RecordId, StringComparer.Ordinal)
            .Select(group => group.First()));
        Replace(workspace.Entities, workspace.Entities
            .GroupBy(entity => (entity.Domain, entity.AuthorityKey, entity.SourceHash))
            .Select(group => group.OrderBy(entity => entity.MissingRequiredFields.Count).First()));
    }

    public static void AfterTransform(RecoveryWorkspace workspace)
    {
        Replace(workspace.Localization, workspace.Localization.GroupBy(row => row.LocalizationId, StringComparer.Ordinal).Select(group => group.First()));
        Replace(workspace.Staging, workspace.Staging.GroupBy(row => row.StagingId, StringComparer.Ordinal).Select(group => group.First()));
        Replace(workspace.Validation, workspace.Validation.GroupBy(row => row.ValidationId, StringComparer.Ordinal).Select(group => group.First()));
        Replace(workspace.Validated, workspace.Validated.GroupBy(row => row.ValidatedId, StringComparer.Ordinal).Select(group => group.First()));
    }

    private static void Replace<T>(List<T> target, IEnumerable<T> values)
    {
        var materialized = values.ToArray();
        target.Clear();
        target.AddRange(materialized);
    }
}
