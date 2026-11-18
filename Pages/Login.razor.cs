using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using ChatApp.Models;

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

                var result = await client.PostAsJsonAsync("api/users/login", loginUser);

                if (result.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Snackbar.Add("Invalid email or password.", Severity.Error);
                    return;
                }
                if (!result.IsSuccessStatusCode)
                {
                    Snackbar.Add("Login failed. Please try again later.", Severity.Error);
                    return;
                }

                var user = await result.Content.ReadFromJsonAsync<UserDto>();
                if (user == null || string.IsNullOrEmpty(user.Token))
                {
                    Snackbar.Add("Login failed: no token received.", Severity.Error);
                    return;
                }

                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", user.Token);
                await JSRuntime.InvokeVoidAsync("localStorage.setItem", "refreshToken", user.RefreshToken);
                await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "username", user.Username);
                await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "userId", user.Id.ToString());

                Navigation.NavigateTo("/chat");
            }
            catch (HttpRequestException)
            {
                Snackbar.Add("Can't reach the server. Check your connection.", Severity.Error);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Snackbar.Add("Something went wrong. Please try again.", Severity.Error);
            }
            finally
            {
                _loading = false;
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