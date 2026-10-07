using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.UI
{
    // Вспомогательное расширение для безопасного чтения Int/Long значений ElementId кросс-платформенно
    public static class ElementIdExtensions
    {
        public static long GetIdValue(this ElementId id)
        {
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }
    }
}
