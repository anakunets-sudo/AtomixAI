using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Core
{
    public static class AtomicStorage
    {
        private static readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        private static readonly List<string> _history = new List<string>();
        public static int MaxCapacity { get; set; } = 20;

        // ТОТ САМЫЙ МЕТОД:
        public static bool Has(string alias) => !string.IsNullOrEmpty(alias) && _data.ContainsKey(alias);

        public static void Set(string alias, object value)
        {
            lock (_data)
            {
                if (_data.ContainsKey(alias)) _history.Remove(alias);
                else if (_history.Count >= MaxCapacity)
                {
                    var oldest = _history[0];
                    _history.RemoveAt(0);
                    _data.Remove(oldest);
                }
                _history.Add(alias);
                _data[alias] = value;
            }
        }

        public static object Get(string alias) => _data.TryGetValue(alias, out var val) ? val : null;
        public static string[] GetCurrentContext() => _history.ToArray();
    }
}
