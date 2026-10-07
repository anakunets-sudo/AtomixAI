using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Atomic
{
    /// <summary>
    /// Single source of truth for all pipeline, contract, and parameter keys.
    /// These strings MUST perfectly match the JSON keys used by the AI.
    /// </summary>
    public static class BimKeys
    {
        // Ключи для портов и системных контрактов обмена элементами
        public static class Elements
        {
            public const string ElementIds = "ElementIds";
        }

        // Ключи для системных параметров команд поиска и фильтрации
        public static class Search
        {
            public const string Scope = "Scope";
            public const string Categories = "Categories";
            public const string ClassName = "ClassName";
            public const string FilterType = "FilterType";
            public const string ParameterName = "ParameterName";
            public const string Operator = "Operator";
            public const string Value = "Value";
            public const string StorageType = "StorageType";
            public const string Collector = "Collector";
        }

        // Ключи для пространственных фильтров среднего уровня
        public static class Spatial
        {
            public const string Levels = "Levels";
            public const string Rooms = "Rooms";
            public const string WorksetName = "WorksetName";
            public const string DesignOptionName = "DesignOptionName";
        }

        // Ключи для команд создания элементов (например, стен)
        public static class Creation
        {
            public const string Length = "Length";
            public const string Height = "Height";
            public const string Location = "Location";
        }
    }
}
