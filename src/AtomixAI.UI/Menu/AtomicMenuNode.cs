using System.Collections.Generic;
using Newtonsoft.Json;

namespace AtomixAI.UI.Menu
{    public class AtomicMenuNode
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        // Добавляем этот атрибут, чтобы твой menu.js понимал свойство как item.name
        [JsonProperty("name")]
        public string Label { get; set; }

        [JsonProperty("group")]
        public string Group { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("hasChildren")]
        public bool HasChildren { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; }

        [JsonProperty("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        public static AtomicMenuNode Create(string id, string label, string group, bool hasChildren = false)
        {
            return new AtomicMenuNode { Id = id, Label = label, Group = group, HasChildren = hasChildren };
        }
    }
}
