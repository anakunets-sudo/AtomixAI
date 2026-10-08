using AtomixAI.Core;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

    namespace AtomixAI.Atomic
    {
    public static class Registry
    {
        // КЭШ МАНИФЕСТА: Твой монолитный промт собирается ОДИН раз при старте Ревита,
        // гарантируя 100% стабильность работы Google Prompt Caching на продакшене.
        public static string CachedAiManual { get; set; } = string.Empty;

        /// <summary>
        /// Генерирует строгую схему для нативных Gemini Function Calling.
        /// Поддерживает автоматический разворот классов умных схем (SchemaType).
        /// </summary>
        public static string GetToolsJson()
        {
            var tools = new List<object>();

            var commandTypes = typeof(Registry).Assembly
                .GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in commandTypes)
            {
                var info = type.GetCustomAttribute<AiInfoAttribute>();
                if (info == null) continue;

                var propertiesSchema = new Dictionary<string, object>();
                var requiredParams = new List<string>();

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var paramAttr = Attribute.GetCustomAttribute(prop, typeof(AiParamAttribute), true) as AiParamAttribute;
                    if (paramAttr == null) continue;

                    string extraHint = "";
                    if (prop.Name.Equals("Context", StringComparison.OrdinalIgnoreCase)) extraHint = " (Accepts historical '#tag')";
                    else if (prop.Name.Equals("Out", StringComparison.OrdinalIgnoreCase)) extraHint = " (Create new unique '#tag')";

                    string jsonType = "string";

                    // СВЯЩЕННЫЙ ГРААЛЬ СТЫКОВКИ JSON СХЕМЫ:
                    // Если у атрибута задан SchemaType — это сложный вложенный объект (object)
                    if (paramAttr.SchemaType != null)
                    {
                        jsonType = "object";
                        // Строим вложенную JSON-схему из внутренностей класса (например, WallParamsSchema)
                        var innerProps = new Dictionary<string, object>();
                        var schemaProps = paramAttr.SchemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                        foreach (var sProp in schemaProps)
                        {
                            var descAttr = sProp.GetCustomAttribute<DescriptionAttribute>();
                            if (descAttr == null) continue;

                            string innerDesc = descAttr.Description;
                            // Инжектируем системные правила для геометрии
                            if (sProp.Name.Contains("Point") || sProp.Name.Contains("Curve"))
                                innerDesc += " MANDATORY if Context/In is empty. Enter \"\" if Context has geometry.";

                            innerProps[sProp.Name] = new
                            {
                                type = "string", // Все поля внутри схем ИИ заполняет как строки (координаты, имена)
                                description = innerDesc
                            };
                        }

                        propertiesSchema[prop.Name] = new
                        {
                            type = "object",
                            description = "Structured target parameters object.",
                            properties = innerProps
                        };
                    }
                    else
                    {
                        // Стандартная обработка плоских параметров
                        var propType = prop.PropertyType;
                        jsonType = GetJsonType(propType);

                        var propDef = new Dictionary<string, object>
                        {
                            { "type", jsonType },
                            { "description", paramAttr.Description + extraHint + (propType == typeof(double) ? " (Specify units, e.g. '500mm')" : "") }
                        };

                        if (jsonType == "array")
                        {
                            Type elementType = typeof(string);
                            if (propType.IsArray) elementType = propType.GetElementType();
                            else if (propType.IsGenericType) elementType = propType.GetGenericArguments().FirstOrDefault() ?? typeof(string);

                            if (typeof(System.Collections.IDictionary).IsAssignableFrom(elementType) || elementType == typeof(object))
                            {
                                propDef["items"] = new { type = "object", properties = new { } };
                            }
                            else
                            {
                                propDef["items"] = new { type = GetJsonType(elementType) };
                            }
                        }

                        propertiesSchema[prop.Name] = propDef;
                    }

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

            return JsonConvert.SerializeObject(new { tools }, Formatting.Indented);
        }

        private static string GetJsonType(Type type)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            if (underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(short))
                return "integer";

            if (underlyingType == typeof(double) || underlyingType == typeof(float) || underlyingType == typeof(decimal))
                return "double";

            if (underlyingType == typeof(bool))
                return "boolean";

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(underlyingType) && underlyingType != typeof(string))
                return "array";

            return "string";
        }


        /// <summary>
        /// Собирает ультра-компактный XML манифест для Системного Контекста ИИ.
        /// Полностью разворачивает свойства классов-схем в компактные JSON-трафареты.
        /// </summary>
        public static string GetAiManual()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<EXECUTION_PROTOCOL>\n");
            sb.Append("1. Think First: Analyze the user request. Is it a QUERY (find/show) or an ACTION (create/modify)?\n");
            sb.Append("2. Minimalism: DO NOT use 'Construction' or 'Modification' tools unless the user explicitly asked to change the model.\n");
            sb.Append("3. Batching: Return a SINGLE JSON object with a 'sequence' array.\n");
            sb.Append("4. Data Linking: Use '#tag_name_1' to pass data. Set 'Out' in step 1, use as 'In' in step 2.\n");
            sb.Append("5. All units of measurement MUST include units (e.g., '5000mm', '15ft').\n");
            sb.Append("6. Context Awareness: Use <ACTIVE_REVIT_MEMORY_TAGS> to see what elements are already saved in memory tags.\n");
            sb.Append("</EXECUTION_PROTOCOL>\n");

            sb.Append("<MANDATORY_OUTPUT_FORMAT>\n");
            sb.Append("The output MUST strictly match this JSON structure:\n");
            sb.Append("{");
            sb.Append(" \"thought\": \"Brief explanation of your plan\",");
            sb.Append(" \"sequence\": [");
            sb.Append(" { \"name\": \"command_name\", \"arguments\": { \"In\": \"#tag\", \"Params\": { ... }, \"Out\": \"#my_tag\" } }");
            sb.Append(" ]");
            sb.Append(" }\n");
            sb.Append("</MANDATORY_OUTPUT_FORMAT>\n");

            sb.Append("<BIM_TOOLS>\n");

            var commandTypes = typeof(Registry).Assembly.GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .Select(t => new { Type = t, Info = t.GetCustomAttribute<AiInfoAttribute>() })
                .Where(x => x.Info != null)
                .GroupBy(x => x.Info.Group);

            foreach (var group in commandTypes)
            {
                var firstItem = group.First();
                var fieldInfo = typeof(AtomicGroupType).GetField(firstItem.Info.Group.ToString());

                System.ComponentModel.DescriptionAttribute descAttr = null;
                if (fieldInfo != null)
                {
                    descAttr = (System.ComponentModel.DescriptionAttribute)Attribute.GetCustomAttribute(fieldInfo, typeof(System.ComponentModel.DescriptionAttribute));
                }

                string groupDesc = descAttr != null ? descAttr.Description : group.Key.ToString();

                // Имя тега группы в верхнем регистре (CREATION, SEARCH)
                sb.Append($"<{group.Key.ToString().ToUpper()}>\n");
                sb.Append($"<desc>{groupDesc}</desc>\n");

                foreach (var item in group)
                {
                    string keywordsStr = string.Join(",", item.Info.Keywords);

                    // Схлопываем инструмент в одну компактную строку-тег
                    sb.Append($"<tool name=\"{item.Info.Name}\" kw=\"{keywordsStr}\" desc=\"{item.Info.Description}\">");

                    var props = item.Type.GetProperties()
                        .Select(p => new {
                            p.Name,
                            Prop = p,
                            Attr = Attribute.GetCustomAttribute(p, typeof(AiParamAttribute), true) as AiParamAttribute
                        })
                        .Where(x => x.Attr != null);

                    if (props.Any())
                    {
                        var paramStrings = props.Select(x => {
                            string reqStr = x.Attr.IsRequired ? " req=\"true\"" : "";
                            string typeStr = GetFriendlyTypeName(x.Prop.PropertyType);
                            string descStr = x.Attr.Description;

                            // СВЯЩЕННЫЙ ГРААЛЬ ПОСТРОЕНИЯ XML МАНИФЕСТА:
                            // Если у атрибута задан SchemaType — это 100% класс умной схемы! Разворачиваем его!
                            // === ФИКСАЦИЯ РАЗВОРУТА УМНОЙ СХЕМЫ В REGISTRY.CS ===
                            if (x.Attr.SchemaType != null)
                            {
                                typeStr = "json"; // Меняем тип на json для ИИ

                                var innerLines = new List<string>();

                                // Вытаскиваем вообще все публичные свойства класса схемы
                                var schemaProps = x.Attr.SchemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                                foreach (var sProp in schemaProps)
                                {
                                    // Пытаемся прочитать ЛЮБОЙ из двух атрибутов, которые ты мог повесить!
                                    string cleanDesc = string.Empty;

                                    var customAiParam = sProp.GetCustomAttribute<AiParamAttribute>();

                                    if (customAiParam != null)
                                        cleanDesc = customAiParam.Description;
                                    else
                                        continue; // Если описания нет вообще — пропускаем свойство

                                    // Инжектируем жесткие правила конвейера для геометрии
                                    if (sProp.Name.Contains("Point") || sProp.Name.Contains("Curve") || sProp.Name.Contains("Location"))
                                    {
                                        cleanDesc += " MANDATORY if In/Context is empty. Enter \\\"\\\" if Context has geometry.";
                                    }

                                    // Экранируем кавычки для XML-атрибута, чтобы промт не ломал парсер гугла
                                    innerLines.Add($"\\\\'{sProp.Name}\\\\': \\\\'{cleanDesc}\\\\'");
                                }

                                descStr = "{" + string.Join(", ", innerLines) + "}";
                            }
                            else
                            {
                                // Подсказки для инфраструктурных портов
                                if (x.Name.Equals("Context", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("In", StringComparison.OrdinalIgnoreCase))
                                    descStr = "INPUT_PORT: Accepts historical '#tag'. If the user points to a specific tag, USE IT EXACTLY.";
                                else if (x.Name.Equals("Out", StringComparison.OrdinalIgnoreCase))
                                    descStr = "OUTPUT_PORT: Creates a NEW unique data tag.";
                            }

                            // Возвращаем ультра-короткий самозакрывающийся тег <p />
                            return $"<p n=\"{x.Name}\" t=\"{typeStr}\"{reqStr} desc=\"{descStr}\"/>";
                        }).ToArray();

                        sb.Append(string.Join("", paramStrings));
                    }

                    sb.Append("</tool>\n");
                }

                sb.Append($"</{group.Key.ToString().ToUpper()}>\n");
            }

            sb.Append("</BIM_TOOLS>");
            return sb.ToString();
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type.IsArray) return "array";
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return "array";

            string name = type.Name.ToLower();
            if (name.Contains("double") || name.Contains("single")) return "double";
            if (name.Contains("int")) return "int";
            if (name.Contains("boolean")) return "boolean";

            return name;
        }

        /// <summary>
        /// Вытаскивает текущее состояние живых тегов из AtomicStorage
        /// </summary>
        public static string GetActiveContentStateTags()
        {
            var activeTags = AtomicStorage.GetCurrentContext();

            if (activeTags == null || activeTags.Length == 0)
                return "CURRENT REVIT MEMORY: [Empty]. You must use 'Out' to save data first.";

            StringBuilder sb = new StringBuilder();
            foreach (var tag in activeTags)
            {
                sb.Append($"- '{tag}'\n");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
