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
        private const string CreateChipAction = "CREATE_CHIP";

        // Каждый пункт меню (#) — отдельный класс. Новый пункт = новый класс + строка здесь.
        private static readonly List<IMenuItemUiCommand> _menuItemCommands = new List<IMenuItemUiCommand>
        {
            new RecentTagsMenuUiCmd(),
            new SelectionMenuUiCmd(),
            new PickElementsInViewUiCmd(),
            new ElementParametersBranchUiCmd(),
            new InstanceParametersUiCmd(),
            new TypeParametersUiCmd(),
        };

        public static string Dispatch(string action, string itemId, string requestId, UIApplication app)
        {
            if (string.Equals(action, CreateChipAction, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        action = "CHIP_CREATED",
                        requestId,
                        chip = ParameterMenuBuilder.CreateChip(app, itemId)
                    });
                }
                catch (Exception ex)
                {
                    return Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        action = "CHIP_CREATE_ERROR",
                        requestId,
                        message = ex.Message
                    });
                }
            }

            if (!string.Equals(action, SubContextAction, StringComparison.OrdinalIgnoreCase))
                return null;

            var command = _menuItemCommands.FirstOrDefault(c => c.CanHandle(itemId));
            return command != null ? command.Execute(app, itemId) : MenuResponse.Empty();
        }
    }
}
