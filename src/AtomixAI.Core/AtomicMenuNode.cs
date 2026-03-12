using Newtonsoft.Json;
using System.Collections.Generic;

namespace AtomixAI.Core
{
    /// <summary>
    /// Представляет одну строку в интеллектуальном меню TipTap.
    /// </summary>
    public class AtomicMenuNode
    {
        [JsonProperty("id")]
        public string Id { get; set; } // UniqueId элемента или имя тега (#_last)

        [JsonProperty("label")]
        public string Label { get; set; } // То, что видит юзер (имя стены, параметра)

        [JsonProperty("group")]
        public string Group { get; set; } // "Selection", "Views", "Tags", "Recent"

        [JsonProperty("description")]
        public string Description { get; set; } // Подпись (например, "Instance" или "Type")

        [JsonProperty("hasChildren")]
        public bool HasChildren { get; set; } // Будет ли стрелочка "Вправо" (→)

        [JsonProperty("icon")]
        public string Icon { get; set; } // Имя иконки для UI (если нужно)

        [JsonProperty("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        public static AtomicMenuNode Create(string id, string label, string group, bool hasChildren = false)
        {
            return new AtomicMenuNode { Id = id, Label = label, Group = group, HasChildren = hasChildren };
        }
    }
}
