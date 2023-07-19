using System;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace Inedo.Docker;

internal sealed class DockerDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // example: 2023-05-23 14:25:05 -0400 EDT

        var s = reader.GetString()!;
        int spaceIndex = s.IndexOf(' ', s.IndexOf(' ') + 1);

        var dateTime = DateTime.ParseExact(s.AsSpan(0, spaceIndex), "yyyy-MM-dd HH:mm:ss", null);

        int nextSpaceIndex = s.IndexOf(' ', spaceIndex + 1);

        var offset = s.AsSpan()[(spaceIndex + 1)..nextSpaceIndex];

        bool negative = false;
        if (offset[0] == '-')
        {
            negative = true;
            offset = offset[1..];
        }
        else if (offset[0] == '+')
        {
            offset = offset[1..];
        }

        int hours = int.Parse(offset[0..2]);
        int minutes = int.Parse(offset[2..]);

        var timeSpan = new TimeSpan(hours, minutes, 0);
        if (negative)
            timeSpan = -timeSpan;

        return new DateTimeOffset(dateTime, timeSpan);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => throw new NotImplementedException();
}