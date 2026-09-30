using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DotnetGraph.Extension.Services;

internal static class GraphWebPayload
{
    public static string WithLayout(string graphJson, string layoutKey)
    {
        var layout = UserGraphSettings.GetLayout(layoutKey);
        if (string.IsNullOrWhiteSpace(layoutKey) && (layout is null || layout.Count == 0))
        {
            return graphJson;
        }

        using var document = JsonDocument.Parse(graphJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            if (!string.IsNullOrWhiteSpace(layoutKey))
            {
                writer.WriteString("layoutKey", layoutKey);
            }

            if (layout is not null && layout.Count > 0)
            {
                writer.WritePropertyName("layoutPositions");
                writer.WriteStartObject();
                foreach (var entry in layout)
                {
                    writer.WritePropertyName(entry.Key);
                    writer.WriteStartObject();
                    writer.WriteNumber("x", entry.Value.X);
                    writer.WriteNumber("y", entry.Value.Y);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
