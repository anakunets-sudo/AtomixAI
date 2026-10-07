
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace AtomixAI.Core
{
    /// <summary>
    /// Универсальный динамический контейнер данных (Шина Данных).
    /// Используется для передачи состояния между командами (Context) и ввода параметров ИИ (Params).
    /// </summary>
    public class DynamicBimContract
    {
        // Внутреннее хранилище всех полей контракта
        private readonly Dictionary<string, object> _fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Записывает значение по ключу. Если ключ существует — перезаписывает его.
        /// </summary>
        public void Set(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _fields[key] = value;
        }

        /// <summary>
        /// Проверяет наличие ключа в контракте.
        /// </summary>
        public bool Contains(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && _fields.ContainsKey(key);
        }

        /// <summary>
        /// Удаляет поле из контракта.
        /// </summary>
        public bool Remove(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && _fields.Remove(key);
        }

        /// <summary>
        /// Очищает все поля контракта.
        /// </summary>
        public void Clear()
        {
            _fields.Clear();
        }

        public bool Get<T>(string key, out T value)
        {
            value = default;

            // 1. Проверяем физическое наличие ключа и его заполненность
            if (string.IsNullOrWhiteSpace(key) || !_fields.ContainsKey(key) || _fields[key] == null)
            {
                return false;
            }

            object rawValue = _fields[key];
            Type targetType = typeof(T);

            // 2. Быстрый возврат, если типы данных совпадают на 100%
            if (rawValue is T variable)
            {
                value = variable;
                return true;
            }

            // 3. ПОДДЕРЖКА NEWTONSOFT JSON И SYSTEM.TEXT.JSON
            string rawTypeName = rawValue.GetType().FullName;
            if (rawTypeName.Contains("Newtonsoft.Json.Linq") || rawTypeName.Contains("System.Text.Json"))
            {
                try
                {
                    if (rawValue is JToken jsonToken)
                    {
                        value = jsonToken.ToObject<T>();
                        return true;
                    }
                }
                catch
                {
                    // Если структурная конвертация не удалась, откатываемся к обработке через строку
                    rawValue = rawValue.ToString();
                }
            }

            // 4. Обработка пустых или незаполненных ИИ строк
            if (rawValue is string strValue && string.IsNullOrWhiteSpace(strValue))
            {
                if (targetType == typeof(string))
                {
                    value = (T)(object)string.Empty;
                    return true;
                }
                return false; // Если просили не строку, а пришла пустота — это провал конвертации
            }

            try
            {
                // 5. Безопасная конвертация строк в типы с плавающей точкой (защита от локали Windows)
                if ((targetType == typeof(double) || targetType == typeof(float)) && rawValue is string doubleStr)
                {
                    if (double.TryParse(doubleStr.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedDouble))
                    {
                        value = (T)Convert.ChangeType(parsedDouble, targetType, CultureInfo.InvariantCulture);
                        return true;
                    }
                    return false;
                }

                // 6. Поддержка конвертации Перечислений (Enum)
                if (targetType.IsEnum)
                {
                    value = (T)Enum.Parse(targetType, rawValue.ToString(), true);
                    return true;
                }

                // 7. Поддержка Nullable-типов (например, int?, double?)
                Type underlyingType = Nullable.GetUnderlyingType(targetType);
                if (underlyingType != null)
                {
                    value = (T)Convert.ChangeType(rawValue, underlyingType, CultureInfo.InvariantCulture);
                    return true;
                }

                // 8. Стандартное .NET приведение типов (ChangeType) — int, long, bool, string и т.д.
                value = (T)Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DynamicBimContract Error]: Failed to cast field '{key}' from {rawValue.GetType().Name} to {targetType.Name}. Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Позволяет получить отладочный слепок всех текущих полей контракта в виде словаря.
        /// </summary>
        public Dictionary<string, object> Dump()
        {
            return _fields.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        }

        public string Keys()
        {
            if (_fields.Count == 0)
            {
                return "(No keys)";
            }

            return string.Join(", ", _fields.Keys);
        }
    }
}
