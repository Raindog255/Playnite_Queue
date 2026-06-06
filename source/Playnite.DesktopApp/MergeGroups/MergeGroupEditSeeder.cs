using System;
using System.Collections.Generic;
using System.Linq;
using Playnite;
using Playnite.DesktopApp.ViewModels;
using Playnite.MergeGroups;
using Playnite.SDK.Models;

namespace Playnite.DesktopApp
{
    /// <summary>
    /// Seeds merge-group edit state on <see cref="GameEditViewModel"/>.
    /// </summary>
    public static class MergeGroupEditSeeder
    {
        public static void Seed(
            GameEditViewModel viewModel,
            IList<Game> members,
            IEnumerable<CompletionStatus> completionStatuses,
            LibrarySanitizerSettings sanitizerSettings)
        {
            if (viewModel == null || members == null || members.Count == 0)
            {
                return;
            }

            var reduced = MergeGroups.MergeGroupReducer.Reduce(members, sanitizerSettings, completionStatuses);
            var editing = viewModel.EditingGame;
            if (editing == null)
            {
                return;
            }

            editing.Name = reduced.Name;
            editing.SortingName = reduced.SortingName;
            editing.Description = reduced.Description;
            editing.ReleaseDate = reduced.ReleaseDate;
            editing.CompletedDate = reduced.CompletedDate;
            if (reduced.CompletionStatusId != Guid.Empty)
            {
                editing.CompletionStatusId = reduced.CompletionStatusId;
            }

            editing.UserScore = reduced.UserScore;
            editing.CriticScore = reduced.CriticScore;
            editing.CommunityScore = reduced.CommunityScore;
            editing.SeriesIds = reduced.SeriesIds?.ToList();
            editing.GenreIds = reduced.GenreIds?.ToList();
            editing.CategoryIds = reduced.CategoryIds?.ToList();
            editing.DeveloperIds = reduced.DeveloperIds?.ToList();
            editing.PublisherIds = reduced.PublisherIds?.ToList();
            editing.OnHold = reduced.OnHold;
            editing.Icon = reduced.Icon;
            editing.CoverImage = reduced.CoverImage;
            editing.BackgroundImage = reduced.BackgroundImage;
            editing.TagIds = MergeGroups.MergeGroupDisplay.TagIdsUnion(members)?.ToList();

            viewModel.UseNameChanges = true;
            viewModel.UseSortingNameChanges = true;
            viewModel.UseDescriptionChanges = true;
            viewModel.UseReleaseDateChanges = true;
            viewModel.UseCompletedDateChanges = true;
            viewModel.UseCompletionStatusChanges = true;
            viewModel.UseUserScoreChanges = reduced.UserScore.HasValue;
            viewModel.UseCriticScoreChanges = reduced.CriticScore.HasValue;
            viewModel.UseCommunityScoreChanges = reduced.CommunityScore.HasValue;
            viewModel.UseSeriesChanges = reduced.SeriesIds?.Count > 0;
            viewModel.UseGenresChanges = reduced.GenreIds?.Count > 0;
            viewModel.UseCategoryChanges = reduced.CategoryIds?.Count > 0;
            viewModel.UseDeveloperChanges = reduced.DeveloperIds?.Count > 0;
            viewModel.UsePublisherChanges = reduced.PublisherIds?.Count > 0;
            viewModel.UseOnHoldChanges = reduced.OnHold;
            viewModel.UseIconChanges = !string.IsNullOrEmpty(reduced.Icon);
            viewModel.UseImageChanges = !string.IsNullOrEmpty(reduced.CoverImage);
            viewModel.UseBackgroundChanges = !string.IsNullOrEmpty(reduced.BackgroundImage);
            viewModel.UseTagChanges = false;

            viewModel.RefreshSelectableListsFromEditingGame();
        }
    }
}
