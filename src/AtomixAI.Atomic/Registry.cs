using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AtomixAI.Core;

namespace AtomixAI.Atomic
{
    public static class Registry
    {
        public static string GetToolsJson()
        {
            var tools = new List<object>();

            // 1. Находим все команды через рефлексию
            var commandTypes = Assembly.GetAssembly(typeof(Registry))
                .GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in commandTypes)
            {
                var info = type.GetCustomAttribute<AtomicInfoAttribute>();
                if (info == null) continue;

                // 2. Генерируем схему параметров на основе свойств класса
                var propertiesSchema = new Dictionary<string, object>();
                var requiredParams = new List<string>();

                foreach (var prop in type.GetProperties())
                {
                    var paramAttr = prop.GetCustomAttribute<AtomicParamAttribute>();
                    if (paramAttr == null) continue;

                    // Описываем каждый параметр для ИИ
                    propertiesSchema[prop.Name] = new
                    {
                        type = GetJsonType(prop.PropertyType),
                        description = paramAttr.Description +
                                     (prop.PropertyType == typeof(double) ? " (Specify units, e.g. '500mm' or '10ft')" : "")
                    };

                    // По умолчанию считаем все параметры с атрибутом обязательными
                    requiredParams.Add(prop.Name);
                }

                // 3. Формируем структуру MCP Tool
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

            return Newtonsoft.Json.JsonConvert.SerializeObject(new { tools });
        }

        private static string GetJsonType(Type type)
        {
            if (type == typeof(double) || type == typeof(int) || type == typeof(float)) return "string"; // ИИ шлет "500mm"
            if (type == typeof(bool)) return "boolean";
            return "string";
        }
    }
}
