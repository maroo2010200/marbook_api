using System.Text.Json.Serialization;

namespace MarbookApi.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PostVisibility
{
    Public = 0,
    Followers = 1,
    Private = 2
}
