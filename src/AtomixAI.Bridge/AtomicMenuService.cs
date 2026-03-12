using AtomixAI.Core;
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace AtomixAI.Bridge
{
    public class AtomicMenuService
    {
        private readonly RecentSelectionService _recent;

        public AtomicMenuService(RecentSelectionService recent)
        {
            _recent = recent;
        }

        // Уровень 0: Главное меню
        public List<AtomicMenuNode> GetRootMenu(int selectionCount)
        {
            var nodes = new List<AtomicMenuNode>();

            // 1. Функциональные входы
            if (selectionCount > 0)
                nodes.Add(AtomicMenuNode.Create("selection", $"Selection ({selectionCount})", "Actions", true));

            nodes.Add(AtomicMenuNode.Create("views", "Views", "Actions", true));
            nodes.Add(AtomicMenuNode.Create("parameters", "Parameters", "Actions", true));
            nodes.Add(AtomicMenuNode.Create("tags", "Tags", "Actions", true));

            // 2. Блок "Recent" из памяти
            var recent = _recent.GetRecent();
            foreach (var item in recent)
            {
                // Для главного меню меняем группу на Recent, чтобы JS отрисовал их внизу
                nodes.Add(new AtomicMenuNode
                {
                    Id = item.Id,
                    Label = item.Label,
                    Group = "Recent",
                    HasChildren = item.HasChildren
                });
            }

            return nodes;
        }
        public List<AtomicMenuNode> GetSelectionNodes(Document doc, ICollection<ElementId> selectedIds)
        {
            var nodes = new List<AtomicMenuNode>();

            foreach (var id in selectedIds)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;

                // 1. Узел Экземпляра (Имя)
                nodes.Add(new AtomicMenuNode
                {
                    Id = el.UniqueId,
                    Label = el.Name,
                    Group = "Selection",
                    Description = "Instance",
                    HasChildren = true // Вправо -> Параметры экземпляра
                });

                // 2. Узел Типа (Типоразмер)
                Element typeEl = doc.GetElement(el.GetTypeId());
                if (typeEl != null)
                {
                    nodes.Add(new AtomicMenuNode
                    {
                        Id = typeEl.UniqueId,
                        Label = typeEl.Name,
                        Group = "Selection",
                        Description = "Type",
                        HasChildren = true // Вправо -> Параметры типа
                    });
                }
            }
            return nodes;
        }
        public List<AtomicMenuNode> GetTagsFromStorage()
        {
            // Твой метод из Core: получаем все активные ключи (#_last и др.)
            string[] tags = AtomicStorage.GetCurrentContext();

            return tags.Select(tag => new AtomicMenuNode
            {
                Id = tag,
                Label = tag,
                Group = "Tags",
                Description = "Storage Tag",
                HasChildren = true // Вправо -> посмотрим, что внутри тега
            }).ToList();
        }
        public List<AtomicMenuNode> GetParameterNodes(Document doc, string elementUniqueId)
        {
            Element el = doc.GetElement(elementUniqueId);
            if (el == null) return new List<AtomicMenuNode>();

            // Берем параметры и превращаем их в ноды меню
            return el.Parameters.Cast<Parameter>()
                .Where(p => p.HasValue) // Только те, где есть данные
                .Select(p => new AtomicMenuNode
                {
                    Id = p.Id.ToString(), // ID параметра
                    Label = p.Definition.Name,
                    Description = p.AsValueString() ?? p.AsString(), // Текущее значение как подсказка
                    Group = "Parameters",
                    HasChildren = false // Параметр — это тупик, его мы вставляем в чат
                })
                .OrderBy(p => p.Label)
                .ToList();
        }

    }
}