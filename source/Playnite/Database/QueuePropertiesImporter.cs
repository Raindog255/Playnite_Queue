using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Playnite.Database
{
    /// <summary>
    /// Aggregate report from a single <see cref="QueuePropertiesImporter.ImportFromCsv"/>
    /// run. <see cref="Messages"/> contains a per-row line for any row that wasn't
    /// matched, was ambiguous, or had a value parse error — surfaced to the user via
    /// the result dialog and also written to the application log.
    /// </summary>
    public class QueueImportResult
    {
        public int Matched { get; set; }
        public int Updated { get; set; }
        public int NotFound { get; set; }
        public int Ambiguous { get; set; }
        public int Errors { get; set; }
        public List<string> Messages { get; } = new List<string>();
    }

    /// <summary>
    /// One-shot bulk-populates the queue-related <see cref="Game"/> fields from a
    /// Sheets-exported CSV (see plan section 8 for the column contract). Designed to
    /// run once when a user first adopts the Queue view to seed `OnHold`, `Free`,
    /// `Mobile`, `AcquiredDate`, `CompletedDate`, `UserScore`, `Categories`, and
    /// optionally `CompletionStatus` from data they already maintain in a sheet.
    /// </summary>
    /// <remarks>
    /// Matching is case-insensitive by `Name`. Same-name duplicates are reported
    /// as ambiguous and skipped — the user is expected to disambiguate them in
    /// Playnite directly (the Library field, which Playnite assigns from the
    /// importing plugin, is the natural distinguisher and isn't user-editable
    /// from a spreadsheet anyway). Blank cells preserve the existing value —
    /// nothing in this importer ever clears a previously-set field. The
    /// `UserRating` column is on a 0–10 scale to match what humans naturally
    /// write in spreadsheets and is multiplied by 10 to land in Playnite's
    /// native 0–100 `UserScore`.
    /// </remarks>
    public static class QueuePropertiesImporter
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        /// <summary>
        /// Reads <paramref name="csvPath"/>, matches each row against the database,
        /// and applies any populated values inside a single
        /// <see cref="GameDatabase.BufferedUpdate"/> so consumers see one batched
        /// notification instead of N. Returns a summary of matched / updated /
        /// not-found / ambiguous / error counts plus per-row diagnostic messages.
        /// </summary>
        public static QueueImportResult ImportFromCsv(string csvPath, IGameDatabaseMain db)
        {
            if (string.IsNullOrWhiteSpace(csvPath))
            {
                throw new ArgumentException("CSV path is required.", nameof(csvPath));
            }

            using (var stream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                return ImportFromReader(reader, db);
            }
        }

        internal static QueueImportResult ImportFromReader(TextReader reader, IGameDatabaseMain db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            var result = new QueueImportResult();
            var rows = CsvReader.Parse(reader).ToList();
            if (rows.Count == 0)
            {
                result.Messages.Add("CSV is empty.");
                return result;
            }

            var header = BuildHeaderMap(rows[0]);
            if (!header.ContainsKey("name"))
            {
                result.Messages.Add("CSV is missing the required 'Name' column.");
                result.Errors++;
                return result;
            }

            // One BufferedUpdate around the whole import so subscribers (queue
            // view, completion-status auto-filler, etc.) see a single batched
            // notification instead of one per row.
            using (db.BufferedUpdate())
            {
                for (var i = 1; i < rows.Count; i++)
                {
                    var rowNumber = i + 1; // 1-indexed, including the header
                    var fields = rows[i];
                    var name = GetCell(fields, header, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        // Blank lines from trailing newlines are common in
                        // Sheets exports — silently skip rather than report.
                        if (fields.All(string.IsNullOrWhiteSpace))
                        {
                            continue;
                        }

                        result.Errors++;
                        result.Messages.Add($"Row {rowNumber}: missing Name.");
                        continue;
                    }

                    var matches = FindGames(db, name);
                    if (matches.Count == 0)
                    {
                        result.NotFound++;
                        result.Messages.Add($"Row {rowNumber} ('{name}'): no matching game.");
                        continue;
                    }

                    // When the same name lands on multiple games (typically the
                    // same title imported from two libraries — Steam + Epic,
                    // etc.), apply the row to every match. The user owns one
                    // game logically; they shouldn't have to replay it just
                    // because Playnite tracks it twice.
                    if (matches.Count > 1)
                    {
                        result.Ambiguous++;
                        result.Messages.Add(
                            $"Row {rowNumber} ('{name}'): matched {matches.Count} games — applied to all.");
                    }

                    foreach (var game in matches)
                    {
                        result.Matched++;
                        if (ApplyRow(db, game, fields, header, rowNumber, result))
                        {
                            result.Updated++;
                        }
                    }
                }
            }

            return result;
        }

        private static bool ApplyRow(
            IGameDatabaseMain db,
            Game game,
            IReadOnlyList<string> fields,
            IReadOnlyDictionary<string, int> header,
            int rowNumber,
            QueueImportResult result)
        {
            var changed = false;

            if (TryParseBool(GetCell(fields, header, "onhold"), out var onHold, rowNumber, "OnHold", result))
            {
                if (onHold.HasValue && game.OnHold != onHold.Value)
                {
                    game.OnHold = onHold.Value;
                    changed = true;
                }
            }

            if (TryParseBool(GetCell(fields, header, "free"), out var free, rowNumber, "Free", result))
            {
                if (free.HasValue && game.Free != free.Value)
                {
                    game.Free = free.Value;
                    changed = true;
                }
            }

            if (TryParseBool(GetCell(fields, header, "mobile"), out var mobile, rowNumber, "Mobile", result))
            {
                if (mobile.HasValue && game.Mobile != mobile.Value)
                {
                    game.Mobile = mobile.Value;
                    changed = true;
                }
            }

            if (TryParseDate(GetCell(fields, header, "acquireddate"), out var acquired, rowNumber, "AcquiredDate", result))
            {
                if (acquired.HasValue && game.AcquiredDate != acquired.Value)
                {
                    game.AcquiredDate = acquired.Value;
                    changed = true;
                }
            }

            if (TryParseDate(GetCell(fields, header, "completeddate"), out var completed, rowNumber, "CompletedDate", result))
            {
                if (completed.HasValue && game.CompletedDate != completed.Value)
                {
                    game.CompletedDate = completed.Value;
                    changed = true;
                }
            }

            if (TryParseUserRating(GetCell(fields, header, "userrating"), out var userScore, rowNumber, result))
            {
                if (userScore.HasValue && game.UserScore != userScore.Value)
                {
                    game.UserScore = userScore.Value;
                    changed = true;
                }
            }

            var statusCell = GetCell(fields, header, "completionstatus");
            if (!string.IsNullOrWhiteSpace(statusCell))
            {
                var status = db.CompletionStatuses.Add(statusCell.Trim());
                if (status != null && game.CompletionStatusId != status.Id)
                {
                    game.CompletionStatusId = status.Id;
                    changed = true;
                }
            }

            var categoriesCell = GetCell(fields, header, "categories");
            if (!string.IsNullOrWhiteSpace(categoriesCell))
            {
                var newIds = ResolveCategories(db, categoriesCell);
                if (!ListsEqualUnordered(game.CategoryIds, newIds))
                {
                    // Replace (not union): the spec treats this column as the
                    // canonical list. Blank cells preserve existing assignments;
                    // populated cells overwrite them.
                    game.CategoryIds = newIds;
                    changed = true;
                }
            }

            if (changed)
            {
                db.Games.Update(game);
            }

            return changed;
        }

        private static List<Game> FindGames(IGameDatabaseMain db, string name)
        {
            return db.Games
                .Where(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static List<Guid> ResolveCategories(IGameDatabaseMain db, string cell)
        {
            var ids = new List<Guid>();
            foreach (var raw in cell.Split('|'))
            {
                var name = raw.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var category = db.Categories.Add(name);
                if (category != null && !ids.Contains(category.Id))
                {
                    ids.Add(category.Id);
                }
            }

            return ids;
        }

        private static bool TryParseBool(
            string raw,
            out bool? value,
            int rowNumber,
            string columnLabel,
            QueueImportResult result)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }

            switch (raw.Trim().ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "y":
                case "1":
                    value = true;
                    return true;
                case "false":
                case "no":
                case "n":
                case "0":
                    value = false;
                    return true;
                default:
                    result.Errors++;
                    result.Messages.Add($"Row {rowNumber}: '{raw}' is not a valid {columnLabel} value (expected TRUE/FALSE).");
                    return false;
            }
        }

        // Date formats we accept. Order doesn't matter for correctness, but the
        // ISO and Sheets-default forms are listed first because they account for
        // ~all real-world rows.
        //
        // Deliberately omitted: regional `M/d/yyyy` and `d/M/yyyy` — these are
        // ambiguous (3/4/2024 = March 4 or April 3?) and silently mis-parsing
        // the user's library is a worse failure mode than asking them to fix
        // the cell.
        private static readonly string[] AcceptedDateFormats = new[]
        {
            "yyyy-MM-dd",      // ISO
            "MMM d, yyyy",     // Sheets default ("Sep 7, 2020")
            "MMM d yyyy",      // Sheets default minus the comma
            "MMMM d, yyyy",    // Full month name ("September 7, 2020")
            "MMMM d yyyy",
            "d MMM yyyy",      // "7 Sep 2020"
            "d MMMM yyyy",     // "7 September 2020"
        };

        private static bool TryParseDate(
            string raw,
            out DateTime? value,
            int rowNumber,
            string columnLabel,
            QueueImportResult result)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }

            // InvariantCulture so month names always mean English regardless of
            // the user's Windows locale. AllowWhiteSpace tolerates the stray
            // double-space that Sheets sometimes emits between month and day.
            if (DateTime.TryParseExact(
                    raw.Trim(),
                    AcceptedDateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsed))
            {
                value = parsed.Date;
                return true;
            }

            result.Errors++;
            result.Messages.Add($"Row {rowNumber}: '{raw}' is not a recognized {columnLabel}. Supported formats: yyyy-MM-dd, MMM d yyyy (e.g. Sep 7, 2020).");
            return false;
        }

        private static bool TryParseUserRating(
            string raw,
            out int? value,
            int rowNumber,
            QueueImportResult result)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return true;
            }

            if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rating))
            {
                result.Errors++;
                result.Messages.Add($"Row {rowNumber}: '{raw}' is not a valid UserRating (expected a number 0-10).");
                return false;
            }

            if (rating < 0 || rating > 10)
            {
                result.Errors++;
                result.Messages.Add($"Row {rowNumber}: UserRating '{raw}' is out of the 0-10 range.");
                return false;
            }

            // 8.5 → 85, 9 → 90, 0 → 0. Half-away-from-zero matches what users
            // typing decimal scores expect (every existing CSV editor rounds
            // 0.5 up, not banker's-style).
            value = (int)Math.Round(rating * 10.0, MidpointRounding.AwayFromZero);
            return true;
        }

        private static IReadOnlyDictionary<string, int> BuildHeaderMap(IReadOnlyList<string> headerRow)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headerRow.Count; i++)
            {
                var key = headerRow[i]?.Trim();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                // Normalize so "Acquired Date", "AcquiredDate", " acquired date "
                // all collapse to the same lookup key.
                var normalized = NormalizeColumn(key);
                if (!map.ContainsKey(normalized))
                {
                    map[normalized] = i;
                }
            }

            return map;
        }

        private static string NormalizeColumn(string value)
        {
            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        // Sheets users often type a placeholder ("-", "N/A") instead of leaving
        // a cell genuinely empty, especially in numeric columns like UserRating
        // where Sheets won't autofill. Treat those as blank so the row imports
        // cleanly instead of throwing per-row parse errors.
        private static readonly HashSet<string> BlankPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-", "--", "\u2013", "\u2014", "n/a", "na", "null"
        };

        private static string GetCell(
            IReadOnlyList<string> fields,
            IReadOnlyDictionary<string, int> header,
            string column)
        {
            if (!header.TryGetValue(column, out var idx) || idx >= fields.Count)
            {
                return null;
            }

            var value = fields[idx];
            if (value != null && BlankPlaceholders.Contains(value.Trim()))
            {
                return null;
            }

            return value;
        }

        private static bool ListsEqualUnordered(IList<Guid> a, IList<Guid> b)
        {
            var ac = a?.Count ?? 0;
            var bc = b?.Count ?? 0;
            if (ac != bc)
            {
                return false;
            }

            if (ac == 0)
            {
                return true;
            }

            var set = new HashSet<Guid>(a);
            foreach (var id in b)
            {
                if (!set.Contains(id))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Minimal RFC-4180-flavored CSV parser. Handles quoted fields, embedded
    /// newlines and commas, and `""` escaping inside quoted fields. Built in-house
    /// because Playnite has no CSV dependency yet and our format is small/strict.
    /// </summary>
    internal static class CsvReader
    {
        public static IEnumerable<List<string>> Parse(TextReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            var field = new StringBuilder();
            var row = new List<string>();
            var inQuotes = false;
            var rowHasContent = false;

            int next;
            while ((next = reader.Read()) != -1)
            {
                var c = (char)next;
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // Doubled quote inside a quoted field is a literal quote.
                        if (reader.Peek() == '"')
                        {
                            field.Append('"');
                            reader.Read();
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }

                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    rowHasContent = true;
                    continue;
                }

                if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    rowHasContent = true;
                    continue;
                }

                if (c == '\r')
                {
                    if (reader.Peek() == '\n')
                    {
                        reader.Read();
                    }

                    row.Add(field.ToString());
                    field.Clear();
                    if (rowHasContent)
                    {
                        yield return row;
                    }

                    row = new List<string>();
                    rowHasContent = false;
                    continue;
                }

                if (c == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    if (rowHasContent)
                    {
                        yield return row;
                    }

                    row = new List<string>();
                    rowHasContent = false;
                    continue;
                }

                field.Append(c);
                rowHasContent = true;
            }

            if (rowHasContent || field.Length > 0)
            {
                row.Add(field.ToString());
                yield return row;
            }
        }
    }
}
