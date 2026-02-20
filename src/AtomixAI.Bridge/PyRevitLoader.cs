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
            // Ищем скрипт (например, "wall_cleanup.py") 
            string fullPath = Path.Combine(_scriptsPath, scriptName);

            if (!File.Exists(fullPath))
                return new AtomicResult { Success = false, Message = $"Скрипт {scriptName} не найден." };

            try
            {
                // Для выполнения через pyRevit мы обычно используем CLI или 
                // передаем аргументы через временный JSON-файл/Environment 
                string jsonArgs = JsonConvert.SerializeObject(args);

                // ВАЖНО: Выполняем скрипт внутри нашей транзакции из Core 
                return TransactionManager.ExecuteSafe($"PyScript: {scriptName}", () =>
                {
                    // Здесь логика вызова IronPython Engine или запуск через pyrevit CLI 
                    // Пример заглушки: 
                    Console.WriteLine($"Запуск {scriptName} с параметрами: {jsonArgs}");

                    // Реальное выполнение: 
                    // var engine = Python.CreateEngine(); ... 
                });
            }
            catch (Exception ex)
            {
                return new AtomicResult { Success = false, Message = $"Ошибка Python: {ex.Message}" };
            }
        }
    }
}