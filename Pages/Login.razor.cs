using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace ChatApp.Pages
{
    public partial class Login
    {
        private string? username;
        [Inject] NavigationManager Navigation { get; set; } = null!;
        [Inject] IJSRuntime JSRuntime { get; set; } = null!;
        
        private void HandleLogin()
        {
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
    }
}