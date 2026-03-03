using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;

namespace ChatApp.Pages
{
    public partial class Login
    {
        private string? email;
        private string? password;
        InputType PasswordInput = InputType.Password;
        [Inject] NavigationManager Navigation { get; set; } = null!;
        [Inject] IJSRuntime JSRuntime { get; set; } = null!;
        [Inject] IHttpClientFactory ClientFactory { get; set; } = null!;
        
        private async Task HandleLogin()
        {
            var loginUser = new 
            {
                User_email = email,
                Password = password
            };
            var client = ClientFactory.CreateClient("ChatAppAPI");

            var request = await client.PostAsJsonAsync("api/users/login", loginUser);

            if (request.IsSuccessStatusCode)
            {
                var response = await request.Content.ReadFromJsonAsync<UserDto>();
                string username = response.Username;

                await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "username", username);
                Navigation.NavigateTo("/chat");
            }
            else
            {
                Navigation.NavigateTo("/Home");
            }

            

        }
        private async Task HandleKeyPress(KeyboardEventArgs e)
        {
            if(e.Key == "Enter")
            {
                await HandleLogin();
            }
        }
        private void GoToRegister()
        {
            Navigation.NavigateTo("/register");
        }
    }
}