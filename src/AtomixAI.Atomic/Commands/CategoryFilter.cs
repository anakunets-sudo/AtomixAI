using AtomixAI.Core;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Atomic.Commands
{
    /// <summary>
    /// Fast filter for Revit BuiltInCategories. 
    /// Usually applied after ClassFilter to narrow down the element collection.
    /// </summary>

    [AtomicInfo(
    name: "category",
    group: AtomicGroupType.Search,
    description: "Search elemetns by Category",
    keywords: new[] { "category", "filter" })]
    public class CategoryFilter : ISearchFilter
    {
        /// <summary>
        /// Priority 2: Runs after ClassFilter (1) but before slow parameter filters (10).
        /// </summary>
        public int Priority => 2;

        [AtomicParam("Revit BuiltInCategory name (e.g. OST_Walls)", isRequired: true)]
        public string CategoryName { get; set; }

        /// <summary>
        /// Applies category filter to the collector.
        /// </summary>
        public FilteredElementCollector Apply(Document doc, FilteredElementCollector collector)
        {
            if (collector == null)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] {this.GetType().Name}: Incoming collector is NULL!");
                return null;
            }

            var bic = ResolveCategory(CategoryName);

            if (bic == BuiltInCategory.INVALID) return collector;

            return collector.OfCategory(bic);
        }

        /// <summary>
        /// Resolves string category name into BuiltInCategory enum with fuzzy matching.
        /// </summary>
        protected BuiltInCategory ResolveCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return BuiltInCategory.INVALID;

            string target = categoryName.Trim().Replace("\"", "").Replace("'", "");

            var variants = new List<string> { target };
            if (!target.StartsWith("OST_")) variants.Add("OST_" + target);
            if (!target.EndsWith("s")) variants.Add(target + "s");

            var _allCategoryNames = Enum.GetNames(typeof(BuiltInCategory));

            foreach (var variant in variants)
            {
                // Ищем в статическом кэше вместо Enum.GetNames
                foreach (var bInCategoryName in _allCategoryNames)
                {
                    if (string.Equals(bInCategoryName, variant, StringComparison.OrdinalIgnoreCase))
                    {
                        return (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), bInCategoryName);
                    }
                }
            }
            return BuiltInCategory.INVALID;
        }
    }
}
