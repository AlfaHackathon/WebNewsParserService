using System.Text.Json.Serialization;

namespace Api.Common;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NewsSource
{
    Rbc,
    Alfa,
    Tass,
}