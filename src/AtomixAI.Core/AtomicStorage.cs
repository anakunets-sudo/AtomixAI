using System;
using System.Collections.Generic;
using System.Linq;

namespace AtomixAI.Core
{
    public static class AtomicStorage
    {
        private static readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        private static readonly List<string> _history = new List<string>();
        private static readonly object _lockObj = new object();
        public static int MaxCapacity { get; set; } = 100;

        public static bool Has(string alias)
        {
            lock (_lockObj)
            {
                bool exists = !string.IsNullOrEmpty(alias) && _data.ContainsKey(alias);
                System.Diagnostics.Debug.WriteLine($"[AtomicStorage.Has] '{alias}': {exists}");
                return exists;
            }
        }

        public static void Set(string alias, object value)
        {
            lock (_lockObj)
            {
                System.Diagnostics.Debug.WriteLine($"[AtomicStorage.Set] ⇒ '{alias}' ({value?.GetType().Name ?? "null"})");

                if (_data.ContainsKey(alias))
                {
                    System.Diagnostics.Debug.WriteLine($"[AtomicStorage.Set] Обновление существующего значения");
                    _history.Remove(alias);
                }
                else if (_history.Count >= MaxCapacity)
                {
                    var oldest = _history[0];
                    System.Diagnostics.Debug.WriteLine($"[AtomicStorage.Set] Лимит ({MaxCapacity}), удаляю старейший: {oldest}");
                    _history.RemoveAt(0);
                    _data.Remove(oldest);
                }

                _history.Add(alias);
                _data[alias] = value;

                System.Diagnostics.Debug.WriteLine($"[AtomicStorage.Set] ✓ Сохранено (Total: {_data.Count}/{MaxCapacity})");
            }
        }

        public static object Get(string alias)
        {
            lock (_lockObj)
            {
                bool found = _data.TryGetValue(alias, out var val);
                System.Diagnostics.Debug.WriteLine($"[AtomicStorage.Get] '{alias}': {(found ? "✓ НАЙДЕНО" : "✗ НЕ НАЙДЕНО")}");
                return val;
            }
        }

        public static T Get<T>(string alias)
        {
            var val = Get(alias);
            return val is T ? (T)val : default(T);
        }

        public static void Remove(string alias)
        {
            lock (_lockObj)
            {
                if (_data.Remove(alias))
                {
                    _history.Remove(alias);
                    System.Diagnostics.Debug.WriteLine($"[AtomicStorage.Remove] ✓ '{alias}' удалён");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AtomicStorage.Remove] ✗ '{alias}' не найден для удаления");
                }
            }
        }

        public static string[] GetCurrentContext()
        {
            lock (_lockObj)
            {
                var result = _history.ToArray();
                System.Diagnostics.Debug.WriteLine($"[AtomicStorage.GetCurrentContext] {result.Length} элементов: {string.Join(", ", result.Take(5))}...");
                return result;
            }
        }

        public static void Clear()
        {
            lock (_lockObj)
            {
                System.Diagnostics.Debug.WriteLine($"[AtomicStorage.Clear] Очистка {_data.Count} элементов");
                _data.Clear();
                _history.Clear();
                System.Diagnostics.Debug.WriteLine("[AtomicStorage.Clear] ✓ Очищено");
            }
        }
    }
}