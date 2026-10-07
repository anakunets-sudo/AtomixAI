using AtomixAI.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Atomic.Commands
{
    [AiInfo(
        name: "filter_spatial",
        group: AtomicGroupType.Search,
        description: "Filters incoming elements by structural boundaries like Levels, Rooms, Worksets, or Design Options.",
        keywords: new[] { "level", "floor", "room", "space", "workset", "design option" })]
    public class FilterSpatialCmd : BaseAtomicCommand
    {
        public class SpatialFilterSchema : DynamicBimContract
        {
            [AiParam("List of target Level ElementIds or Level Names (e.g., ['12345', 'Level 2']). Leave empty if not filtering by level.", isRequired: false)]
            public List<string> Levels { get; set; }

            [AiParam("List of Room Names or Room Numbers to search inside (e.g., ['Kitchen', '101']). Leave empty if not filtering by room.", isRequired: false)]
            public List<string> Rooms { get; set; }

            [AiParam("The exact name of the Workset to restrict elements (e.g., 'Shared Levels and Grids').", isRequired: false)]
            public string WorksetName { get; set; }

            [AiParam("The exact name of the Design Option (e.g., 'Option A').", isRequired: false)]
            public string DesignOptionName { get; set; }
        }

        [AiParam(schema: typeof(SpatialFilterSchema), type: "json", isRequired: true)]
        public override DynamicBimContract Params { get; set; }

        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            UIDocument uidoc = handler.UIDoc;
            Document doc = uidoc.Document;


            if(!GetInput(out DynamicBimContract inputContract).Success)
            {
                return AtomicResult.Error($"Failed to retrieve input tag '{In}' from AtomicStorage.");
            }

            inputContract.Get(BimKeys.Elements.ElementIds, out List<ElementId> inputIds);

            if (inputIds == null || inputIds.Count == 0)
            {
                return AtomicResult.Error($"Spatial filter skipped. Input tag '{In}' is empty.");
            }

            // Выполняем однократное приведение схемы параметров (как у вас в WallCreateCmd)
            var myParams = this.Params as SpatialFilterSchema;
            if (myParams == null)
            {
                return SetOutput(null, 0, false, "Critical Error: Invalid parameter contract type.");
            }

            // 2. Инициализируем свежий коллектор Ревита на базе входных ID
            var collector = new FilteredElementCollector(doc, inputIds);
            var filters = new List<ElementFilter>();

            // === ФИЛЬТР 1: УРОВНИ (ElementLevelFilter + LogicalOrFilter) ===
            if (myParams.Levels != null && myParams.Levels.Count > 0)
            {
                var levelIds = ResolveLevelIds(doc, myParams.Levels);
                if (levelIds.Count > 0)
                {
                    var levelSubFilters = levelIds.Select(id => new ElementLevelFilter(id)).Cast<ElementFilter>().ToList();
                    if (levelSubFilters.Count == 1)
                        filters.Add(levelSubFilters[0]);
                    else
                        filters.Add(new LogicalOrFilter(levelSubFilters));
                }
            }

            // === ФИЛЬТР 2: РАБОЧИЕ НАБОРЫ (WorksetFilter) ===
            if (!string.IsNullOrWhiteSpace(myParams.WorksetName))
            {
                var workset = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .FirstOrDefault(w => w.Name.Equals(myParams.WorksetName, StringComparison.OrdinalIgnoreCase));

                if (workset != null)
                {
                    // Использовать ElementWorksetFilter вместо несуществующего WorksetFilter
                    filters.Add(new ElementWorksetFilter(workset.Id));
                }
            }

            // === ФИЛЬТР 3: ВАРИАНТЫ ПРОЕКТИРОВАНИЯ ===
            if (!string.IsNullOrWhiteSpace(myParams.DesignOptionName))
            {
                var option = new FilteredElementCollector(doc)
                    .OfClass(typeof(DesignOption))
                    .Cast<DesignOption>()
                    .FirstOrDefault(o => o.Name.Equals(myParams.DesignOptionName, StringComparison.OrdinalIgnoreCase));

                if (option != null)
                {
                    // Использовать ElementDesignOptionFilter вместо несуществующего DesignOptionFilter
                    filters.Add(new ElementDesignOptionFilter(option.Id));
                }
            }

            // Применяем собранные БЫСТРЫЕ фильтры к коллектору
            if (filters.Count > 0)
            {
                collector.WherePasses(new LogicalAndFilter(filters));
            }

            // === ФИЛЬТР 4: КОМНАТЫ / ПРОСТРАНСТВА (Геометрический перебор - Медленный, идет в конце) ===
            List<ElementId> finalIds;
            if (myParams.Rooms != null && myParams.Rooms.Count > 0)
            {
                finalIds = FilterByRoomsGeometry(doc, collector, myParams.Rooms);
            }
            else
            {
                finalIds = collector.ToElementIds().ToList();
            }

            // 3. Безопасный пустой возврат через Exclude
            if (finalIds.Count == 0)
            {
                var emptyCollector = new FilteredElementCollector(doc, inputIds);
                return SetOutput(emptyCollector.Excluding(inputIds).ToList(), 0, false, "0 elements matched the spatial constraints.");
            }

            System.Diagnostics.Debug.WriteLine($"[SPATIAL_FILTER] Filtered from {inputIds.Count} down to {finalIds.Count} elements.");

            var contract = new DynamicBimContract();

            contract.Set(BimKeys.Elements.ElementIds, finalIds);

            return SetOutput(contract, finalIds.Count, true, $"Filtered {finalIds.Count} spatial elements.");
        }

        #region Вспомогательные методы разрешения сущностей
        private static List<ElementId> ResolveLevelIds(Document doc, List<string> rawLevels)
        {
            var result = new List<ElementId>();
            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            foreach (var input in rawLevels)
            {
                // Если ИИ прислал чистый ID в строке
                if (int.TryParse(input, out int idInt))
                {
                    result.Add(new ElementId(idInt));
                    continue;
                }

                // Умный поиск уровня по его человеческому имени (Fuzzy Name Resolution)
                var matchedLevel = allLevels.FirstOrDefault(l =>
                    l.Name.Equals(input, StringComparison.OrdinalIgnoreCase) ||
                    l.Name.Replace(" ", "").Contains(input.Replace(" ", "")));

                if (matchedLevel != null)
                {
                    result.Add(matchedLevel.Id);
                }
            }
            return result;
        }

        private static List<ElementId> FilterByRoomsGeometry(Document doc, FilteredElementCollector currentCollector, List<string> roomQueries)
        {
            var result = new List<ElementId>();

            // Находим целевые комнаты по имени или номеру
            var targetRooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .Where(e => e is Autodesk.Revit.DB.Architecture.Room)
                .Cast<Autodesk.Revit.DB.Architecture.Room>()
                .Where(r => roomQueries.Any(q =>
                    r.Name.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                    r.Number.Equals(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (targetRooms.Count == 0) return result;

            // Заставляем Revit разрешить ленивый foreach
            currentCollector.WherePasses(new ElementIsElementTypeFilter(false));

            foreach (Element elem in currentCollector)
            {
                if (elem == null || elem.Location == null) continue;

                XYZ point = null;
                if (elem.Location is LocationPoint lp)
                {
                    point = lp.Point;
                }
                else if (elem.Location is LocationCurve lc)
                {
                    // Для линейных элементов (стены, воздуховоды) берем геометрический центр линии
                    point = lc.Curve.Evaluate(0.5, true);
                }

                if (point == null) continue;

                // Проверяем, находится ли точка элемента внутри какой-либо из целевых комнат
                foreach (var room in targetRooms)
                {
                    if (room.IsPointInRoom(point))
                    {
                        result.Add(elem.Id);
                        break;
                    }
                }
            }
            return result;
        }
        #endregion
    }
}
