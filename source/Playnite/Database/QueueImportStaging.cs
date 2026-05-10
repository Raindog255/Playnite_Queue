using Newtonsoft.Json;
using Playnite.LibrarySanitizer;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Playnite.Database
{
    public class QueueImportStagingResult
    {
        public int RowsStaged { get; set; }
        public int CleanRows { get; set; }
        public int NeedsReview { get; set; }
        public int Errors { get; set; }
        public List<string> Messages { get; } = new List<string>();
    }

    public class QueueImportStagingFile
    {
        public string SourceCsvPath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public Dictionary<string, int> HeaderMap { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<QueueImportStagedRow> Rows { get; set; } = new List<QueueImportStagedRow>();
    }

    public class QueueImportStagedRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int RowNumber { get; set; }
        public string OriginalName { get; set; }
        public string Name { get; set; }
        public List<string> Fields { get; set; } = new List<string>();
        public List<Guid> MatchedGameIds { get; set; } = new List<Guid>();
        public List<string> Messages { get; set; } = new List<string>();
        public bool Dismissed { get; set; }
    }

    /// <summary>
    /// File-backed staging area for queue-property CSV imports. Import only
    /// parses and matches rows; mutation of the live game database is delayed
    /// until the user commits reviewed rows from the Import Review view.
    /// </summary>
    public static class QueueImportStaging
    {
        public const string StagingFileName = "queue-import-staging.json";

        public static string DefaultStagingPath =>
            Path.Combine(PlaynitePaths.ConfigRootPath, StagingFileName);

        public static bool HasPendingRows(string stagingPath = null)
        {
            var file = Load(stagingPath);
            return file?.Rows?.Any(r => !r.Dismissed) == true;
        }

        public static QueueImportStagingResult StageFromCsv(
            string csvPath,
            IGameDatabaseMain db,
            LibrarySanitizerSettings sanitizerSettings,
            string stagingPath = null)
        {
            if (string.IsNullOrWhiteSpace(csvPath))
            {
                throw new ArgumentException("CSV path is required.", nameof(csvPath));
            }

            if (HasPendingRows(stagingPath))
            {
                throw new InvalidOperationException("A queue import is already staged. Commit or discard it before importing another CSV.");
            }

            using (var stream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                return StageFromReader(reader, csvPath, db, sanitizerSettings, stagingPath);
            }
        }

        internal static QueueImportStagingResult StageFromReader(
            TextReader reader,
            string sourceCsvPath,
            IGameDatabaseMain db,
            LibrarySanitizerSettings sanitizerSettings,
            string stagingPath = null)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (HasPendingRows(stagingPath))
            {
                throw new InvalidOperationException("A queue import is already staged. Commit or discard it before importing another CSV.");
            }

            var result = new QueueImportStagingResult();
            var rows = CsvReader.Parse(reader).ToList();
            if (rows.Count == 0)
            {
                result.Messages.Add("CSV is empty.");
                result.Errors++;
                return result;
            }

            var header = QueuePropertiesImporter.BuildHeaderMap(rows[0]);
            if (!header.ContainsKey("name"))
            {
                result.Messages.Add("CSV is missing the required 'Name' column.");
                result.Errors++;
                return result;
            }

            var file = new QueueImportStagingFile
            {
                SourceCsvPath = sourceCsvPath,
                CreatedAt = DateTime.Now,
                HeaderMap = header.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            };

            for (var i = 1; i < rows.Count; i++)
            {
                var rowNumber = i + 1;
                var fields = rows[i];
                var name = QueuePropertiesImporter.GetCell(fields, header, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    if (fields.All(string.IsNullOrWhiteSpace))
                    {
                        continue;
                    }

                    result.Errors++;
                    result.Messages.Add($"Row {rowNumber}: missing Name.");
                    continue;
                }

                var staged = new QueueImportStagedRow
                {
                    RowNumber = rowNumber,
                    OriginalName = name.Trim(),
                    Name = name.Trim(),
                    Fields = new List<string>(fields)
                };

                RefreshMatches(staged, db);
                if (staged.MatchedGameIds.Count == 0)
                {
                    staged.Messages.Add("No matching game.");
                }

                file.Rows.Add(staged);
                result.RowsStaged++;
                // Only un-matched rows count toward NeedsReview. Sanitization
                // differences are still surfaced inside the review UI (and used
                // to prefill the manual-match search), but they no longer push
                // an otherwise-matched row into the review queue, since there's
                // nothing for the user to act on if matching already succeeded.
                if (staged.MatchedGameIds.Count == 0)
                {
                    result.NeedsReview++;
                }
                else
                {
                    result.CleanRows++;
                }
            }

            Save(file, stagingPath);
            return result;
        }

        public static QueueImportStagingFile Load(string stagingPath = null)
        {
            var path = stagingPath ?? DefaultStagingPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new QueueImportStagingFile();
            }

            var file = JsonConvert.DeserializeObject<QueueImportStagingFile>(File.ReadAllText(path));
            file = file ?? new QueueImportStagingFile();
            file.HeaderMap = new Dictionary<string, int>(
                file.HeaderMap ?? new Dictionary<string, int>(),
                StringComparer.OrdinalIgnoreCase);
            file.Rows = file.Rows ?? new List<QueueImportStagedRow>();
            foreach (var row in file.Rows)
            {
                row.Fields = row.Fields ?? new List<string>();
                row.MatchedGameIds = row.MatchedGameIds ?? new List<Guid>();
                row.Messages = row.Messages ?? new List<string>();
            }
            return file;
        }

        public static void Save(QueueImportStagingFile file, string stagingPath = null)
        {
            var path = stagingPath ?? DefaultStagingPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(file ?? new QueueImportStagingFile(), Formatting.Indented));
        }

        public static void Delete(string stagingPath = null)
        {
            var path = stagingPath ?? DefaultStagingPath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static void RefreshMatches(QueueImportStagedRow row, IGameDatabaseMain db)
        {
            if (row == null || db == null) return;

            var liveManualMatches = (row.MatchedGameIds ?? new List<Guid>())
                .Select(id => db.Games[id])
                .Where(g => g != null)
                .Select(g => g.Id)
                .Distinct()
                .ToList();

            if (liveManualMatches.Count > 0)
            {
                row.MatchedGameIds = liveManualMatches;
                row.Messages.RemoveAll(m => string.Equals(m, "No matching game.", StringComparison.OrdinalIgnoreCase));
                return;
            }

            row.MatchedGameIds = QueuePropertiesImporter.FindGames(db, row.Name)
                .Select(g => g.Id)
                .Distinct()
                .ToList();

            row.Messages.RemoveAll(m => string.Equals(m, "No matching game.", StringComparison.OrdinalIgnoreCase));
            if (row.MatchedGameIds.Count == 0)
            {
                row.Messages.Add("No matching game.");
            }
        }

        public static void SetManualMatch(QueueImportStagedRow row, Game selected, IGameDatabaseMain db)
        {
            if (row == null || selected == null || db == null) return;

            // Preserve the importer's multi-match semantics: if the selected
            // title appears in multiple libraries, apply the staged row to all
            // records with that exact selected name.
            row.MatchedGameIds = QueuePropertiesImporter.FindGames(db, selected.Name)
                .Select(g => g.Id)
                .Distinct()
                .ToList();
            if (row.MatchedGameIds.Count == 0)
            {
                row.MatchedGameIds = new List<Guid> { selected.Id };
            }
            row.Messages.RemoveAll(m => string.Equals(m, "No matching game.", StringComparison.OrdinalIgnoreCase));
        }

        public static QueueImportResult Commit(QueueImportStagingFile file, IGameDatabaseMain db, string stagingPath = null)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (db == null) throw new ArgumentNullException(nameof(db));

            var result = new QueueImportResult();
            var remaining = new List<QueueImportStagedRow>();
            foreach (var row in file.Rows.Where(r => !r.Dismissed))
            {
                RefreshMatches(row, db);
                if (row.MatchedGameIds.Count == 0)
                {
                    result.NotFound++;
                    result.Messages.Add($"Row {row.RowNumber} ('{row.Name}'): no matching game.");
                    remaining.Add(row);
                    continue;
                }

                foreach (var id in row.MatchedGameIds.ToList())
                {
                    var game = db.Games[id];
                    if (game == null)
                    {
                        continue;
                    }

                    result.Matched++;
                    if (QueuePropertiesImporter.ApplyRow(db, game, row.Fields, file.HeaderMap, row.RowNumber, result))
                    {
                        result.Updated++;
                    }
                }
            }

            file.Rows = remaining;
            if (file.Rows.Count == 0)
            {
                Delete(stagingPath);
            }
            else
            {
                Save(file, stagingPath);
            }

            return result;
        }

        public static bool NeedsSanitization(string name, LibrarySanitizerSettings settings)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var result = SanitizerRules.Apply(name, SanitizerScope.GameName, settings);
            return !string.Equals(result.OriginalName, result.SanitizedName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Lightweight read-only lookup so the review UI can probe whether a
        /// candidate name (typically the sanitized form of a staged row) would
        /// resolve to library games, without touching the staged row itself.
        /// </summary>
        public static List<Game> FindMatches(string name, IGameDatabaseMain db)
        {
            if (string.IsNullOrWhiteSpace(name) || db == null)
            {
                return new List<Game>();
            }

            return QueuePropertiesImporter.FindGames(db, name);
        }
    }
}
