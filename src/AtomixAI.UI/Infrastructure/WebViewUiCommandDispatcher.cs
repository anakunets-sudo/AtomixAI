using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using AtomixAI.UI.Commands;
using AtomixAI.UI.Commands.Selection;

namespace AtomixAI.UI.Infrastructure
{
    public static class WebViewUiCommandDispatcher
    {
        private const string SubContextAction = "GET_SUB_CONTEXT";

        // Каждый пункт меню (#) — отдельный класс. Новый пункт = новый класс + строка здесь.
        private static readonly List<IMenuItemUiCommand> _menuItemCommands = new List<IMenuItemUiCommand>
        {
            new SelectionMenuUiCmd(),
            new PickElementsInViewUiCmd(),
            new ElementParametersBranchUiCmd(),
            new InstanceParametersUiCmd(),
            new TypeParametersUiCmd(),
        };

        public static string Dispatch(string action, string itemId, UIApplication app)
        {
            if (!string.Equals(action, SubContextAction, StringComparison.OrdinalIgnoreCase))
                return null;

            var command = _menuItemCommands.FirstOrDefault(c => c.CanHandle(itemId));
            return command != null ? command.Execute(app, itemId) : MenuResponse.Empty();
        }
    }
}
