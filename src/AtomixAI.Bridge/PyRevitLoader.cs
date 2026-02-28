using System;
using System.IO;
using System.Collections.Generic;
using AtomixAI.Core;
using Newtonsoft.Json;

namespace AtomixAI.Bridge
{
    public class PyRevitLoader
    {
        private readonly string _scriptsPath;

        public PyRevitLoader(string scriptsRoot)
        {
            // Путь к папке scripts/ из корня решения 
            _scriptsPath = scriptsRoot;
        }

        public AtomicResult RunScript(string scriptName, Dictionary<string, object> args)
        {
            string fullPath = Path.Combine(_scriptsPath, scriptName);
            if (!File.Exists(fullPath))
                return AtomicResult.Error($"Скрипт {scriptName} не найден.");

            try
            {
                // 1. Проверяем, есть ли активная транзакция (хендлер)
                var handler = TransactionManager.CurrentHandler;
                if (handler == null)
                    return AtomicResult.Error("Python Execution Failed: No active transaction context.");

                // 2. Подготовка аргументов (через переменные окружения или временный файл)
                string jsonArgs = JsonConvert.SerializeObject(args);

                // 3. ВЫПОЛНЕНИЕ (Прямое)
                // Здесь вы вызываете IronPython Engine. 
                // ВАЖНО: Вы передаете handler.UIDoc.Document в область видимости Python, 
                // чтобы скрипт мог работать в ТОЙ ЖЕ транзакции, что и C#.

                // Пример (псевдокод):
                // var scope = _engine.CreateScope();
                // scope.SetVariable("doc", handler.UIDoc.Document);
                // scope.SetVariable("args", args);
                // var result = _engine.ExecuteFile(fullPath, scope);

                return AtomicResult.Ok($"Script {scriptName} executed successfully.");
            }
            catch (Exception ex)
            {
                return AtomicResult.Error($"Python Error in '{scriptName}': {ex.Message}");
            }
        }
    }
}