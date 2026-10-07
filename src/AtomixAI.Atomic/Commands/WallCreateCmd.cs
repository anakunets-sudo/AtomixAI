using AtomixAI.Core;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace AtomixAI.Atomic.Commands
{
    [AiInfo(name: "create_wall", group: AtomicGroupType.Creation, description: "Create wall by length.", keywords: new[] { "wall", "create" })]
    public class WallCreateCmd : BaseAtomicCommand, IAtomicCommand
    {
        public class WallParamsSchema : DynamicBimContract
        {
            [AiParam("Wall length.", isRequired: true)]
            public string Length { get; set; }

            public double GetLength()
            {
                return BimUnitConverter.ParseLength(Length);
            }
        }

        [AiParam(schema: typeof(WallParamsSchema), type: "json")]
        public override DynamicBimContract Params { get; set; }

        protected override AtomicResult Execute(ITransactionHandler handler)
        {
            var doc = handler.UIDoc.Document;

            ElementId createdWallId = null;

            using (Transaction tr = new Transaction(doc, "AtomixAI: Create Wall"))
            {
                tr.Start();

                Debug.WriteLine($"WallCreateCmd");

                var wallParams = (WallParamsSchema)this.Params;

                Debug.WriteLine($"WallCreateCmd: Length {wallParams.Length}");

                double wallLength = wallParams.GetLength();

                // Длина уже в футах благодаря ToolDispatcher.ParseToRevitFeet
                Line line = Line.CreateBound(XYZ.Zero, new XYZ(wallLength, 0, 0));

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
                return SetOutput(null, 0, false, "Wall creation failed (Invalid ID).");
            }

            Debug.WriteLine($"[WallCreateCmd] Финальная проверка перед возвратом. ID: {createdWallId}");

            var contract = new DynamicBimContract ();

            contract.Set(BimKeys.Elements.ElementIds, new List<ElementId> { createdWallId });

            return SetOutput(contract, 1, true, $"Success. 1 wall created as {this.Out}.");
        }

        private XYZ[] ParseStringToXyzArray(string rawCurveStr)
        {
            if (string.IsNullOrWhiteSpace(rawCurveStr)) return null;

            try
            {
                // 1. Убираем квадратные скобки и пробелы по краям
                string cleanStr = rawCurveStr.Replace("[", "").Replace("]", "").Trim();

                // 2. Делим строку на две отдельные точки по точке с запятой ';'
                string[] rawPoints = cleanStr.Split(';');
                if (rawPoints.Length < 2) return null;

                // Массив для хранения двух итоговых точек Revit
                XYZ[] resultPoints = new XYZ[2];

                for (int i = 0; i < 2; i++)
                {
                    // 3. Расщепляем каждую точку на X, Y, Z по запятой ','
                    string[] coords = rawPoints[i].Split(',');
                    if (coords.Length < 3) return null;

                    // Используем InvariantCulture, чтобы парсинг не спотыкался на точках/запятых в дробях
                    if (!double.TryParse(coords[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double xMm) ||
                        !double.TryParse(coords[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double yMm) ||
                        !double.TryParse(coords[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double zMm))
                    {
                        return null; // Если хоть одна координата не число — вся строка невалидна
                    }

                    // 4. КРИТИЧЕСКИЙ ШАГ: Переводим миллиметры из промта ИИ во внутренние футы Revit API
                    double xFeet = xMm / 304.8;
                    double yFeet = yMm / 304.8;
                    double zFeet = zMm / 304.8;

                    resultPoints[i] = new XYZ(xFeet, yFeet, zFeet);
                }

                return resultPoints;
            }
            catch
            {
                return null; // Защита от любых непредвиденных исключений (например, IndexOutOfRange)
            }
        }
    }
}