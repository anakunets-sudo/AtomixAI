using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Core
{
    public interface IRevitContext
    {
        Autodesk.Revit.UI.UIDocument UIDoc { get; }
        Autodesk.Revit.UI.UIApplication UIApp { get; }
    }
}