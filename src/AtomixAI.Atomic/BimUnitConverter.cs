using Autodesk.Revit.DB;
using System.Globalization;

namespace AtomixAI.Atomic
{
    public static class BimUnitConverter
    {
        private static readonly Units MetricUnits = new Units(UnitSystem.Metric);        

        public static double ParseLength(string value, double defaultMm = 0.0)
        {
            if (string.IsNullOrWhiteSpace(value)) return ConvertMmToInternal(defaultMm);

            try
            {
#if REVIT2021_OR_GREATER
                if (UnitFormatUtils.TryParse(MetricUnits, SpecTypeId.Length, value, out double valueFeet))
                {
                    return valueFeet;
                }
#else
            if (UnitFormatUtils.TryParse(MetricUnits, UnitType.UT_Length, value, out double valueFeet))
            {
                return valueFeet;
            }
#endif
            }
            catch { /* Ловим мусор от ИИ */ }

            return ConvertMmToInternal(defaultMm);
        }

        public static double ParseAngle(string value, Document doc, double defaultDegrees = 0.0)
        {
            if (string.IsNullOrWhiteSpace(value)) return ConvertDegreesToInternal(defaultDegrees);

            CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
            CultureInfo originalUiCulture = Thread.CurrentThread.CurrentUICulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

                string cleanedValue = value.Replace(',', '.');
                Units docUnits = doc.GetUnits();

#if REVIT2021_OR_GREATER
                if (UnitFormatUtils.TryParse(docUnits, SpecTypeId.Angle, cleanedValue, out double valueRadians))
                {
                    return valueRadians;
                }
#else
            if (UnitFormatUtils.TryParse(docUnits, UnitType.UT_Angle, cleanedValue, out double valueRadians))
            {
                return valueRadians;
            }
#endif
            }
            catch { /* Ловим мусор от ИИ */ }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }

            return ConvertDegreesToInternal(defaultDegrees);
        }

        public static double ConvertMmToInternal(double mm)
        {
#if REVIT2021_OR_GREATER
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
#else
        return UnitUtils.Convert(mm, DisplayUnitType.DUT_MILLIMETERS, DisplayUnitType.DUT_DECIMAL_FEET);
#endif
        }

        public static double ConvertDegreesToInternal(double degrees)
        {
#if REVIT2021_OR_GREATER
            return UnitUtils.ConvertToInternalUnits(degrees, UnitTypeId.Degrees);
#else
        return UnitUtils.Convert(degrees, DisplayUnitType.DUT_DECIMAL_DEGREES, DisplayUnitType.DUT_RADIANS);
#endif
        }
    }
}
