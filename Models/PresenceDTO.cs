namespace ChatApp.Models
{
    using System.Text.Json.Serialization;
    public class PresenceDTO
    {
        [JsonPropertyName("user_id")]
        public long UserId { get; set; }
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("last_seen")]
        public DateTime LastSeen { get; set; }
    }
}