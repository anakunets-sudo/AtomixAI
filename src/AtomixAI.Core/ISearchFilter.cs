using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Core
{
    public interface ISearchFilter
    {
        // Приоритет для сортировки "паровоза" 
        int Priority { get; }

        // Применяет фильтрацию к входящему коллектору 
        FilteredElementCollector Apply(Document doc, FilteredElementCollector collector);
    }
}
