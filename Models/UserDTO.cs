using System.Text.Json.Serialization;

namespace ChatApp.Pages
{
    public class UserDto
    {
        public string Username {get; set;} = string.Empty;
        public string User_email { get; set;} = string.Empty;
        public string Password {get; set;} = string.Empty;
    }
}