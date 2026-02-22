using System;
using System.Collections.Generic;
using System.Text;

namespace AtomixAI.Core
{
    public interface IAtomicCommand
    {
        // Техническое имя для вызова из MCP (например, "wall_create")
        string CommandId { get; }

        // Метка, которую ИИ передает для сохранения результата (например, "wall_1")
        string ResultAlias { get; set; }

        // Основной метод выполнения
        AtomicResult Execute(Dictionary<string, object> parameters);
    }
}
