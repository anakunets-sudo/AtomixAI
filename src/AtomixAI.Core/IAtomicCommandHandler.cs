using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Core
{
    public interface IAtomicCommandHandler
    {
        // Очередь хранит кортеж: ID инструмента, JSON аргументы, Ключ для ответа
        System.Collections.Generic.Queue<(string, string, string)> CommandQueue { get; }
    }
}
