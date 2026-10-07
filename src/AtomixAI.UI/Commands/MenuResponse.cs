using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;

namespace AtomixAI.UI.Commands
{
    // Общий формат ответа для menu.js
    public static class MenuResponse
    {
        public static string Items(IEnumerable<object> items)
        {
            return JsonConvert.SerializeObject(new
            {
                action = "MENU_DATA",
                payload = new { items = items }
            });
        }

        public static string Empty() => Items(new List<object>());

        public static string Label(string icon, string text)
        {
            return $"<span class=\"menu-icon\">{icon}</span> {WebUtility.HtmlEncode(text)}";
        }
    }
}
