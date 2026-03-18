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
        private bool _loading;
        private string? email;
        private string? password;
        bool isShow;
        string PasswordInputIcon = Icons.Material.Filled.VisibilityOff;
        InputType PasswordInput = InputType.Password;
        [Inject] NavigationManager Navigation { get; set; } = null!;
        [Inject] IJSRuntime JSRuntime { get; set; } = null!;
        [Inject] IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] ISnackbar Snackbar {get; set;} = default!;
        
        private async Task HandleLogin()
        {
            if(_loading) return;

            _loading = true;
            try
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
                    if(response.Token != null)
                    {
                        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", response.Token);
                        await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "username", response.Username);
                    }


                    Navigation.NavigateTo("/chat");
                }
                else
                {
                    Navigation.NavigateTo("/Home");
                }

            }
            catch(Exception ex)
            {
                _loading = false;
                Snackbar.Add(ex.Message, Severity.Error);
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
        private void TogglePasswordVisibility()
        {
            if (isShow)
            {
                isShow = false;
                PasswordInputIcon = Icons.Material.Filled.VisibilityOff;
                PasswordInput = InputType.Password;
            }
            else
            {
                isShow = true;
                PasswordInputIcon = Icons.Material.Filled.Visibility;
                PasswordInput = InputType.Text;
            }
        }
    }
}