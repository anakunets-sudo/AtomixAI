using AtomixAI.Core;
using Autodesk.Revit.DB;

[AtomicInfo(
    name: "create_wall",
    group: AtomicGroupType.Creation,
    description: "Создает стену заданной длины. Длина должна быть с единицами (мм или ft).",
    keywords: new[] { "wall", "line", "create" }
)]
public class WallCreateCmd : IAtomicCommand
{
    public string CommandId => "create_wall";

    [AtomicParam("Длина стены (например, '5000mm' или '10ft')", isRequired: true)]
    public double Length { get; set; }

    public AtomicResult Execute(Dictionary<string, object> parameters)
    {
        // 1. Берем уже открытый контекст и готовые Футы
        var doc = TransactionManager.CurrentHandler.UIDoc.Document;

        using (Transaction tr = new Transaction(doc, "AtomixAI: Create Wall"))
        {
            tr.Start();

            // Длина уже в футах благодаря ToolDispatcher.ParseToRevitFeet
            Line line = Line.CreateBound(XYZ.Zero, new XYZ(Length, 0, 0));

            // Поиск инфраструктуры...
            Level level = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElement() as Level;
            ElementId typeId = doc.GetDefaultElementTypeId(ElementTypeGroup.WallType);

            Wall.Create(doc, line, typeId, level.Id, 10.0, 0, false, false);

            tr.Commit();
        }
        return new AtomicResult { Success = true, Message = $"Wall created with length {Length} ft" };
    }
}
