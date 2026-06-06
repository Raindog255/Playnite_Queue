using Playnite.SDK.Models;
using System;
using System.Collections.Generic;

namespace Playnite.MergeGroups
{
    /// <summary>
    /// Sync-bucket values computed by <see cref="MergeGroupReducer"/> for a member set.
    /// </summary>
    public class MergeGroupReducedValues
    {
        public string Name { get; set; }
        public string SortingName { get; set; }
        public string Description { get; set; }
        public ReleaseDate? ReleaseDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public Guid CompletionStatusId { get; set; }
        public int? UserScore { get; set; }
        public int? CriticScore { get; set; }
        public int? CommunityScore { get; set; }
        public List<Guid> SeriesIds { get; set; }
        public List<Guid> GenreIds { get; set; }
        public List<Guid> CategoryIds { get; set; }
        public List<Guid> DeveloperIds { get; set; }
        public List<Guid> PublisherIds { get; set; }
        public bool OnHold { get; set; }
        public string Icon { get; set; }
        public string CoverImage { get; set; }
        public string BackgroundImage { get; set; }
    }
}
