namespace ChatApp.Models
{
    public class PresenceDTO
    {
        public long UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public DateTime LastSeen { get; set; }
    }
}