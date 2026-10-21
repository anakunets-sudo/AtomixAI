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
        /// <summary>
        /// Генерирует строгую схему для нативных Gemini Function Calling.
        /// Поддерживает автоматический разворот классов умных схем (SchemaType).
        /// </summary>
        public static string GetToolsJson()
        {
            var commandTypes = typeof(Registry).Assembly
                .GetTypes()
                .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .Select(t => new { Type = t, Info = t.GetCustomAttribute<AiInfoAttribute>() })
                .Where(x => x.Info != null)
                .GroupBy(x => x.Info.Group);

            var groups = new Dictionary<string, object>();

            foreach (var group in commandTypes)
            {
                var fieldInfo = typeof(AtomicGroupType).GetField(group.Key);
                var groupParam = fieldInfo?.GetCustomAttribute<AiParamAttribute>();
                var commands = new List<object>();

                foreach (var item in group)
                {
                    var propertiesSchema = new Dictionary<string, object>();
                    var requiredParams = new List<string>();

                    foreach (var prop in item.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
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

                    commands.Add(new
                    {
                        name = item.Info.Name,
                        keywords = item.Info.Keywords ?? new string[0],
                        description = item.Info.Description,
                        parameters = new
                        {
                            type = "object",
                            properties = propertiesSchema,
                            required = requiredParams
                        }
                    });
                }

                groups[group.Key.ToUpperInvariant()] = new
                {
                    category_description = groupParam != null ? groupParam.Description : group.Key,
                    tools = commands
                };
            }

            return JsonConvert.SerializeObject(new { bim_tools = groups }, Formatting.Indented);
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
