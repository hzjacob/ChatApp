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
        protected int messagePageSize = 20;
        protected int currentOffset = 0;
        protected bool hasMoreMesssages = false;
        public bool _isScrolledToTop;

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
                // Scroll to bottom after initial loa
            await JSRuntime.InvokeVoidAsync("eval", "document.getElementById('end-of-messages').scrollIntoView({behavior:'smooth'})");
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                    await JSRuntime.InvokeVoidAsync("watchChatScrollById", "chat-container", DotNetObjectReference.Create(this));
            }
        }
        public async Task LoadMessagesAsync(bool isInitialLoad = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (isInitialLoad)
            {
                currentOffset = 0;
                Messages.Clear();
            }

            var response = await SupabaseClient
                .From<Message>()
                .Order("created_at", Ordering.Descending)
                .Range(currentOffset, currentOffset + messagePageSize - 1)
                .Get();

            if (response.Models.Any())
            {
                var newMessage = response.Models.OrderBy(x => x.CreatedAt).ToList();
                
                if (isInitialLoad)
                {
                    Messages = newMessage;
                    StateHasChanged(); // Let UI render first
                    
                    // Traditional scroll to bottom for first load
                }
                else
                {
                    // --- THE INSTANT PIN LOGIC ---
                    // Use the class you applied to your MudCardContent
                    var selector = ".mud-card-content"; 

                    // 1. Save the current distance from the bottom
                    var scrollInfo = await JSRuntime.InvokeAsync<double>("eval", 
                        $"var el = document.querySelector('{selector}'); el.scrollHeight - el.scrollTop");

                    // 2. Add the older messages to the top
                    Messages.InsertRange(0, newMessage);
                    
                    // 3. Trigger Blazor render
                    StateHasChanged();

                    // 4. Restore the position in the next animation frame to prevent "flicker"
                    await JSRuntime.InvokeVoidAsync("eval", $@"
                        requestAnimationFrame(() => {{
                            var el = document.querySelector('{selector}');
                            el.scrollTop = el.scrollHeight - {scrollInfo};
                        }});
                    ");
                }

                currentOffset += messagePageSize;
                hasMoreMesssages = response.Models.Count >= messagePageSize;
            }
            else
            {
                hasMoreMesssages = false;
            }
            StateHasChanged();
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"loadmessage function timer: {sw.ElapsedMilliseconds} ms");
        }
        private async Task SetupRealtimeAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
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
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"setuprealtime function timer: {sw.ElapsedMilliseconds} ms");
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
        [JSInvokable] 
        public void OnChatScroll(bool atTop) 
        {
            if (_isScrolledToTop != atTop) 
            {

                _isScrolledToTop = atTop;

                StateHasChanged(); 
            }
        }
    }
}