using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace AtomixAI.Core
{
    public sealed class ChipStorage
    {
        public static ChipStorage Shared { get; } = new ChipStorage();

        private readonly object _sync = new object();
        private readonly Dictionary<string, JObject> _pending = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, JObject> _approved = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly List<string> _approvedOrder = new List<string>();
        private long _lastId;

        public string CreatePending(string label, JObject data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            lock (_sync)
            {
                var id = "chip_" + checked(++_lastId).ToString(CultureInfo.InvariantCulture);
                _pending.Add(id, new JObject
                {
                    ["id"] = id,
                    ["label"] = label,
                    ["data"] = data.DeepClone()
                });
                return id;
            }
        }

        public List<JObject> GetApprovedEntries()
        {
            lock (_sync)
            {
                var entries = new List<JObject>(_approvedOrder.Count);
                for (int i = _approvedOrder.Count - 1; i >= 0; i--)
                    entries.Add((JObject)_approved[_approvedOrder[i]].DeepClone());
                return entries;
            }
        }

        public JArray CommitAndCreateSnapshot(JArray? chipIds)
        {
            if (chipIds == null) throw new ArgumentException("Missing chipIds array.");
            lock (_sync)
            {
                var snapshot = new JArray();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var pendingToApprove = new List<string>();
                foreach (var token in chipIds)
                {
                    if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                        throw new ArgumentException("Invalid chip ID.");
                    var id = token.Value<string>()!;
                    if (!seen.Add(id)) continue;

                    if (_approved.TryGetValue(id, out var approvedEntry))
                    {
                        snapshot.Add(approvedEntry.DeepClone());
                        continue;
                    }

                    if (!_pending.TryGetValue(id, out var pendingEntry))
                        throw new ArgumentException($"Stale chip [{id}]. Remove it and select it again from the menu.");

                    snapshot.Add(pendingEntry.DeepClone());
                    pendingToApprove.Add(id);
                }

                foreach (string id in pendingToApprove)
                {
                    _approved.Add(id, _pending[id]);
                    _approvedOrder.Add(id);
                    _pending.Remove(id);
                }

                _pending.Clear();
                return snapshot;
            }
        }

        public void DiscardPending()
        {
            lock (_sync)
            {
                _pending.Clear();
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _pending.Clear();
                _approved.Clear();
                _approvedOrder.Clear();
            } // IDs must never be reused, including after clear.
        }
    }
}
