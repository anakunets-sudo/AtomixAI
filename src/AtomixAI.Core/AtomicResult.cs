using System;
using System.Collections.Generic;
using System.Text;

namespace AtomixAI.Core
{
    public class AtomicResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; } // Для передачи ID созданных элементов 
    }
}
