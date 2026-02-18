using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using Supabase;
using Postgrest;
using ChatApp.Models;
using static Postgrest.Constants;
using Supabase.Realtime.PostgresChanges;
using Microsoft.IdentityModel.Tokens;
using Supabase.Realtime.Models;
using System.Text.Json.Serialization;

namespace ChatApp.Pages
{
    public partial class ChatBase : ComponentBase
    {
        [Inject] private NavigationManager Navigation { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private Supabase.Client SupabaseClient { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        // These properties are accessed by your Chat.razor
        public string? CurrentUser { get; set; }
        protected List<Message> Messages { get; set; } = new();
        protected string NewMessage { get; set; } = "";
        protected ElementReference chatMessages;
        public List<string> OnlineUsers { get; set; } = new();

        public bool _shouldPreventDefault = false;
        public bool _isSending = false;
        

        protected override async Task OnInitializedAsync()
        {
            
            CurrentUser = await JSRuntime.InvokeAsync<string?>("sessionStorage.getItem", "username");

            if (string.IsNullOrEmpty(CurrentUser))
            {
                Navigation.NavigateTo("/");
                return;
            }
            await SupabaseClient.Realtime.ConnectAsync();
            await LoadMessagesAsync();
            await SetupRealtimeAsync();
                // Scroll to bottom after initial load
            await Task.Delay(100); // Small delay to ensure messages are rendered
            await JSRuntime.InvokeVoidAsync("eval", "document.getElementById('end-of-messages').scrollIntoView({behavior:'smooth'})");
        }
        private async Task LoadMessagesAsync()
        {
            var response = await SupabaseClient.From<Message>().Order("created_at", Ordering.Ascending).Get();
            Messages = response.Models;
            StateHasChanged();
        }
        private async Task SetupRealtimeAsync()
        {
            var channel = SupabaseClient.Realtime.Channel("public-messages");
        // 1. Generate a unique key for this session
            var presenceKey = Guid.NewGuid().ToString();

            // 2. Register with your custom model and capture the presence manager
            var presence = channel.Register<PresenceUser>(presenceKey);

            // 3. Attach the handler using the required EventType.Sync
            presence.AddPresenceEventHandler(Supabase.Realtime.Interfaces.IRealtimePresence.EventType.Sync, (sender, type) =>
            {
                // Get the latest snapshot of who is online
                var state = presence.CurrentState;

                InvokeAsync(() =>
                {
                    // Flatten the dictionary and grab unique usernames
                    OnlineUsers = state.Values
                        .SelectMany(x => x)
                        .Select(u => u.Username)
                        .Distinct()
                        .ToList();

                    StateHasChanged();
                });
            });
            // 1. Register the listener for Inserts
            channel.Register(new PostgresChangesOptions("public", "messages", eventType: PostgresChangesOptions.ListenType.Inserts));

            // 2. Use the non-generic handler
            channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.Inserts, (sender, change) =>
            {
                // Deserialization happens here
                var newMessage = change.Model<Message>();

                if (newMessage != null)
                {
                    InvokeAsync(async () =>
                    {
                        Messages.Add(newMessage);
                        StateHasChanged();
                        await Task.Delay(50); // Small delay to ensure UI updates before scrolling
                        await JSRuntime.InvokeVoidAsync("eval", "document.getElementById('end-of-messages').scrollIntoView({behavior:'smooth'})");
                    });
                }
            });
            
            await channel.Subscribe();

            presence.Track(new PresenceUser 
            { 
                Username = CurrentUser ?? "Anonymous", 
                OnlineAt = DateTime.Now 
            });
        }
        public async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(NewMessage) || _isSending)
                return;
            
            _isSending = true;
            // 1. Capture the message and clear the input IMMEDIATELY
            var messageText = NewMessage;
            NewMessage = ""; 
            StateHasChanged(); // This makes the text disappear instantly for the user
            await JSRuntime.InvokeVoidAsync("eval", "document.getElementById('end-of-messages').scrollIntoView({behavior:'smooth'})");

            try
            {
                var message = new Message
                {
                    Username = CurrentUser ?? "Anonymous",
                    Content = messageText,
                    CreatedAt = DateTime.UtcNow
                };

                // 2. Send to Supabase in the background
                var response = await SupabaseClient.From<Message>().Insert(message);

                if (response?.ResponseMessage?.IsSuccessStatusCode != true)
                {
                    // If it failed, give the text back so they don't lose it
                    NewMessage = messageText;
                    Snackbar.Add("Failed to send. Try again.", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                NewMessage = messageText;
                Snackbar.Add("Connection error.", Severity.Error);
            }
            finally
            {
                _isSending = false;
                StateHasChanged();
            }
        }
        public async Task HandleKeyPress(KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !e.ShiftKey )
            {
                _shouldPreventDefault =true;
                await SendMessageAsync();
            }
            else
            {
                _shouldPreventDefault = false;
            }
        }

        public class PresenceUser: BasePresence
        {
            [JsonPropertyName("username")]
            public string Username { get; set; } = string.Empty;
            [JsonPropertyName("online_at")]
            public DateTime OnlineAt { get; set; }
        }
    }
}