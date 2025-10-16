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
                PluginLog.Verbose($"[JsonExt] GetPropertyOrDefault - Element is not an object (ValueKind: {el.ValueKind}) for property '{name}'");
                return null;
            }

            // Return null if the property doesn't exist
            if (!el.TryGetProperty(name, out var property))
            {
                PluginLog.Verbose($"[JsonExt] GetPropertyOrDefault - Property '{name}' not found in JSON object");
                return null;
            }

            // Handle different JSON value types
            var result = property.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => property.GetString(),
                _ => null // Return null for non-string, non-null values (numbers, booleans, objects, arrays)
            };

            if (result == null && property.ValueKind != JsonValueKind.Null)
            {
                PluginLog.Verbose($"[JsonExt] GetPropertyOrDefault - Property '{name}' exists but is not a string (ValueKind: {property.ValueKind})");
            }

            return result;
        }
    }
}