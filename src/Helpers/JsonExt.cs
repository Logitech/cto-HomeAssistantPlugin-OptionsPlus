namespace Loupedeck.HomeAssistantPlugin
{
    using System.Text.Json;

    // Must be public and at namespace scope for extension methods to work across files
    public static class JsonExt
    {
        public static String GetPropertyOrDefault(this JsonElement el, String name)
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!el.TryGetProperty(name, out var v))
            {
                return null;
            }

            if (v.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
    }
}
