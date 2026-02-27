using System;
using System.Collections.Generic;
using System.Linq;

namespace AtomixAI.Core
{
    internal class StorageSlot
    {
        public object Value { get; set; }
        public HashSet<string> Aliases { get; } = new HashSet<string>();
    }

    public static class AtomicStorage
    {
        private static readonly Dictionary<string, StorageSlot> _data = new Dictionary<string, StorageSlot>();
        private static readonly List<string> _history = new List<string>();
        private static readonly object _lockObj = new object();
        public static int MaxCapacity { get; set; } = 100;
        private const string LAST_KEY = "_last";

        // --- ТОТ САМЫЙ МЕТОД ДЛЯ TRANSACTION MANAGER ---
        public static string[] GetCurrentContext()
        {
            lock (_lockObj)
            {
                // Возвращаем все ключи (алиасы), которые сейчас есть в словаре
                return _data.Keys.ToArray();
            }
        }

        public static void Set(string alias, object value)
        {
            if (string.IsNullOrWhiteSpace(alias) || alias.Equals("none", StringComparison.OrdinalIgnoreCase)) return;
            if (value == null) { Remove(alias); return; }

            lock (_lockObj)
            {
                if (_data.TryGetValue(alias, out var oldSlot)) oldSlot.Aliases.Remove(alias);

                var newSlot = new StorageSlot { Value = value };
                newSlot.Aliases.Add(alias);
                newSlot.Aliases.Add(LAST_KEY);

                _data[alias] = newSlot;
                _data[LAST_KEY] = newSlot;

                UpdateHistory(alias);
                CheckCapacity();
            }
        }

        public static void Link(string sourceAlias, string targetAlias)
        {
            if (string.IsNullOrEmpty(targetAlias) || sourceAlias == targetAlias) return;
            lock (_lockObj)
            {
                if (_data.TryGetValue(sourceAlias, out var slot))
                {
                    slot.Aliases.Add(targetAlias);
                    _data[targetAlias] = slot;
                    UpdateHistory(targetAlias);
                }
            }
        }

        public static object Get(string alias)
        {
            lock (_lockObj)
            {
                return _data.TryGetValue(alias, out var slot) ? slot.Value : null;
            }
        }

        public static T Get<T>(string alias)
        {
            var val = Get(alias);
            return val is T ? (T)val : default;
        }

        public static void Remove(string alias)
        {
            lock (_lockObj)
            {
                if (_data.TryGetValue(alias, out var slot))
                {
                    foreach (var linkedName in slot.Aliases.ToList())
                    {
                        _data.Remove(linkedName);
                        _history.Remove(linkedName);
                    }
                    slot.Value = null;
                }
            }
        }

        private static void UpdateHistory(string alias)
        {
            _history.Remove(alias);
            _history.Add(alias);
        }

        private static void CheckCapacity()
        {
            while (_history.Count > MaxCapacity) Remove(_history[0]);
        }

        public static void Clear()
        {
            lock (_lockObj) { _data.Clear(); _history.Clear(); }
        }
    }
}
