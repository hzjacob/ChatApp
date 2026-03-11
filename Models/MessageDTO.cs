namespace ChatApp.Models
{
    public class MessageDTO
    {
        public long? Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long? SendTo { get; set; }
        public string? RoomId {get; set;}

    }
}