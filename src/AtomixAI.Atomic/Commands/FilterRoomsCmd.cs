using AtomixAI.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace AtomixAI.Atomic.Commands
{
    [AiInfo(
        name: "filter_rooms",
        group: AtomicGroupType.Search,
        description: "Filters incoming items by rooms, spaces or areas.",
        keywords: new[] { "room", "space"})]
    public class FilterRoomsCmd : BaseAtomicCommand
    {
        public class RoomsFilterSchema : DynamicBimContract
        {
            [AiParam("List of room parameter identifiers 'Id', for example: ['488178', '488179'].", isRequired: false)]
            public List<string> RoomIds { get; set; }

            [AiParam("List of Room Names to search inside (e.g., ['Kitchen', 'Living Room'])", isRequired: false)]
            public List<string> RoomNames { get; set; }

            [AiParam("List of Room Numbers to search inside (e.g., ['101', '202'])", isRequired: false)]
            public List<string> RoomNumbers { get; set; }
        }

        [AiParam(schema: typeof(RoomsFilterSchema), type: "json", isRequired: true)]
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
                return AtomicResult.Error($"Room filter skipped. Input tag '{In}' is empty.");
            }

            // Выполняем однократное приведение схемы параметров (как у вас в WallCreateCmd)
            var myParams = this.Params as RoomsFilterSchema;
            if (myParams == null)
            {
                return SetOutput(null, 0, false, "Critical Error: Invalid parameter contract type.");
            }

            System.Diagnostics.Debug.WriteLine($"[FilterRoomsCmd] Params.RoomIds : {(myParams.RoomIds == null ? "null/empty" : myParams.RoomIds.ToString())}");
            System.Diagnostics.Debug.WriteLine($"[FilterRoomsCmd] Params.RoomNames: {((myParams.RoomNames == null) ? "null/empty" : myParams.RoomNames.ToString())}");
            System.Diagnostics.Debug.WriteLine($"[FilterRoomsCmd] Params.RoomNumbers: {((myParams.RoomNumbers == null) ? "null/empty" : myParams.RoomNumbers.ToString())}");

            // 2. Инициализируем свежий коллектор Ревита на базе входных ID
            var collector = new FilteredElementCollector(doc, inputIds);

            // === ФИЛЬТР 4: КОМНАТЫ / ПРОСТРАНСТВА (Геометрический перебор - Медленный, идет в конце) ===
            List<ElementId> finalIds;

            if (GetRoom(doc, myParams, out var rooms))
            {
                finalIds = FilterByRoomsGeometry(doc, collector, rooms);
            }
            else
            {
                return SetOutput(null, 0, false, "Critical Error: Room not found.");
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

        private static bool GetRoom (Document doc, RoomsFilterSchema myParams, out List<Room>? rooms)
        {
            rooms = null;

            var allRooms = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.SpatialElement)).OfCategory(BuiltInCategory.OST_Rooms).ToElements();

            System.Diagnostics.Debug.WriteLine($"[FilterRoomsCmd] [GetRoom] allRooms Count {allRooms.Count}");

            if (myParams.RoomIds != null && myParams.RoomIds.Count > 0)
            {
                rooms = new List<Room>();
                foreach (var roomId in myParams.RoomIds)
                {
                    if (long.TryParse(roomId, out long id))
                    {
                        IEnumerable<Element>? els = null;

#if REVIT2024_OR_GREATER
                        els = allRooms.Where(e => e.Id.Value == id);
#else
                    els= allRooms.FirstOrDefault(e => e.Id.IntegerValue == id);
#endif
                        if (els != null && els.Count() > 0)
                        {
                            rooms.AddRange(els.OfType<Room>());
                        }
                    }
                }

                if (rooms.Count > 0)
                {
                    return true;
                }
            }

            if (myParams.RoomNames != null && myParams.RoomNames.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[FilterRoomsCmd] [GetRoom] myParams.RoomNames Count {myParams.RoomNames.Count}");

                IEnumerable<Element> els = allRooms.Where(e => myParams.RoomNames.Any(n => e.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));

                if (els == null || els.Count() == 0)
                {
                    els = allRooms.Where(e => myParams.RoomNames.Any(n => e.get_Parameter(BuiltInParameter.ROOM_NAME).AsString().Equals(n, StringComparison.OrdinalIgnoreCase)));
                }

                if (els != null && els.Count() > 0)
                {
                    rooms = new List<Room>();
                    rooms.AddRange(els.OfType<Room>());
                    return true;
                }
            }

            if (myParams.RoomNumbers != null && myParams.RoomNumbers.Count > 0)
            {
                    IEnumerable<Element> els = els = allRooms.Where(e => myParams.RoomNumbers.Any(n => e.get_Parameter(BuiltInParameter.ROOM_NUMBER).AsString().Equals(n, StringComparison.OrdinalIgnoreCase)));                

                if (els != null && els.Count() > 0)
                {
                    rooms = new List<Room>();
                    rooms.AddRange(els.OfType< Room>());
                    return true;
                }
            }

            return false;
        }

        private static List<ElementId> FilterByRoomsGeometry(Document doc, FilteredElementCollector currentCollector, List<Room> rooms)
        {
            System.Diagnostics.Debug.WriteLine($"[FilterRoomsCmd] [FilterByRoomsGeometry] rooms Count {rooms.Count}");

            var result = new List<ElementId>();

            System.Diagnostics.Debug.WriteLine($"[FilterRoomsCmd] [FilterByRoomsGeometry] currentCollector Count {currentCollector.GetElementCount()}");

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

                foreach (Room room in rooms)
                {
                    // Проверяем, находится ли точка элемента внутри какой-либо из целевых комнат
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
