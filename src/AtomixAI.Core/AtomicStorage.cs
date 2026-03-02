using System;
using System.Collections.Generic;
using System.Linq;

namespace AtomixAI.Core
{
    internal class StorageSlot
    {
        public object Value { get; set; }
        public HashSet<string> Tags { get; } = new HashSet<string>();
    }

    public static class AtomicStorage
    {
        private static readonly Dictionary<string, StorageSlot> _data = new Dictionary<string, StorageSlot>();
        private static readonly List<string> _history = new List<string>();
        private static readonly object _lockObj = new object();
        public static int MaxCapacity { get; set; } = 100;
        private const string LAST_KEY = "#_last";

        // --- ТОТ САМЫЙ МЕТОД ДЛЯ TRANSACTION MANAGER ---
        public static string[] GetCurrentContext()
        {
            lock (_lockObj)
            {
                // Возвращаем все ключи (алиасы), которые сейчас есть в словаре
                return _data.Keys.ToArray();
            }
        }

        public static void Set(string tag, object value)
        {
            if (string.IsNullOrWhiteSpace(tag) || tag.Equals("none", StringComparison.OrdinalIgnoreCase)) return;
            if (value == null) { Remove(tag); return; }

            lock (_lockObj)
            {
                if (_data.TryGetValue(tag, out var oldSlot)) oldSlot.Tags.Remove(tag);

                var newSlot = new StorageSlot { Value = value };
                newSlot.Tags.Add(tag);
                newSlot.Tags.Add(LAST_KEY);

                _data[tag] = newSlot;
                _data[LAST_KEY] = newSlot;

                UpdateHistory(tag);
                CheckCapacity();
            }
        }

        public static void Link(string sourceTag, string targetTag)
        {
            if (string.IsNullOrEmpty(targetTag) || sourceTag == targetTag) return;
            lock (_lockObj)
            {
                if (_data.TryGetValue(sourceTag, out var slot))
                {
                    slot.Tags.Add(targetTag);
                    _data[targetTag] = slot;
                    UpdateHistory(targetTag);
                }
            }
        }

        public static object Get(string tag)
        {
            lock (_lockObj)
            {
                return _data.TryGetValue(tag, out var slot) ? slot.Value : null;
            }
        }

        public static T Get<T>(string tag)
        {
            var val = Get(tag);
            return val is T ? (T)val : default;
        }

        public static void Remove(string tag)
        {
            lock (_lockObj)
            {
                if (_data.TryGetValue(tag, out var slot))
                {
                    foreach (var linkedName in slot.Tags.ToList())
                    {
                        _data.Remove(linkedName);
                        _history.Remove(linkedName);
                    }
                    slot.Value = null;
                }
            }
        }

        private static void UpdateHistory(string tag)
        {
            _history.Remove(tag);
            _history.Add(tag);
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
