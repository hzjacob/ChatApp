namespace ChatApp.Models
{
    public class MessageDTO
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int? SendTo { get; set; }
        public string? RoomId {get; set;}

    }
}