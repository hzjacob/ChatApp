using Microsoft.AspNetCore.Components;
namespace ChatApp.Pages
{
    public partial class Register
    {
        private string? username;
        private string? email;
        private string? password;
        private string? confirmPassword;
        [Inject] NavigationManager Navigation {get; set;} =null!;

        private async Task HandleRegister()
        {
            
        }
        private void GoToLogin()
        {
            Navigation.NavigateTo("/login");
        }
    
    }

}