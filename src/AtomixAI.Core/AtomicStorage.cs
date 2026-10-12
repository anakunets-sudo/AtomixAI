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
        private static readonly Dictionary<string, StorageSlot> _data = new Dictionary<string, StorageSlot>(StringComparer.OrdinalIgnoreCase);
        // Служебный порядок записи всех тегов (для GetLastTag и вытеснения по ёмкости).
        private static readonly List<string> _recentTags = new List<string>();
        // Финальные теги успешных операций, имеющие смысл для пользователя (меню Recent). Новые сверху.
        private static readonly List<string> _userTags = new List<string>();
        private static readonly object _lockObj = new object();
        public static int MaxCapacity { get; set; } = 1000;
        public static int MaxUserTags { get; set; } = 100;
        //private const string LAST_KEY = "#_last";

        // --- ТОТ САМЫЙ МЕТОД ДЛЯ TRANSACTION MANAGER ---
        public static string[] GetCurrentContext()
        {
            lock (_lockObj)
            {
                // Возвращаем все ключи (алиасы), которые сейчас есть в словаре
                return _data.Keys.ToArray();
            }
        }

        // Тег последней успешно зафиксированной операции (null, если ещё ничего не было).
        public static string? GetLastTag()
        {
            lock (_lockObj)
            {
                return _recentTags.Count > 0 ? _recentTags[_recentTags.Count - 1] : null;
            }
        }

        // Фиксирует финальный тег успешной цепочки как «пользовательский» (для меню Recent).
        public static void MarkUserTag(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            lock (_lockObj)
            {
                _userTags.Remove(tag);
                _userTags.Insert(0, tag);
                if (_userTags.Count > MaxUserTags) _userTags.RemoveAt(_userTags.Count - 1);
            }
        }

        // Теги для меню Recent: новые сверху, старые ниже.
        public static string[] GetUserTags()
        {
            lock (_lockObj)
            {
                return _userTags.ToArray();
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
                //newSlot.Tags.Add(LAST_KEY);

                _data[tag] = newSlot;
                //_data[LAST_KEY] = newSlot;

                TrackRecentTag(tag);
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
                    TrackRecentTag(targetTag);
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
                        _recentTags.Remove(linkedName);
                    }
                    slot.Value = null;
                }
            }
        }

        private static void TrackRecentTag(string tag)
        {
            _recentTags.Remove(tag);
            _recentTags.Add(tag);
        }

        private static void CheckCapacity()
        {
            while (_recentTags.Count > MaxCapacity) Remove(_recentTags[0]);
        }

        public static void Clear()
        {
            lock (_lockObj) { _data.Clear(); _recentTags.Clear(); _userTags.Clear(); }
        }
    }
}
