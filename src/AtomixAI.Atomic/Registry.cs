using AtomixAI.Core;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
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

                    if (paramAttr.SchemaType != null)
                    {
                        propertiesSchema[prop.Name] = BuildObjectSchema(
                            paramAttr.SchemaType,
                            "Structured target parameters object.");
                    }
                    else
                    {
                        var propType = prop.PropertyType;
                        string description = paramAttr.Description + extraHint +
                            (propType == typeof(double) ? " (Specify units, e.g. '500mm')" : "");
                        propertiesSchema[prop.Name] = BuildPropertySchema(propType, description);
                    }

                    if (paramAttr.IsRequired || prop.Name.Equals("Out", StringComparison.OrdinalIgnoreCase))
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
                return "number";

            if (underlyingType == typeof(bool))
                return "boolean";

            if (typeof(System.Collections.IDictionary).IsAssignableFrom(underlyingType) ||
                (underlyingType.IsGenericType && underlyingType.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
                return "object";

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(underlyingType) && underlyingType != typeof(string))
                return "array";

            if (underlyingType == typeof(string) || underlyingType == typeof(char) || underlyingType.IsEnum)
                return "string";

            if (underlyingType.IsValueType)
                return "string";

            return "object";
        }

        private static object BuildPropertySchema(Type type, string description)
        {
            Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;
            string jsonType = GetJsonType(type);
            var schema = new Dictionary<string, object>
            {
                { "type", jsonType },
                { "description", description }
            };

            if (effectiveType == typeof(object))
                schema["additionalProperties"] = true;
            else if (jsonType == "array")
            {
                Type elementType = GetElementType(type);
                schema["items"] = BuildTypeSchema(elementType);
            }
            else if (jsonType == "object")
            {
                if (IsDictionary(type))
                {
                    Type valueType = type.IsGenericType ? type.GetGenericArguments()[1] : typeof(object);
                    schema["additionalProperties"] = BuildTypeSchema(valueType);
                }
                else
                {
                    var objectSchema = BuildObjectSchema(type, null) as Dictionary<string, object>;
                    foreach (var property in objectSchema)
                        schema[property.Key] = property.Value;
                }
            }

            return schema;
        }

        private static object BuildTypeSchema(Type type)
        {
            if (type == typeof(object))
                return new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "additionalProperties", true }
                };
            return BuildPropertySchema(type, null);
        }

        private static object BuildObjectSchema(Type type, string description)
        {
            var properties = new Dictionary<string, object>();
            var required = new List<string>();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attribute = property.GetCustomAttribute<AiParamAttribute>();
                if (attribute == null) continue;
                string propertyDescription = attribute.Description;
                if (property.Name.Contains("Point") || property.Name.Contains("Curve"))
                    propertyDescription += " MANDATORY if Context/In is empty. Enter \"\" if Context has geometry.";
                properties[property.Name] = BuildPropertySchema(property.PropertyType, propertyDescription);
                if (attribute.IsRequired)
                    required.Add(property.Name);
            }

            var schema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties },
                { "required", required },
                { "additionalProperties", false }
            };
            if (description != null)
                schema["description"] = description;
            return schema;
        }

        private static Type GetElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType)
            {
                return type.GetGenericArguments().FirstOrDefault() ?? typeof(object);
            }
            return typeof(object);
        }

        private static bool IsDictionary(Type type)
        {
            return typeof(System.Collections.IDictionary).IsAssignableFrom(type) ||
                type.GetInterfaces().Append(type).Any(candidate => candidate.IsGenericType &&
                    (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                     candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
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

            /*
            sb.Append("<MANDATORY_OUTPUT_FORMAT>\n");
            sb.Append("The output MUST strictly match this JSON structure:\n");
            sb.Append("{");
            sb.Append(" \"thought\": \"Brief explanation of your plan\",");
            sb.Append(" \"sequence\": [");
            sb.Append(" { \"name\": \"command_name\", \"arguments\": { \"In\": \"#tag\", \"Params\": { ... }, \"Out\": \"#my_tag\" } }");
            sb.Append(" ]");
            sb.Append(" }\n");
            sb.Append("</MANDATORY_OUTPUT_FORMAT>\n");
            */
            
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

                var groupParam = fieldInfo?.GetCustomAttribute<AiParamAttribute>();
                string groupDesc = groupParam != null ? groupParam.Description : group.Key.ToString();

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

                                    // Экранируем кавычки для XML-атрибута, чтобы промт не ломал парсер гугла
                                    innerLines.Add($"\\\\'{sProp.Name}\\\\': \\\\'{cleanDesc}\\\\'");
                                }

                                descStr = "{" + string.Join(", ", innerLines) + "}";
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
            if (IsDictionary(type)) return "object";
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

        /// <summary>
        /// Упорядоченный список пользовательских тегов (новые сверху) для блока <RECENT_USER_TAGS>.
        /// </summary>
        public static string GetRecentUserTagsState()
        {
            string[] tags = AtomicStorage.GetUserTags();

            if (tags == null || tags.Length == 0)
                return "RECENT USER TAGS: [empty]";

            var sb = new StringBuilder();
            for (int i = 0; i < tags.Length; i++)
            {
                sb.Append($"- '{tags[i]}'{(i == 0 ? "  (MOST RECENT)" : "")}\n");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
