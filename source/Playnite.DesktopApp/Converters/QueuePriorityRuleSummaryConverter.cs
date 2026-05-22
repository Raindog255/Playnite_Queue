using Playnite;
using Playnite.DesktopApp.ViewModels;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Globalization;
using System.Windows.Data;

namespace Playnite.DesktopApp.QueueConverters
{
    /// <summary>
    /// One-line summary for a queue priority rule (property · value) for read-only list rows.
    /// </summary>
    public class QueuePriorityRuleSummaryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is QueuePriorityRule rule))
            {
                return string.Empty;
            }

            try
            {
                var desktop = DesktopApplication.Current?.MainModel as DesktopAppViewModel;
                var db = desktop?.Database;

                switch (rule.Field)
                {
                    case QueuePriorityField.Free:
                        return $"Free · {FormatBool(rule.BoolValue)}";
                    case QueuePriorityField.Mobile:
                        return $"Mobile · {FormatBool(rule.BoolValue)}";
                    case QueuePriorityField.Series:
                        return $"Series · {ResolveDbName(db?.Series, rule.ValueId)}";
                    case QueuePriorityField.Developer:
                        return $"Developer · {ResolveDbName(db?.Companies, rule.ValueId)}";
                    case QueuePriorityField.Style:
                        return $"Style · {ResolveDbName(db?.Categories, rule.ValueId)}";
                    case QueuePriorityField.Genre:
                        return $"Genre · {ResolveDbName(db?.Genres, rule.ValueId)}";
                    case QueuePriorityField.Library:
                        return $"Library · {ResolveLibraryName(rule.ValueId, desktop)}";
                    default:
                        return rule.Field.ToString();
                }
            }
            catch (Exception)
            {
                return rule.Field.ToString();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static string FormatBool(bool? v)
        {
            if (v == true)
            {
                return "Yes";
            }

            if (v == false)
            {
                return "No";
            }

            return "—";
        }

        private static string ResolveDbName<T>(IItemCollection<T> coll, Guid? id) where T : DatabaseObject
        {
            if (!id.HasValue || coll == null)
            {
                return "—";
            }

            var item = coll.Get(id.Value);
            return item?.Name ?? "—";
        }

        private static string ResolveLibraryName(Guid? pluginId, DesktopAppViewModel model)
        {
            if (!pluginId.HasValue)
            {
                return "—";
            }

            var id = pluginId.Value;
            if (id == Guid.Empty)
            {
                return "Playnite";
            }

            if (model?.Extensions?.Plugins != null &&
                model.Extensions.Plugins.TryGetValue(id, out var plug) &&
                plug.Plugin is LibraryPlugin lib)
            {
                return lib.Name;
            }

            return id.ToString();
        }
    }
}
