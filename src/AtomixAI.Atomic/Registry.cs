using AtomixAI.Core;
using Newtonsoft.Json;
using System;
using System.Collections;
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

            // 1. Находим все команды, реализующие IAtomicCommand
            var commandTypes = Assembly.GetAssembly(typeof(AtomicSearchFactory))
                .GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in commandTypes)
            {
                var info = type.GetCustomAttribute<AtomicInfoAttribute>();
                if (info == null) continue;

                var propertiesSchema = new Dictionary<string, object>();
                var requiredParams = new List<string>();

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var paramAttr = Attribute.GetCustomAttribute(prop, typeof(AtomicParamAttribute), true) as AtomicParamAttribute;
                    if (paramAttr == null) continue;

                    string extraHint = "";
                    if (prop.Name == "In") extraHint = " (Use '#tag' or '_last')";
                    else if (prop.Name == "Out") extraHint = " (Create new '#tag')";

                    // ОПРЕДЕЛЕНИЕ ТИПА (С поддержкой JSON Schema / Gemini)
                    var propType = prop.PropertyType;
                    string jsonType = GetJsonType(propType);

                    // Создаем структуру параметра
                    var propDef = new Dictionary<string, object>
            {
                { "type", jsonType },
                { "description", paramAttr.Description + extraHint +
                  (propType == typeof(double) ? " (Specify units, e.g. '500mm')" : "") }
            };

                    // КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: Добавляем 'items' для массивов
                    if (jsonType == "array")
                    {
                        Type elementType = typeof(string);
                        if (propType.IsArray) elementType = propType.GetElementType();
                        else if (propType.IsGenericType) elementType = propType.GetGenericArguments().FirstOrDefault() ?? typeof(string);

                        // If the array contains a DICTIONARY (as in our factory)
                        if (typeof(System.Collections.IDictionary).IsAssignableFrom(elementType) || elementType == typeof(object))
                        {
                            propDef["items"] = new
                            {
                                type = "object",
                                properties = new { } // Gemini accepts an empty object if the type object is specified
                            };
                        }
                        else
                        {
                            propDef["items"] = new { type = GetJsonType(elementType) };
                        }
                    }

                    propertiesSchema[prop.Name] = propDef;

                    if (paramAttr.IsRequired)
                        requiredParams.Add(prop.Name);
                }

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

            return JsonConvert.SerializeObject(new { tools });
        }

        // Вспомогательный метод для маппинга типов
        private static string GetJsonType(Type type)
        {
            if (type == typeof(int) || type == typeof(double) || type == typeof(float) || type == typeof(decimal))
                return "number";
            if (type == typeof(bool))
                return "boolean";
            // Проверка на коллекцию (но не строку)
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
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

                sb.AppendLine("  Usage: Use '#tag_name' to pass data between tools. '#' denotes a memory reference.");
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
