using Autodesk.Revit.DB;

namespace AtomixAI.UI.Commands.Selection
{
    public static class SelectionMenuIds
    {
        public const string Root = "selection";
        public const string PickInView = "action_pick_objects";
        public const string ElementPrefix = "el_";
        public const string InstancePrefix = "inst_";
        public const string TypePrefix = "type_";

        public static string Element(ElementId id) => ElementPrefix + id.GetIdValue();
        public static string Instance(string rawId) => InstancePrefix + rawId;
        public static string Type(string rawId) => TypePrefix + rawId;

        public static bool HasPrefix(string itemId, string prefix)
        {
            return itemId != null && itemId.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);
        }

        public static string RawId(string itemId, string prefix) => itemId.Substring(prefix.Length);

        public static ElementId ParseElementId(string rawId)
        {
            if (!long.TryParse(rawId, out long value)) return ElementId.InvalidElementId;
#if REVIT2024_OR_GREATER
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }
    }
}
