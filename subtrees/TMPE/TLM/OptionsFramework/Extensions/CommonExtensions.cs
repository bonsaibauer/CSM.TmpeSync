using System;
using OptionsFramework.Attibutes;

namespace OptionsFramework.Extensions
{
    public static class CommonExtensions
    {
        public static string GetPropertyDescription<T>(this T value, string propertyName)
        {
            var fi = value.GetType().GetProperty(propertyName);
            var attributes = (AbstractOptionsAttribute[])fi.GetCustomAttributes(typeof(AbstractOptionsAttribute), false);
            if (attributes.Length <= 0) {
                throw new Exception($"Property {propertyName} wasn't annotated with AbstractOptionsAttribute");
            }
            return attributes[0].Description; 
        }

        public static string GetPropertyGroup<T>(this T value, string propertyName)
        {
            var fi = value.GetType().GetProperty(propertyName);
            var attributes = (AbstractOptionsAttribute[])fi.GetCustomAttributes(typeof(AbstractOptionsAttribute), false);
            if (attributes.Length <= 0) {
                throw new Exception($"Property {propertyName} wasn't annotated with AbstractOptionsAttribute");
            }
            return attributes[0].Group; 
        }

        public static TR GetAttribute<T, TR>(this T value, string propertyName)where TR : Attribute
        {
            var fi = value.GetType().GetProperty(propertyName);
            var attributes = (TR[])fi.GetCustomAttributes(typeof(TR), false);
            return attributes.Length != 1 ? null : attributes[0];
        }
    }
}