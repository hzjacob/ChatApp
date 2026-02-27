using System.Net.Http.Json;
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
        [Inject] private IHttpClientFactory ClientFactory {get; set;} = null!;

        private async Task HandleRegister()
        {
            try
            {
                var client = ClientFactory.CreateClient("ChatAppAPI");

                var newUser = new 
                {
                    username = username,
                    password = password,
                    user_email = email
                };

                var result = await client.PostAsJsonAsync("/api/users", newUser);

                if (result.IsSuccessStatusCode)
                {
                    Navigation.NavigateTo("/login");
                }

            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            
            
        }
        private void GoToLogin()
        {
            Navigation.NavigateTo("/login");
        }
    
    }

}