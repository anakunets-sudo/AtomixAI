using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AtomixAI.UI.Commands
{
    public sealed class ChipPayload
    {
        public ChipPayload(string chipId, string chipLabel, JObject chipData)
        {
            ChipId = chipId;
            ChipLabel = chipLabel;
            ChipData = chipData;
        }

        [JsonProperty("chipId")]
        public string ChipId { get; }

        [JsonProperty("chipLabel")]
        public string ChipLabel { get; }

        [JsonProperty("chipData")]
        public JObject ChipData { get; }
    }
}
