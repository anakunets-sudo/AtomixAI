using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Core
{
    /// <summary>
    /// Простая структура данных для передачи инструкций фильтрации от ИИ к Revit.
    /// Заменяет собой JArray для корректной генерации JSON-схемы инструментов.
    /// </summary>
    public class FilterInstruction
    {
        public string Kind { get; set; }
        public string Value { get; set; }
    }
}
