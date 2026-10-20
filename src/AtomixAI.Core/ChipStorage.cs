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
        // Подтверждённые пользователем значения: единственный источник правды для отправки в ИИ.
        private readonly Dictionary<string, JObject> _overrides = new Dictionary<string, JObject>(StringComparer.Ordinal);
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
                var toApprove = new Dictionary<string, JObject>(StringComparer.Ordinal);
                foreach (var token in chipIds)
                {
                    if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                        throw new ArgumentException("Invalid chip ID.");
                    var id = token.Value<string>()!;
                    if (!seen.Add(id)) continue;

                    // Подтверждённое пользователем значение имеет приоритет над исходным.
                    JObject resolved;
                    bool isPending;
                    if (_overrides.TryGetValue(id, out var overrideEntry))
                    {
                        resolved = (JObject)overrideEntry.DeepClone();
                        isPending = _pending.ContainsKey(id);
                    }
                    else if (_pending.TryGetValue(id, out var pendingEntry))
                    {
                        resolved = (JObject)pendingEntry.DeepClone();
                        isPending = true;
                    }
                    else if (_approved.TryGetValue(id, out var approvedEntry))
                    {
                        resolved = (JObject)approvedEntry.DeepClone();
                        isPending = false;
                    }
                    else
                    {
                        throw new ArgumentException($"Stale chip [{id}]. Remove it and select it again from the menu.");
                    }

                    // Снапшот и хранилище не должны ссылаться на один и тот же объект.
                    snapshot.Add((JObject)resolved.DeepClone());

                    // Новые и отредактированные чипсы фиксируются в Model Data как единственный источник правды.
                    if (isPending || _overrides.ContainsKey(id))
                        toApprove[id] = resolved;
                }

                foreach (var pair in toApprove)
                {
                    if (!_approved.ContainsKey(pair.Key)) _approvedOrder.Add(pair.Key);
                    _approved[pair.Key] = pair.Value;
                }

                _pending.Clear();
                _overrides.Clear();
                return snapshot;
            }
        }

        // Фиксирует подтверждённое пользователем значение чипса, не трогая исходную запись.
        public void SetOverride(string id, JObject entry)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Missing chip ID.", nameof(id));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            lock (_sync)
            {
                _overrides[id] = (JObject)entry.DeepClone();
            }
        }

        // Возвращает запись чипса для построения оверрайда: приоритет у подтверждённой.
        public bool TryGetEntry(string id, out JObject? entry)
        {
            lock (_sync)
            {
                if (_overrides.TryGetValue(id, out var overrideEntry)) { entry = (JObject)overrideEntry.DeepClone(); return true; }
                if (_pending.TryGetValue(id, out var pendingEntry)) { entry = (JObject)pendingEntry.DeepClone(); return true; }
                if (_approved.TryGetValue(id, out var approvedEntry)) { entry = (JObject)approvedEntry.DeepClone(); return true; }
                entry = null;
                return false;
            }
        }

        public void DiscardPending()
        {
            lock (_sync)
            {
                _pending.Clear();
                _overrides.Clear();
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _pending.Clear();
                _approved.Clear();
                _approvedOrder.Clear();
                _overrides.Clear();
            } // IDs must never be reused, including after clear.
        }
    }
}
