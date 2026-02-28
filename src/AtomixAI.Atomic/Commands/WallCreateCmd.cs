using AtomixAI.Core;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AtomixAI.Atomic.Commands
{
    [AtomicInfo(name: "create_wall", group: AtomicGroupType.Creation, description: "Создает стену заданной длины. Длина должна быть с единицами (мм или ft).", keywords: new[] { "wall", "line", "create" })]
    public class WallCreateCmd : BaseAtomicCommand, IAtomicCommand
    {
        [AtomicParam("Длина стены (например, '5000mm' или '10ft')", isRequired: true)]
        public double Length { get; set; }

        protected override AtomicResult Execute(ITransactionHandler handler)
        {
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
#if REVIT2024_OR_GREATER
            Debug.WriteLine($"WallCreateCmd: Wall created with ID {createdWallId.Value}", nameof(WallCreateCmd));
#else
            Debug.WriteLine($"WallCreateCmd: Wall created with ID {createdWallId.IntegerValue}", nameof(WallCreateCmd));
#endif

            if (createdWallId == null || createdWallId == ElementId.InvalidElementId)
            {
                return SetOutput(null, false, "Wall creation failed (Invalid ID).");
            }

            Debug.WriteLine($"[WallCreateCmd] Финальная проверка перед возвратом. ID: {createdWallId}");

            return SetOutput(createdWallId, true, "1 wall created successfully.");
        }
    }
}