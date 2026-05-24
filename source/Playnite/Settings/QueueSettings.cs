using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Playnite
{
    /// <summary>
    /// Source date used to order eligible games in the Queue view (always ascending).
    /// </summary>
    public enum QueueDateSource
    {
        ReleaseDate,
        AcquiredDate
    }

    /// <summary>
    /// Game attributes a <see cref="QueuePriorityRule"/> can match against to override the
    /// default date sort. Listed in roughly the order they appear in the Queue settings panel.
    /// </summary>
    public enum QueuePriorityField
    {
        Series,
        Developer,
        Library,
        Free,
        Mobile,
        Style,
        Genre,
        Tag
    }

    /// <summary>
    /// A single override rule that promotes games matching a particular attribute value
    /// ahead of the default date-sorted queue. Rules are ordered by <see cref="Order"/>;
    /// rule with the smallest <see cref="Order"/> outranks all later rules. Within a group
    /// of games matching the same rule, the date sort from <see cref="QueueSettings.DateSource"/>
    /// is preserved.
    /// </summary>
    public class QueuePriorityRule : ObservableObject
    {
        private QueuePriorityField field;
        public QueuePriorityField Field
        {
            get => field;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        }

        private Guid? valueId;
        /// <summary>
        /// Database id of the matched value for fields that reference db items
        /// (Series, Developer, Library, Style/Category, Genre, Tag). Null when the field
        /// is a boolean (Free, Mobile).
        /// </summary>
        public Guid? ValueId
        {
            get => valueId;
            set
            {
                valueId = value;
                OnPropertyChanged();
            }
        }

        private bool? boolValue;
        /// <summary>
        /// Matched boolean value for boolean fields (Free, Mobile). Null when the
        /// field is an id reference.
        /// </summary>
        public bool? BoolValue
        {
            get => boolValue;
            set
            {
                boolValue = value;
                OnPropertyChanged();
            }
        }

        private int order;
        public int Order
        {
            get => order;
            set
            {
                order = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Configuration for a single attribute buffer. A buffer pushes a game to the back
    /// of the queue if its attribute (Style/Series/Developer/Genre) matches either
    /// (a) any currently-playing game (when <see cref="PushIfPlayingMatches"/> is on), or
    /// (b) any of the last <see cref="RecentCompletedCount"/> games sorted by
    /// CompletedDate descending. Setting both off / zero disables the buffer.
    /// </summary>
    public class QueueBufferSettings : ObservableObject
    {
        private bool pushIfPlayingMatches;
        public bool PushIfPlayingMatches
        {
            get => pushIfPlayingMatches;
            set
            {
                pushIfPlayingMatches = value;
                OnPropertyChanged();
            }
        }

        private int recentCompletedCount;
        public int RecentCompletedCount
        {
            get => recentCompletedCount;
            set
            {
                recentCompletedCount = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Persistent configuration for the Queue view: visible-count cap, primary date
    /// source, override priority rules, per-attribute push-back buffers, and grouping
    /// toggles. Always sorted ascending by the chosen date source — a queue is
    /// inherently oldest-first.
    /// </summary>
    public class QueueSettings : ObservableObject
    {
        private int visibleCount = 1;
        /// <summary>
        /// Maximum number of games shown in the queue, not counting always-visible
        /// currently-playing games when they exceed this cap.
        /// </summary>
        public int VisibleCount
        {
            get => visibleCount;
            set
            {
                visibleCount = value;
                OnPropertyChanged();
            }
        }

        private QueueDateSource dateSource = QueueDateSource.AcquiredDate;
        public QueueDateSource DateSource
        {
            get => dateSource;
            set
            {
                dateSource = value;
                OnPropertyChanged();
            }
        }

        private Guid? playingStatusId;
        /// <summary>
        /// Id of the CompletionStatus that represents a currently-playing game.
        /// Games with this status are always shown at the top of the queue. Null
        /// disables the always-visible behavior.
        /// </summary>
        public Guid? PlayingStatusId
        {
            get => playingStatusId;
            set
            {
                playingStatusId = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<Guid> terminalStatusIds = new ObservableCollection<Guid>();
        /// <summary>
        /// Ids of CompletionStatuses that mark a game as no longer eligible for the
        /// queue (e.g. Beaten, Completed, Played, Abandoned). Games with one of these
        /// statuses are filtered out before sorting. The Playing status is handled
        /// separately and should not appear in this list.
        /// </summary>
        public ObservableCollection<Guid> TerminalStatusIds
        {
            get => terminalStatusIds;
            set
            {
                terminalStatusIds = value ?? new ObservableCollection<Guid>();
                OnPropertyChanged();
            }
        }

        private ObservableCollection<QueuePriorityRule> priorityRules = new ObservableCollection<QueuePriorityRule>();
        public ObservableCollection<QueuePriorityRule> PriorityRules
        {
            get => priorityRules;
            set
            {
                priorityRules = value;
                OnPropertyChanged();
            }
        }

        private QueueBufferSettings styleBuffer = new QueueBufferSettings();
        public QueueBufferSettings StyleBuffer
        {
            get => styleBuffer;
            set
            {
                styleBuffer = value ?? new QueueBufferSettings();
                OnPropertyChanged();
            }
        }

        private QueueBufferSettings seriesBuffer = new QueueBufferSettings();
        public QueueBufferSettings SeriesBuffer
        {
            get => seriesBuffer;
            set
            {
                seriesBuffer = value ?? new QueueBufferSettings();
                OnPropertyChanged();
            }
        }

        private QueueBufferSettings developerBuffer = new QueueBufferSettings();
        public QueueBufferSettings DeveloperBuffer
        {
            get => developerBuffer;
            set
            {
                developerBuffer = value ?? new QueueBufferSettings();
                OnPropertyChanged();
            }
        }

        private QueueBufferSettings genreBuffer = new QueueBufferSettings();
        public QueueBufferSettings GenreBuffer
        {
            get => genreBuffer;
            set
            {
                genreBuffer = value ?? new QueueBufferSettings();
                OnPropertyChanged();
            }
        }

        private bool groupByDeveloper;
        public bool GroupByDeveloper
        {
            get => groupByDeveloper;
            set
            {
                groupByDeveloper = value;
                OnPropertyChanged();
            }
        }

        private bool groupBySeries;
        public bool GroupBySeries
        {
            get => groupBySeries;
            set
            {
                groupBySeries = value;
                OnPropertyChanged();
            }
        }
    }
}
