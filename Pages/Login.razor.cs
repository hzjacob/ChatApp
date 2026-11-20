using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using ChatApp.Models;

namespace ChatApp.Pages
{
    public partial class Login
    {
        private MudForm? _form;
        private bool _isValid;
        private bool _loading;
        private string? email;
        private string? password;
        private bool isShow;
        private string PasswordInputIcon = Icons.Material.Filled.VisibilityOff;
        private InputType PasswordInput = InputType.Password;

        /// <summary>Pre-fills the email, e.g. after registering (/login?email=...).</summary>
        [SupplyParameterFromQuery(Name = "email")] public string? PrefillEmail { get; set; }

        [Inject] NavigationManager Navigation { get; set; } = null!;
        [Inject] IJSRuntime JSRuntime { get; set; } = null!;
        [Inject] IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] ISnackbar Snackbar { get; set; } = default!;

        protected override void OnInitialized()
        {
            if (!string.IsNullOrWhiteSpace(PrefillEmail))
                email = PrefillEmail;
        }

        internal static string? ValidateEmail(string? value) =>
            string.IsNullOrWhiteSpace(value) || new EmailAddressAttribute().IsValid(value.Trim())
                ? null
                : "Please enter a valid email address.";

        private async Task HandleLogin()
        {
            if (_loading) return;

            if (_form != null)
            {
                await _form.ValidateAsync();
                if (!_form.IsValid) return;
            }

            _loading = true;
            try
            {
                var loginUser = new
                {
                    User_email = email?.Trim(),
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
            if (e.Key == "Enter")
                await HandleLogin();
        }

        private void GoToRegister() => Navigation.NavigateTo("/register");

        private void TogglePasswordVisibility()
        {
            isShow = !isShow;
            PasswordInputIcon = isShow ? Icons.Material.Filled.Visibility : Icons.Material.Filled.VisibilityOff;
            PasswordInput = isShow ? InputType.Text : InputType.Password;
        }
    }
}
