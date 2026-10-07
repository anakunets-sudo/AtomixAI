using AtomixAI.Core;
using Autodesk.Revit.DB;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml.Linq;

namespace AtomixAI.Core
{
    public static class TransactionManager
    {
        public static Func<string, ITransactionHandler>? TransactionFactory { get; set; }

        [ThreadStatic]
        private static ITransactionHandler? _currentHandler;
        public static ITransactionHandler? CurrentHandler => _currentHandler;

        // Номер запроса общий для UI и Revit API. [ThreadStatic] здесь нельзя:
        // Cancel приходит из WebView, а TransactionGroup живёт только в Execute().
        private static int _requestGeneration;
        private static readonly ConcurrentDictionary<int, byte> _cancelledGenerations = new ConcurrentDictionary<int, byte>();

        [ThreadStatic]
        private static int _sequenceGeneration;

        public static int ActiveRequestGeneration => Volatile.Read(ref _requestGeneration);

        public static int BindRequest(int generation)
        {
            if (generation <= 0)
                generation = Interlocked.Increment(ref _requestGeneration);
            else
                Volatile.Write(ref _requestGeneration, generation);
            return generation;
        }

        public static void RequestCancel()
        {
            int generation = Volatile.Read(ref _requestGeneration);
            if (generation > 0)
                _cancelledGenerations.TryAdd(generation, 0);
        }

        public static bool IsGenerationCancelled(int generation)
        {
            return generation > 0 && _cancelledGenerations.ContainsKey(generation);
        }

        /// <summary>
        /// true, если это уже отменённый запрос или call_batch от предыдущего хода.
        /// </summary>
        public static bool IsStaleOrCancelled(int generation)
        {
            if (generation <= 0)
                return false;
            if (IsGenerationCancelled(generation))
                return true;
            int active = ActiveRequestGeneration;
            return active > 0 && generation != active;
        }

        public static bool IsCurrentSequenceCancelled => IsGenerationCancelled(_sequenceGeneration);

        public static AtomicResult ExecuteSequence(string name, Func<AtomicResult> sequenceAction, int generation = 0)
        {
            _sequenceGeneration = generation;
            try
            {
                if (IsGenerationCancelled(generation))
                    return AtomicResult.Cancel();

                // Снимок хранилища меток (#) до начала
                var snapshot = AtomicStorage.GetCurrentContext();

                using (var handler = TransactionFactory?.Invoke(name))
                {
                    if (handler == null) return AtomicResult.Error("Factory not initialized");
                    _currentHandler = handler;

                    try
                    {
                        // 1. ВЫПОЛНЕНИЕ: Получаем один склеенный результат из DispatchSequence
                        var finalResult = sequenceAction();

                        // 2. ОТМЕНА: Rollback только здесь, на потоке Revit API, пока хендлер жив.
                        if (IsGenerationCancelled(generation))
                        {
                            handler.Rollback();
                            RollbackStorage(snapshot);
                            return AtomicResult.Cancel();
                        }

                        // 3. РЕАКЦИЯ: Если хоть один шаг внутри был Success = false
                        if (finalResult == null || !finalResult.Success)
                        {
                            handler.Rollback(); // Откатываем все изменения в Revit
                            RollbackStorage(snapshot); // Очищаем созданные в этой сессии метки

                            // Возвращаем тот самый Error, который пришел из Dispatcher
                            return finalResult ?? AtomicResult.Error("Sequence failed with null result.");
                        }

                        // 4. ФИКСАЦИЯ: Если всё отлично
                        handler.Assimilate();
                        return finalResult;
                    }
                    catch (Exception ex)
                    {
                        handler?.Rollback();
                        RollbackStorage(snapshot);
                        if (IsGenerationCancelled(generation))
                            return AtomicResult.Cancel();
                        return AtomicResult.Error($"Sequence Critical Crash: {ex.Message}");
                    }
                    finally
                    {
                        _currentHandler = null;
                    }
                }
            }
            finally
            {
                _sequenceGeneration = 0;
            }
        }

        /*
        public static AtomicResult ExecuteSafe(string name, Func<AtomicResult> action)
        {
            // Если мы уже внутри ExecuteSequence, просто выполняем действие
            if (_currentHandler != null)
            {
                return action();
            }

            // Если это одиночный вызов — создаем новый хендлер
            using (var handler = TransactionFactory?.Invoke(name))
            {
                _currentHandler = handler;
                try
                {
                    var result = action();
                    if (result != null && result.Success) handler.Assimilate();
                    else handler.Rollback();
                    return result;
                }
                finally { _currentHandler = null; }
            }
        }

        */
        /// <summary>
        /// Удаляет из хранилища все ключи, которые появились в процессе выполнения неудачной команды.
        /// </summary>
        private static void RollbackStorage(string[] snapshot)
        {
            try
            {
                var currentKeys = AtomicStorage.GetCurrentContext();
                // Находим ключи, которых не было в снимке (новые "грязные" данные)
                var dirtyKeys = currentKeys.Except(snapshot).ToList();

                foreach (var key in dirtyKeys)
                {
                    AtomicStorage.Remove(key);
                    Debug.WriteLine($"[TransactionManager] Storage Rollback: Removed dirty key '{key}'");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TransactionManager] Critical error during Storage Rollback: {ex.Message}");
            }
        }

        // Перегрузка для простых Action (также с защитой данных)
        /*
        public static AtomicResult ExecuteSafe(string name, Action action)
        {
            return ExecuteSafe(name, () => {
                action();
                return new AtomicResult { Success = true };
            });
        }*/
    }

    /// <summary>
    /// Команда, ожидающая ExternalEvent. Generation связывает её с ходом чата,
    /// который пользователь мог уже отменить.
    /// </summary>
    public readonly struct PendingToolCall
    {
        public PendingToolCall(string toolId, string jsonArgs, int generation)
        {
            ToolId = toolId ?? string.Empty;
            JsonArgs = jsonArgs ?? "{}";
            Generation = generation;
        }

        public string ToolId { get; }
        public string JsonArgs { get; }
        public int Generation { get; }
    }
}