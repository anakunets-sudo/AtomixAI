using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AtomixAI.Core
{
    public static class Util
    {
        public static double ParseToRevitFeet(object rawValue)
        {
            // Очистка строки и нормализация разделителей
            string input = rawValue?.ToString()?.ToLower()
                ?.Replace(" ", "").Replace(",", ".") ?? "0";

            // Выделяем только числовую часть
            string numericPart = new string(input.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            if (!double.TryParse(numericPart, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double value)) return 0;

            // Маппинг единиц измерения во внутренние футы Revit
            if (input.EndsWith("mm")) return value / 304.8;
            if (input.EndsWith("cm")) return value / 30.48;
            if (input.EndsWith("m")) return value / 0.3048;
            if (input.EndsWith("in")) return value / 12.0;
            if (input.EndsWith("ft")) return value; // Уже футы

            // По умолчанию (если единиц нет) считаем, что прилетели ММ
            return value / 304.8;
        }
    }
}
