using System.Text.Json.Serialization;
using Supabase;
namespace ChatApp.Models
{
    public class PresenceUser: Supabase.Realtime.Models.BasePresence
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("online_at")]
        public DateTime OnlineAt { get; set; }
    }
}