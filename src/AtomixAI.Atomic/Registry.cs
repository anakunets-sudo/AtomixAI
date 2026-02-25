using AtomixAI.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AtomixAI.Atomic
{
    public static class Registry
    {
        public static string GetToolsJson()
        {
            var tools = new List<object>();

            // 1. Находим все команды, реализующие IAtomicCommand (включая базовый класс)
            var commandTypes = Assembly.GetAssembly(typeof(Registry))
                .GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in commandTypes)
            {
                var info = type.GetCustomAttribute<AtomicInfoAttribute>();
                if (info == null) continue;

                var propertiesSchema = new Dictionary<string, object>();
                var requiredParams = new List<string>();

                // 2. Сканируем свойства С УЧЕТОМ НАСЛЕДОВАНИЯ
                // GetProperties() по умолчанию берет публичные свойства всей иерархии
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    // КЛЮЧЕВОЙ МОМЕНТ: Ищем атрибут вверх по дереву наследования (третий параметр true)
                    var paramAttr = Attribute.GetCustomAttribute(prop, typeof(AtomicParamAttribute), true) as AtomicParamAttribute;

                    if (paramAttr == null) continue;

                    // Описываем тип и описание для LLM
                    propertiesSchema[prop.Name] = new
                    {
                        type = GetJsonType(prop.PropertyType),
                        description = paramAttr.Description +
                                     (prop.PropertyType == typeof(double) ? " (Specify units, e.g. '500mm' or '10ft')" : "")
                    };

                    // 3. Добавляем в список обязательных ТОЛЬКО если флаг IsRequired в атрибуте равен true
                    if (paramAttr.IsRequired)
                    {
                        requiredParams.Add(prop.Name);
                    }
                }

                // 4. Формируем структуру инструмента (совместимо с MCP / OpenAI Function Calling)
                tools.Add(new
                {
                    name = info.Name,
                    description = info.Description,
                    inputSchema = new
                    {
                        type = "object",
                        properties = propertiesSchema,
                        required = requiredParams
                    }
                });
            }

            // Возвращаем упакованный JSON для Python-моста
            return JsonConvert.SerializeObject(new { tools });
        }

        private static string GetJsonType(Type type)
        {
            if (type == typeof(int) || type == typeof(double) || type == typeof(float))
                return "number";
            if (type == typeof(bool))
                return "boolean";
            if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
                return "array";

            return "string";
        }

        /// <summary>
        /// Собираем все классы и отдаем в инструкцию в orchestrator.py
        /// </summary>
        /// <returns></returns>
        public static string GetAiManual()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("### AVAILABLE BIM TOOLS & LOGIC:");

            var commandTypes = Assembly.GetAssembly(typeof(Registry)).GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in commandTypes)
            {
                var info = type.GetCustomAttribute<AtomicInfoAttribute>();
                if (info == null) continue;

                sb.AppendLine($"- ACTION: '{info.Name}'");
                sb.AppendLine($"  Description: {info.Description}");

                // ИСПРАВЛЕНО: выбираем пары (Имя свойства, Атрибут), чтобы проверить IsRequired
                var props = type.GetProperties()
                    .Select(p => new {
                        p.Name,
                        Attr = Attribute.GetCustomAttribute(p, typeof(AtomicParamAttribute), true) as AtomicParamAttribute
                    })
                    .Where(x => x.Attr != null);

                sb.Append("  Parameters: ");
                // ИСПРАВЛЕНО: используем x.Attr.IsRequired для выделения жирным
                sb.AppendLine(string.Join(", ", props.Select(x => x.Attr.IsRequired ? $"**{x.Name}**" : x.Name)));
                sb.AppendLine();
            }
            return sb.ToString();
        }


        public static string GetActiveContentStateAliases()
        {
            // Используем твой метод GetCurrentContext()
            var activeAliases = AtomicStorage.GetCurrentContext();

            if (activeAliases.Length == 0)
                return "CURRENT REBIT MEMORY: [Empty]. You must use 'Out' to save data first.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("### ACTIVE BIM ALIASES IN MEMORY:");
            foreach (var alias in activeAliases)
            {
                var data = AtomicStorage.Get(alias);
                // Достаем тип данных для ИИ (Wall, List, и т.д.)
                string typeName = data?.GetType().Name ?? "Unknown";
                sb.AppendLine($"- '{alias}' (Type: {typeName})");
            }
            return sb.ToString();
        }
    }
}
