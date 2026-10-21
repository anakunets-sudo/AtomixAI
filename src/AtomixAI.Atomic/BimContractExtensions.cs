using AtomixAI.Core;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Atomic
{
    public static class BimContractExtensions
    {
        public static bool TryParseValue(this Parameter parameter, Document doc, string value, out double result)
        {
            result = 0.0;
            if (parameter?.Definition == null || string.IsNullOrWhiteSpace(value)) return false;

            try
            {
                ForgeTypeId dataType = parameter.Definition.GetDataType();

                if (dataType != null && !string.IsNullOrEmpty(dataType.TypeId)
                    && UnitUtils.IsMeasurableSpec(dataType))
                {
                    return UnitFormatUtils.TryParse(doc.GetUnits(), dataType, value, out result);
                }

                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            }
            catch (Exception ex) when (
                ex is Autodesk.Revit.Exceptions.ArgumentException ||
                ex is InvalidOperationException ||
                ex is FormatException)
            {
                System.Diagnostics.Debug.WriteLine($"[Parse] '{value}' for '{parameter.Definition.Name}': {ex.Message}");
                result = 0.0;
                return false;
            }
        }

        public static bool TryParseValue(this Parameter parameter, Document doc, string value, out int result)
        {
            result = 0;

            if(parameter.TryParseValue(doc, value, out double doubleResult))
            {
                result = Convert.ToInt32(doubleResult);
                return true;
            }

            return false;
        }

        public static double GetLength(this DynamicBimContract contract, string propertyName, double defaultMm = 0.0)
        {
            contract.Get(propertyName, out string rawValue);

            rawValue = rawValue ?? string.Empty;
            return BimUnitConverter.ParseLength(rawValue, defaultMm);
        }

        public static double GetAngle(this DynamicBimContract contract, string propertyName, Document doc, double defaultDegrees = 0.0)
        {
            contract.Get(propertyName, out string rawValue);

            rawValue = rawValue ?? string.Empty;
            return BimUnitConverter.ParseAngle(rawValue, doc, defaultDegrees);
        }
    }
}
