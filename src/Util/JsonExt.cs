namespace Loupedeck.HomeAssistantPlugin
{
    using System.Text.Json;

    // Must be public and at namespace scope for extension methods to work across files
    public static class JsonExt
    {
        public static String? GetPropertyOrDefault(this JsonElement el, String name)
        {
            // Return null if the element is not an object
            if (el.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Return null if the property doesn't exist
            if (!el.TryGetProperty(name, out var property))
            {
                return null;
            }

            // Handle different JSON value types
            return property.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => property.GetString(),
                _ => null // Return null for non-string, non-null values (numbers, booleans, objects, arrays)
            };
        }
    }
}