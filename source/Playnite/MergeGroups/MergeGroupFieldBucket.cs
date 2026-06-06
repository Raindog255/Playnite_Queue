namespace Playnite.MergeGroups
{
    /// <summary>
    /// Property bucket for merge-group semantics.
    /// </summary>
    public enum MergeGroupFieldBucket
    {
        /// <summary>Computed from all members and written to every member.</summary>
        Sync,
        /// <summary>Stored per member; aggregated at display time only.</summary>
        Concat,
        /// <summary>Stored per member; edited only on individual member records.</summary>
        PerRecord,
    }
}
