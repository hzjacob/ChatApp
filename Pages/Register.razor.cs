using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace ChatApp.Pages
{
    public partial class Register
    {
        private const int UsernameMinLength = 3;
        private const int UsernameMaxLength = 20;
        private const int PasswordMinLength = 6;

        private MudForm? _form;
        private bool _isValid;
        private bool _loading;
        private string? username;
        private string? email;
        private string? password;
        private string? confirmPassword;
        private bool isShow;
        private string PasswordInputIcon = Icons.Material.Filled.VisibilityOff;
        private InputType PasswordInput = InputType.Password;

        [Inject] NavigationManager Navigation { get; set; } = null!;
        [Inject] IHttpClientFactory ClientFactory { get; set; } = null!;
        [Inject] ISnackbar Snackbar { get; set; } = default!;

        // ---------- Validation ----------

        private static string? ValidateUsername(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null; // handled by Required
            var v = value.Trim();
            if (v.Length < UsernameMinLength) return $"Username must be at least {UsernameMinLength} characters.";
            if (v.Length > UsernameMaxLength) return $"Username can be at most {UsernameMaxLength} characters.";
            if (!v.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-'))
                return "Use only letters, numbers, '_', '.' or '-'.";
            return null;
        }

        private static string? ValidateEmail(string? value) => Login.ValidateEmail(value);

        private static string? ValidatePassword(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null; // handled by Required
            if (value.Length < PasswordMinLength) return $"Password must be at least {PasswordMinLength} characters.";
            if (!value.Any(char.IsLetter) || !value.Any(char.IsDigit))
                return "Password must contain at least one letter and one number.";
            return null;
        }

        private string? ValidateConfirmPassword(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null; // handled by Required
            return value == password ? null : "Passwords do not match.";
        }

        // ---------- Password strength meter ----------

        private int StrengthScore
        {
            get
            {
                if (string.IsNullOrEmpty(password)) return 0;
                int score = 0;
                if (password.Length >= PasswordMinLength) score++;
                if (password.Length >= 10) score++;
                if (password.Any(char.IsUpper) && password.Any(char.IsLower)) score++;
                if (password.Any(char.IsDigit)) score++;
                if (password.Any(c => !char.IsLetterOrDigit(c))) score++;
                return score;
            }
        }

        private double StrengthValue => StrengthScore * 20;

        private string StrengthLabel => StrengthScore switch
        {
            <= 1 => "Weak",
            2 or 3 => "Fair",
            4 => "Good",
            _ => "Strong"
        };

        private Color StrengthColor => StrengthScore switch
        {
            <= 1 => Color.Error,
            2 or 3 => Color.Warning,
            4 => Color.Info,
            _ => Color.Success
        };

        // ---------- Actions ----------

        private async Task HandleRegister()
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
                var client = ClientFactory.CreateClient("ChatAppAPI");
                var trimmedEmail = email!.Trim();

                var newUser = new
                {
                    Username = username!.Trim(),
                    Password = password,
                    User_email = trimmedEmail
                };

                var result = await client.PostAsJsonAsync("api/users/register", newUser);

                if (result.IsSuccessStatusCode)
                {
                    Snackbar.Add("Account created! You can now log in.", Severity.Success);
                    Navigation.NavigateTo($"/login?email={Uri.EscapeDataString(trimmedEmail)}");
                    return;
                }

                var serverMessage = await ReadErrorMessageAsync(result);
                var message = result.StatusCode switch
                {
                    HttpStatusCode.Conflict => serverMessage ?? "That username or email is already taken.",
                    HttpStatusCode.BadRequest => serverMessage ?? "Please check your details and try again.",
                    _ => serverMessage ?? "Registration failed. Please try again later."
                };
                Snackbar.Add(message, Severity.Error);
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

        /// <summary>
        /// Pulls a readable message out of an error response: plain text, or a JSON body
        /// with "message" / "detail" / "title" (ProblemDetails).
        /// </summary>
        private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response)
        {
            try
            {
                var body = (await response.Content.ReadAsStringAsync()).Trim();
                if (string.IsNullOrEmpty(body)) return null;

                if (body.StartsWith('{'))
                {
                    using var doc = JsonDocument.Parse(body);
                    foreach (var name in new[] { "message", "Message", "detail", "title" })
                    {
                        if (doc.RootElement.TryGetProperty(name, out var prop) &&
                            prop.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(prop.GetString()))
                            return prop.GetString();
                    }
                    return null;
                }

                if (body.StartsWith('"')) body = JsonSerializer.Deserialize<string>(body) ?? body;

                // Ignore HTML error pages or very long bodies.
                return body.StartsWith('<') || body.Length > 200 ? null : body;
            }
            catch
            {
                return null;
            }
        }

        private async Task HandleKeyPress(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
                await HandleRegister();
        }

        private void GoToLogin() => Navigation.NavigateTo("/login");

        private void TogglePasswordVisibility()
        {
            isShow = !isShow;
            PasswordInputIcon = isShow ? Icons.Material.Filled.Visibility : Icons.Material.Filled.VisibilityOff;
            PasswordInput = isShow ? InputType.Text : InputType.Password;
        }
    }
}
