using AtomixAI.Core;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AtomixAI.Atomic.Commands
{
    [AtomicInfo(
        name: "create_wall",
        group: AtomicGroupType.Creation,
        description: "Создает стену заданной длины. Длина должна быть с единицами (мм или ft).",
        keywords: new[] { "wall", "line", "create" })]
    public class WallCreateCmd : IAtomicCommand
    {
        public string CommandId => "create_wall";

        [AtomicParam("A unique identifier to save the command's output into global memory for future steps (e.g., 'create_wall_1').")]
        public string ResultAlias { get; set; }

        [AtomicParam("Длина стены (например, '5000mm' или '10ft')", isRequired: true)]
        public double Length { get; set; }

        public AtomicResult Execute(Dictionary<string, object> parameters)
        {
            try
            {
                var handler = TransactionManager.CurrentHandler;
                if (handler == null || handler.UIDoc == null || handler.UIDoc.Document == null)
                    return new AtomicResult { Success = false, Message = "No active Revit document." };

                var doc = handler.UIDoc.Document;
                ElementId createdWallId = null;

                using (Transaction tr = new Transaction(doc, "AtomixAI: Create Wall"))
                {
                    tr.Start();

                    // Длина уже в футах благодаря ToolDispatcher.ParseToRevitFeet
                    Line line = Line.CreateBound(XYZ.Zero, new XYZ(Length, 0, 0));

                    // Поиск инфраструктуры
                    Level level = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElement() as Level;
                    if (level == null)
                        return new AtomicResult { Success = false, Message = "No Level found in document." };

                    ElementId typeId = doc.GetDefaultElementTypeId(ElementTypeGroup.WallType);

                    Wall wall = Wall.Create(doc, line, typeId, level.Id, 10.0, 0, false, false);
                    createdWallId = wall.Id;

                    tr.Commit();
                }

                Debug.WriteLine($"WallCreateCmd: Wall created with ID {createdWallId.IntegerValue}", nameof(WallCreateCmd));

                if (createdWallId == null || createdWallId == ElementId.InvalidElementId)
                {
                    return new AtomicResult { Success = false, Message = "Wall creation failed (Invalid ID)." };
                }

                Debug.WriteLine($"[WallCreateCmd] Финальная проверка перед возвратом. ID: {createdWallId}");

                return new AtomicResult
                {
                    Success = true,
                    Data = createdWallId, // Убедись, что ToolDispatcher ожидает именно объект ElementId
                    Message = $"1 wall created successfully."
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WallCreateCmd failed: {ex}", nameof(WallCreateCmd));
                return new AtomicResult { Success = false, Message = ex.Message };
            }
        }
    }
}