using Playnite.Controllers;
using Playnite.Database;
using Playnite.DesktopApp.ViewModels;
using Playnite.DesktopApp.Windows;
using Playnite.Plugins;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.Settings;
using Playnite.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Playnite.DesktopApp
{
    public class DesktopGamesEditor : GamesEditor
    {
        public DesktopGamesEditor(
            GameDatabase database,
            GameControllerFactory controllerFactory,
            PlayniteSettings appSettings,
            IDialogsFactory dialogs,
            ExtensionFactory extensions,
            PlayniteApplication app,
            IActionSelector actionSelector) : base(
                database,
                controllerFactory,
                appSettings,
                dialogs,
                extensions,
                app,
                actionSelector)
        {
        }

        public bool? SetGameCategories(Game game)
        {
            var model = new CategoryConfigViewModel(new CategoryConfigWindowFactory(), Database, game);
            return model.OpenView();
        }

        public bool? SetGamesCategories(List<Game> games)
        {
            var model = new CategoryConfigViewModel(new CategoryConfigWindowFactory(), Database, games);
            return model.OpenView();
        }

        public bool? EditGame(Game game)
        {
            var model = new GameEditViewModel(
                            game,
                            Database,
                            new GameEditWindowFactory(),
                            Dialogs,
                            new ResourceProvider(),
                            Extensions,
                            AppSettings);
            return model.OpenView();
        }

        public bool? EditGames(List<Game> games)
        {
            var model = new GameEditViewModel(
                            games,
                            Database,
                            new GameEditWindowFactory(),
                            Dialogs,
                            new ResourceProvider(),
                            Extensions,
                            AppSettings);
            return model.OpenView();
        }

        public bool? EditMergeGroup(IList<Game> members)
        {
            if (members == null || members.Count < 2)
            {
                return null;
            }

            var liveMembers = members
                .Select(g => Database.Games.Get(g.Id))
                .Where(g => g != null)
                .ToList();
            if (liveMembers.Count < 2)
            {
                return null;
            }

            var model = new GameEditViewModel(
                liveMembers,
                Database,
                new GameEditWindowFactory(),
                Dialogs,
                new ResourceProvider(),
                Extensions,
                AppSettings);
            return model.OpenView();
        }
    }
}
