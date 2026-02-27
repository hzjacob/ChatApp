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
        private string? username;
        private string? password;
        InputType PasswordInput = InputType.Password;
        [Inject] NavigationManager Navigation { get; set; } = null!;
        [Inject] IJSRuntime JSRuntime { get; set; } = null!;
        [Inject] IHttpClientFactory ClientFactory { get; set; } = null!;
        
        private void HandleLogin()
        {
            var client = ClientFactory.CreateClient("ChatAppAPI");
            
            JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "username", username);

            Navigation.NavigateTo("/chat");

        }
        private async Task HandleKeyPress(KeyboardEventArgs e)
        {
            if(e.Key == "Enter")
            {
                HandleLogin();
            }
        }
        private void GoToRegister()
        {
            Navigation.NavigateTo("/register");
        }
    }
}