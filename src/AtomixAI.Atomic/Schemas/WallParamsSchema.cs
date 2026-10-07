using AtomixAI.Core;
using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls;
/*
namespace AtomixAI.Atomic.Schemas
{
    public class WallParamsSchema : DynamicBimContract
    {
        [AiParam("Wall length.", isRequired: true)]
        public string Length { get; set; }


        /// <summary>
        /// Извлекает строковое значение длины из Params, очищает от единиц (mm),
        /// парсит в double и автоматически переводит миллиметры во внутренние футы Revit API.
        /// </summary>
        /// <returns>Длина во внутренних футах Revit API.</returns>
        public double GetLength(string propertyName, double defaultFt = 0.0)
        {
            string rawValue = this.Get<string>(propertyName);

            try
            {
                Units engUnits = new Units(UnitSystem.Metric);

                // Используем встроенный мощный движок Revit API для парсинга единиц!
                // Он сам поймет суффиксы "mm", "mm", очистит строку и вернет значение сразу во внутренних футах Ревита!
#if REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025 || REVIT2026 || REVIT2027
                // Для современных версий Revit (ForgeTypeId)
                if (UnitFormatUtils.TryParse(engUnits, SpecTypeId.Length, rawValue, out double valueFeet))
                {
                    return valueFeet;
                }
#else
                // Для старых версий Revit (DisplayUnitType)
                if (UnitFormatUtils.TryParse(engUnits, UnitType.UT_Length, rawValue, out double valueFeet))
                {
                    return valueFeet;
                }
#endif
            }
            catch
            {
                // Гасим ошибку, если ИИ прислал нечитаемый мусор
            }
            return defaultFt;
        }


        /// <summary>
        /// Парсит указанное свойство координат (например, StartPoint или EndPoint) 
        /// в объект XYZ Revit API с автоматическим переводом MM -> FEET.
        /// Использует nameof() на стороне вызывающей команды для безопасности.
        /// </summary>
        /// <param name="propertyName">Имя свойства, переданное через nameof().</param>
        /// <returns>Объект XYZ или null, если координата пустая/битая.</returns>
        public XYZ GetPoint(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName)) return null;

            // Извлекаем сырую строку из словаря по имени свойства (безопасно к регистру)
            string rawValue = this.Get<string>(propertyName);
            if (string.IsNullOrWhiteSpace(rawValue)) return null;

            try
            {
                // Очищаем строку от возможных случайных пробелов или скобок ИИ
                string cleanStr = rawValue.Replace("[", "").Replace("]", "").Trim();

                // Расщепляем строку на X, Y, Z по запятой
                string[] coords = cleanStr.Split(',');
                if (coords.Length < 3) return null;

                // Защита от региональных настроек Windows (точки/запятые в дробях)
                if (double.TryParse(coords[0].Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double xMm) &&
                    double.TryParse(coords[1].Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double yMm) &&
                    double.TryParse(coords[2].Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double zMm))
                {
                    // КРИТИЧЕСКИЙ ШАГ: Конвертируем миллиметры ИИ в футы Revit API
                    double xFeet = xMm / 304.8;
                    double yFeet = yMm / 304.8;
                    double zFeet = zMm / 304.8;

                    return new XYZ(xFeet, yFeet, zFeet);
                }
            }
            catch
            {
                // Гасим системные ошибки рантайма, возвращая null для отчета валидатора
            }

            return null;
        }
    }
}
*/