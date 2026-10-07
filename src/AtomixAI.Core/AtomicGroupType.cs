using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Core
{
    using System.ComponentModel;

    public enum AtomicGroupType
    {
        [AiParam("CRITICAL: Commands for creating new physical elements, geometry, lines, or annotations in the Revit model.")]
        Creation,     // Создание элементов (Action)

        [AiParam("CRITICAL: Commands for modifying, updating, rotating, moving, or changing parameters of existing elements.")]
        Modification, // Изменение параметров/геометрии (Action)

        [AiParam("SAFE: Commands for finding, filtering, selecting, or isolating elements within the active document based on criteria.")]
        Search,       // Фильтрация и поиск (Search)

        [AiParam("Commands for performing calculations, checking regulations, structural or geometric analysis, and quality control.")]
        Analysis,     // Расчеты и проверки (Search/Info)

        [AiParam("Commands for fetching help, documentation, BIM standards, project specifications, or reference data.")]
        Knowledge,    // Справка и документация (Info)

        [AiParam("System infrastructure commands, external bridge processes, script executions, or session management utilities.")]
        System        // Служебные команды (Python/Bridge)
    }

}
