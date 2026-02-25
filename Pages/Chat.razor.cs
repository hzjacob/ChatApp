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
        public bool _isLoadingOlder = false;
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
            await JSRuntime.InvokeVoidAsync("ScrollToBottom", "chat-container");
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
            try
            {
                 if (isInitialLoad)
            {
                currentOffset = 0;
                Messages.Clear();
            }
            else
            {
                _isLoadingOlder = true;
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
                
            }
            finally
            {
                _isLoadingOlder = false;
                StateHasChanged();
            }
           

        }
        private async Task SetupRealtimeAsync()
        {
            var channel = SupabaseClient.Realtime.Channel("public-messages");

            var presenceKey = Guid.NewGuid().ToString();


            var presence = channel.Register<PresenceUser>(presenceKey);


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
            channel.Register(new PostgresChangesOptions("public", "messages", eventType: PostgresChangesOptions.ListenType.Inserts));

            channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.Inserts, (sender, change) =>
            {

                var newMessage = change.Model<Message>();

                if (newMessage != null)
                {
                    InvokeAsync(async () =>
                    {
                        Messages.Add(newMessage);
                        StateHasChanged();
                        await Task.Delay(50); 
                        await JSRuntime.InvokeVoidAsync("ScrollToBottom", "chat-container");
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

            var messageText = NewMessage;
            NewMessage = ""; 
            StateHasChanged();
            await JSRuntime.InvokeVoidAsync("ScrollToBottom", "chat-container");

            try
            {
                var message = new Message
                {
                    Username = CurrentUser ?? "Anonymous",
                    Content = messageText,
                    CreatedAt = DateTime.UtcNow
                };

                var response = await SupabaseClient.From<Message>().Insert(message);

                if (response?.ResponseMessage?.IsSuccessStatusCode != true)
                {
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