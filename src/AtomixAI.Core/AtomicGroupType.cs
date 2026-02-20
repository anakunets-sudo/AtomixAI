using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Core
{
    public enum AtomicGroupType
    {
        Creation,     // Создание элементов (Action)
        Modification, // Изменение параметров/геометрии (Action)
        Search,       // Фильтрация и поиск (Search)
        Analysis,     // Расчеты и проверки (Search/Info)
        Knowledge,    // Справка и документация (Info)
        System        // Служебные команды (Python/Bridge)
    }
}
