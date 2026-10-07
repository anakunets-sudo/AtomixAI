using AtomixAI.Core;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
/*
namespace AtomixAI.Atomic.Commands
{
    [AtomicInfo(name: "inspect_tag", group: AtomicGroupType.System, description: "Retrieves properties of elements from input tag (e.g., #wall_1).", keywords: new[] { "#" })]
    public class InspectTagCmd : BaseAtomicCommand, IAtomicCommand
    {
        // ИСПРАВЛЕНО: Тип данных изменен с double на string, чтобы принимать хэштеги текстом
        [AtomicParam("Target tag name for inspection (e.g., '#wall_1').", isRequired: true)]
        public string TagName { get; set; }

        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            var doc = handler.UIDoc.Document;
            string activeTag = TagName ?? this.In;

            // 1. Извлекаем данные из AtomicStorage
            var inputResult = GetInput<object>(out var storedValue, activeTag);
            if (!inputResult.Success)
            {
                return inputResult; // Базовый класс сам вернет ИИ ошибку "Tag empty"
            }

            var elements = new List<Element>();
            bool isElementData = false;

            // 2. Попытка распаковки как элементов Revit (Element или ElementId)
            try
            {
                if (storedValue is System.Collections.IEnumerable enumerable && !(storedValue is string) && !(storedValue is Newtonsoft.Json.Linq.JContainer))
                {
                    foreach (var item in enumerable)
                    {
                        var el = ResolveToElement(doc, item);
                        if (el != null) { elements.Add(el); isElementData = true; }
                    }
                }
                else
                {
                    var el = ResolveToElement(doc, storedValue);
                    if (el != null) { elements.Add(el); isElementData = true; }
                }
            }
            catch
            {
                // Если упали при разрешении элементов САПР — значит, там точно лежит не Revit-объект, а примитив или анонимный класс
                isElementData = false;
            }

            // -----------------------------------------------------------------
            // СЦЕНАРИЙ А: Под тегом скрывается АНОНИМНЫЙ КЛАСС, Словарь или Примитив
            // -----------------------------------------------------------------
            if (!isElementData || elements.Count == 0)
            {
                object unpackedData;

                try
                {
                    // Если это уже JToken (JObject/JArray) от Newtonsoft — сериализуем его напрямую в словарь/объект
                    if (storedValue is Newtonsoft.Json.Linq.JToken token)
                    {
                        unpackedData = token.ToObject<object>();
                    }
                    else
                    {
                        // Если это "живой" анонимный класс из C# — принудительно перегоняем его через JSON-строку 
                        // в анонимный динамический объект, чтобы ИИ увидел все его внутренние свойства!
                        string jsonString = JsonConvert.SerializeObject(storedValue);
                        unpackedData = JsonConvert.DeserializeObject<object>(jsonString);
                    }
                }
                catch (Exception ex)
                {
                    // Если объект совсем не сериализуем — возвращаем как строку
                    unpackedData = storedValue?.ToString() ?? "null";
                    Debug.WriteLine($"[InspectTagCmd] Non-serializable raw object: {ex.Message}");
                }

                var anonymousReport = new Dictionary<string, object>
                {
                    { "target_tag", activeTag },
                    { "data_type", storedValue?.GetType().Name ?? "null" },
                    { "unpacked_content", unpackedData }
                };

                return SetOutput(
                    storageValue: storedValue,
                    dataOverride: anonymousReport,
                    success: true,
                    message: $"Successfully unpacked and inspected raw structured data inside {activeTag}."
                );
            }

            // -----------------------------------------------------------------
            // СЦЕНАРИЙ Б: Под тегом скрываются ЖИВЫЕ ЭЛЕМЕНТЫ REVIT (Стены, Окна...)
            // -----------------------------------------------------------------
            var inspectDetails = new List<Dictionary<string, object>>();

            foreach (var el in elements)
            {
                var elementParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // ДИНАМИЧЕСКИЙ СКАНИРОВАНИЕ ВСЕХ ПАРАМЕТРОВ ЭЛЕМЕНТА
                foreach (Parameter p in el.Parameters)
                {
                    // Берем только параметры, у которых есть реальное текстовое значение (ValueString или StringValue)
                    if (p.HasValue)
                    {
                        string valStr = p.AsValueString() ?? p.AsString() ?? p.AsDouble().ToString();
                        if (!string.IsNullOrWhiteSpace(valStr))
                        {
                            elementParams[p.Definition.Name] = valStr;
                        }
                    }
                }

                // Собираем базовый паспорт элемента + весь динамический мешок его параметров!
                var itemData = new Dictionary<string, object>
                    {
                        { "id", el.Id.ToString() },
                        { "name", el.Name },
                        { "category", el.Category?.Name ?? "Unknown" },
                        { "all_parameters", elementParams } // Здесь будут ВСЕ реальные свойства: от длины до объема
                    };

                inspectDetails.Add(itemData);
            }

            var aiReportData = new Dictionary<string, object>
                    {
                        { "target_tag", activeTag },
                        { "total_elements", elements.Count },
                        { "inspected_properties", inspectDetails }
                    };

            return SetOutput(
                storageValue: storedValue,
                dataOverride: aiReportData,
                success: true,
                message: $"Dynamically inspected {elements.Count} Revit element(s) inside {activeTag}."
            );
        }

        /// <summary>
        /// Вспомогательный метод разрешения объектов хранилища (Element или ElementId) в живой элемент Revit
        /// </summary>
        private Element ResolveToElement(Document doc, object item)
        {
            if (item is Element el) return el;
            if (item is ElementId id && id != ElementId.InvalidElementId) return doc.GetElement(id);
            return null;
        }
    }
}*/