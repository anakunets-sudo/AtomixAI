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
                sb.AppendLine("### 🧩 ATOMIX-AI EXECUTION PROTOCOL:");
                sb.AppendLine("1. **Think First**: Analyze the user request. Is it a QUERY (find/show) or an ACTION (create/modify)?");
                sb.AppendLine("2. **Minimalism**: DO NOT use 'Construction' or 'Modification' tools unless the user explicitly asked to change the model.");
                sb.AppendLine("3. **Batching**: Return a SINGLE JSON object with a 'sequence' array. Searching for elements is a valid standalone sequence.");
                sb.AppendLine("4. **Data Linking**: Use '#tag_name' to pass data. Set 'Out' in step 1, use as 'In' in step 2.");
                sb.AppendLine("5. **Atomic Units**: All lengths MUST include units (e.g., '5000mm', '150mm').");
                sb.AppendLine("6. **Context Awareness**: Use 'get_context_state' to see what elements are already saved in memory tags.");

                sb.AppendLine("\n### 📦 OUTPUT FORMAT (MANDATORY JSON):");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"thought\": \"Brief explanation of your plan\",");
                sb.AppendLine("  \"sequence\": [");
                sb.AppendLine("    { \"name\": \"command_name\", \"arguments\": { \"param1\": \"val\", \"Out\": \"#my_tag\" } }");
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                sb.AppendLine("```");

                sb.AppendLine("\n### 🛠 BIM TOOLBOX (GROUPED BY ACCESS LEVEL):");

                // Собираем все команды и группируем их по AtomicGroupType
                var commandTypes = Assembly.GetAssembly(typeof(Registry)).GetTypes()
                    .Where(t => typeof(IAtomicCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .Select(t => new {
                        Type = t,
                        Info = t.GetCustomAttribute<AtomicInfoAttribute>()
                    })
                    .Where(x => x.Info != null)
                    .GroupBy(x => x.Info.Group);

                foreach (var group in commandTypes)
                {
                    string groupName = group.Key.ToUpper();
                    sb.AppendLine($"\n#### 📂 [{groupName}]");

                    // Динамические инструкции в зависимости от группы
                    if (groupName.Contains("CONSTRUCTION") || groupName.Contains("MODIFICATION") || groupName.Contains("ACTION"))
                    {
                        sb.AppendLine("> ⚠️ CRITICAL: These tools MODIFY the BIM model. Trigger ONLY if the user says 'create', 'place', 'set' or 'delete'.");
                    }
                    else if (groupName.Contains("SEARCH") || groupName.Contains("SELECTION") || groupName.Contains("QUERY"))
                    {
                        sb.AppendLine("> ✅ SAFE: Use these tools for analysis, finding elements, or answering questions.");
                    }

                    foreach (var item in group)
                    {
                        sb.AppendLine($"- Tool: '{item.Info.Name}'");
                        sb.AppendLine($"  Desc: {item.Info.Description}");

                        var props = item.Type.GetProperties()
                            .Select(p => new {
                                p.Name,
                                Attr = Attribute.GetCustomAttribute(p, typeof(AtomicParamAttribute), true) as AtomicParamAttribute
                            })
                            .Where(x => x.Attr != null);

                        if (props.Any())
                        {
                            var paramStrings = props.Select(x => {
                                string pStr = x.Attr.IsRequired ? $"**{x.Name}** (required)" : x.Name;
                                if (x.Name == "In") pStr += " [from #tag]";
                                if (x.Name == "Out") pStr += " [save to #tag]";
                                return pStr;
                            });
                            sb.AppendLine($"  Params: {string.Join(", ", paramStrings)}");
                        }
                    }
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
