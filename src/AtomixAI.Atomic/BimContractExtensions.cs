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
        public static double ParseDouble(this Parameter parameter, string value, double defaultMm = 0.0)
        {
            if (string.IsNullOrWhiteSpace(value)) return BimUnitConverter.ConvertMmToInternal(defaultMm);

            try
            {
#if REVIT2021_OR_GREATER
                if (UnitFormatUtils.TryParse(new Units(UnitSystem.Metric), parameter.Definition.GetDataType(), value, out double valueFeet))
                {
                    return valueFeet;
                }
#else
                if (UnitFormatUtils.TryParse(new Units(UnitSystem.Metric), parameter.Definition.UnitType, value, out double valueFeet))
                {
                    return valueFeet;
                }
#endif
            }
            catch { /* Ловим мусор от ИИ */ }

            return BimUnitConverter.ConvertMmToInternal(defaultMm);
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
