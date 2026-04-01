using Newtonsoft.Json;
using Supabase;
namespace ChatApp.Models
{
    public class PresenceUser: Supabase.Realtime.Models.BasePresence
    {
        [JsonProperty("username")]
        public string Username { get; set; } = string.Empty;
        [JsonProperty("online_at")]
        public DateTime OnlineAt { get; set; }
        [JsonProperty("is_typing")]
        public bool IsTyping { get; set; }
        [JsonProperty("session_id")]
        public string? SessionId { get; set; }
    }
}