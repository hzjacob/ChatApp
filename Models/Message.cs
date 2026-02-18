using Postgrest.Attributes;
using Postgrest.Models;
namespace ChatApp.Models
{
    [Table("messages")]
    public class Message : BaseModel
    {
        [PrimaryKey("id", false)]
        public int Id { get; set; }

        [Column("username")]
        public string Username { get; set; } = string.Empty;

        [Column("content")]
        public string Content { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}