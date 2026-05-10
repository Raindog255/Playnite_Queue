using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playnite.LibrarySanitizer
{
    /// <summary>
    /// Pure action-generation engine for the Library Sanitizer view. Given the
    /// live db record collections plus a settings snapshot, produces the
    /// flat list of actions the view renders. The engine performs no I/O of
    /// its own -- mutations live in <see cref="SanitizerAction.Apply"/>.
    /// </summary>
    public static class LibrarySanitizerEngine
    {
        /// <summary>
        /// Builds the full action list for the current library state. Skips
        /// any action whose <see cref="SanitizerAction.Signature"/> appears
        /// in <paramref name="dismissed"/>. Ordering is stable: sanitize
        /// actions first (game, then series, then company), grouped by
        /// target kind so the view can section them logically.
        /// </summary>
        public static IList<SanitizerAction> BuildActions(
            IEnumerable<Game> games,
            IEnumerable<Series> series,
            IEnumerable<Company> companies,
            LibrarySanitizerSettings settings,
            ISet<string> dismissed = null)
        {
            var actions = new List<SanitizerAction>();
            if (settings == null)
            {
                return actions;
            }

            if (settings.SanitizeEnabled)
            {
                if (settings.SanitizeGames && games != null)
                {
                    foreach (var g in games)
                    {
                        if (g == null || string.IsNullOrEmpty(g.Name)) continue;
                        var r = SanitizerRules.Apply(g.Name, SanitizerScope.GameName, settings);
                        if (!IsActionableForGame(r, g)) continue;
                        actions.Add(SanitizeAction.ForGame(g, r));
                    }
                }

                if (settings.SanitizeSeries && series != null)
                {
                    foreach (var s in series)
                    {
                        if (s == null || string.IsNullOrEmpty(s.Name)) continue;
                        var r = SanitizerRules.Apply(s.Name, SanitizerScope.SeriesName, settings);
                        if (!IsNameChange(r)) continue;
                        actions.Add(SanitizeAction.ForSeries(s, r));
                    }
                }

                if ((settings.SanitizeDevelopers || settings.SanitizePublishers) && companies != null)
                {
                    // Companies are stored in a single collection; we apply
                    // the rule to all of them when either developer or
                    // publisher scope is on. The user's distinction between
                    // "dev" and "pub" is just a UI scope toggle -- the
                    // underlying record is the same Company object.
                    foreach (var c in companies)
                    {
                        if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                        var r = SanitizerRules.Apply(c.Name, SanitizerScope.CompanyName, settings);
                        if (!IsNameChange(r)) continue;
                        actions.Add(SanitizeAction.ForCompany(c, r));
                    }
                }
            }

            if (settings.MissingPropertiesEnabled && games != null)
            {
                foreach (var g in games)
                {
                    if (g == null || g.Hidden) continue;
                    var missing = DetectMissingFields(g, settings);
                    if (missing.Count > 0)
                    {
                        actions.Add(new MissingPropertyAction(g, missing));
                    }
                }
            }

            if (settings.SplitCollectionEnabled && games != null)
            {
                foreach (var g in games)
                {
                    // Skip records the engine itself just hid via a prior
                    // split-collection apply -- otherwise the action would
                    // re-detect the source bundle on every rebuild.
                    if (g == null || g.Hidden) continue;
                    var action = CollectionDetector.TryDetect(g, settings);
                    if (action != null)
                    {
                        actions.Add(action);
                    }
                }
            }

            if (dismissed != null && dismissed.Count > 0)
            {
                actions = actions.Where(a => !dismissed.Contains(a.Signature)).ToList();
            }

            return actions;
        }

        // Engine-side idempotency check. SanitizationResult.Changed only
        // knows whether the rules *proposed* an override; it can't compare
        // those proposals against the live record. Without this filter the
        // numerals "trailing Roman" pass would re-emit a SanitizeAction on
        // every rebuild (it always proposes an Arabic SortingName), and
        // the user would see the just-applied card pop right back.
        internal static bool IsActionableForGame(SanitizationResult r, Game g)
        {
            if (r == null) return false;
            if (!string.Equals(r.OriginalName, r.SanitizedName, StringComparison.Ordinal))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(r.SortingNameOverride)
                && !string.Equals(r.SortingNameOverride, g?.SortingName, StringComparison.Ordinal))
            {
                return true;
            }
            // A non-null SpilledVersion always represents new text being
            // appended to Version, so emit unless the existing Version
            // already ends with that exact phrase (avoids the
            // "Edition Edition" loop after a previously-applied spill).
            if (!string.IsNullOrEmpty(r.SpilledVersion))
            {
                var existing = g?.Version ?? string.Empty;
                if (!existing.EndsWith(r.SpilledVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Series and Company records have no SortingName / Version fields,
        // so the only thing SanitizeAction.Apply can persist for them is a
        // name change. Filter on that to avoid no-op cards from the
        // numerals rule etc.
        internal static bool IsNameChange(SanitizationResult r) =>
            r != null && !string.Equals(r.OriginalName, r.SanitizedName, StringComparison.Ordinal);

        internal static IList<MissingField> DetectMissingFields(Game g, LibrarySanitizerSettings settings)
        {
            var missing = new List<MissingField>();
            if (g == null) return missing;

            if (settings.RequireAcquiredDate && g.AcquiredDate == null)
                missing.Add(MissingField.AcquiredDate);
            if (settings.RequireReleaseDate && g.ReleaseDate == null)
                missing.Add(MissingField.ReleaseDate);
            if (settings.RequireCompletionStatus && g.CompletionStatusId == Guid.Empty)
                missing.Add(MissingField.CompletionStatus);
            if (settings.RequireSeries && (g.SeriesIds == null || g.SeriesIds.Count == 0))
                missing.Add(MissingField.Series);
            if (settings.RequireDevelopers && (g.DeveloperIds == null || g.DeveloperIds.Count == 0))
                missing.Add(MissingField.Developers);
            if (settings.RequireCategories && (g.CategoryIds == null || g.CategoryIds.Count == 0))
                missing.Add(MissingField.Categories);
            if (settings.RequireGenres && (g.GenreIds == null || g.GenreIds.Count == 0))
                missing.Add(MissingField.Genres);
            return missing;
        }
    }
}
