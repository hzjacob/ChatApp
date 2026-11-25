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
    using System.Net.Http.Json;

    namespace ChatApp.Pages
    {
        public partial class ChatBase : ComponentBase
        {
            [Inject] private NavigationManager Navigation { get; set; } = default!;
            [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
            [Inject] private Supabase.Client SupabaseClient { get; set; } = default!;
            [Inject] private ISnackbar Snackbar { get; set; } = default!;
            [Inject] IHttpClientFactory ClientFactory {get; set;} =default!;
            public string? CurrentUser { get; set; }
            protected List<MessageDTO> Messages { get; set; } = new();
            protected string NewMessage { get; set; } = "";
            protected ElementReference chatMessages;
            public List<PresenceDTO> OnlineUsers { get; set; } = new();

            public bool _shouldPreventDefault = false;
            public bool _isSending = false;
            protected int messagePageSize = 20;
            protected int currentOffset = 0;
            protected bool hasMoreMesssages = false;
            public bool _isScrolledToTop;
            public bool _isLoadingOlder = false;
            private CancellationTokenSource? _presenceCts;
            protected List<string> TypingUsers {get; set;} = new();
            private System.Timers.Timer? _typingTimer;
            public string? Token {get; set;}
            public string? RefreshToken{get; set;}
            private Supabase.Realtime.RealtimeChannel? _channel;
            private Supabase.Realtime.RealtimeChannel? _presenceChannel;
            private System.Timers.Timer? _presenceTimer;
            public long SelectedPrivateUser {get; set;}
            protected string? CurrentRoomId {get; set;}
            private int CurrentUserId {get; set;} // Default to global chat ID
            private PresenceDTO? globalChatUser = new PresenceDTO { UserId = 0, Username = "Global Chat", LastSeen = DateTime.UtcNow };
            private long sendToId = 0;
            private DotNetObjectReference<ChatBase>? _dotNetRef;

            // Set when the next render should scroll the chat to the bottom.
            // Scrolling happens in OnAfterRenderAsync so the new messages are already in the DOM.
            private bool _scrollToBottomPending;
            private bool _scrollSmooth;

            protected void RequestScrollToBottom(bool smooth)
            {
                _scrollToBottomPending = true;
                _scrollSmooth = smooth;
            }


            protected override async Task OnInitializedAsync()
            {
                RefreshToken = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "refreshToken");
                CurrentUser = await JSRuntime.InvokeAsync<string?>("sessionStorage.getItem", "username");
                Token = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "authToken");
                var userIdStr = await JSRuntime.InvokeAsync<string?>("sessionStorage.getItem", "userId");

                if (string.IsNullOrEmpty(CurrentUser) || string.IsNullOrEmpty(Token)
                    || !int.TryParse(userIdStr, out var userId))
                {
                        Navigation.NavigateTo("/");
                        return;
                }
                CurrentUserId = userId;
                CurrentRoomId = "public-messages";

                // Realtime (websocket connect + two channel subscriptions) is the slow part,
                // so start it first but don't make the messages / online list wait for it.
                var realtimeTask = StartRealtimeAsync();

                // Messages, online list and heartbeat are independent HTTP calls: run them together.
                await Task.WhenAll(LoadMessagesAsync(), RefreshOnlineUsersAsync(), SendHeartbeat());

                // If this is our first heartbeat the list may have been fetched before our row existed.
                if (!OnlineUsers.Any(u => u.UserId == CurrentUserId))
                {
                    await RefreshOnlineUsersAsync();
                }

                StartHeartbeatTimer(skipFirst: true);

                await realtimeTask;
            }

            private async Task StartRealtimeAsync()
            {
                try
                {
                    await SupabaseClient.Realtime.ConnectAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Realtime connect failed: {ex.Message}");
                    Snackbar.Add("Live updates are unavailable right now. Refresh to see new messages.", Severity.Warning);
                    return;
                }

                // Both subscriptions wait for Supabase to acknowledge; do them in parallel.
                await Task.WhenAll(SetupRealtimeAsync(), OnlineUsersAsync());
            }

            protected override async Task OnAfterRenderAsync(bool firstRender)
            {
                if (firstRender)
                {
                    _dotNetRef = DotNetObjectReference.Create(this);
                    await JSRuntime.InvokeVoidAsync("chatHelpers.watchChatScrollById", "chat-container", _dotNetRef);
                }

                if (_scrollToBottomPending)
                {
                    _scrollToBottomPending = false;
                    await JSRuntime.InvokeVoidAsync("chatHelpers.ScrollToBottom", "chat-container", _scrollSmooth);
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
                Console.WriteLine($"Loading messages for Room: {CurrentRoomId}, Offset: {currentOffset}, PageSize: {messagePageSize}");
                var url = $"api/message/paged?currentOffset={currentOffset}&messagePagesize={messagePageSize}&roomId={CurrentRoomId}";

                var client = ClientFactory.CreateClient("ChatAppAPI");
                client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
                
                var response = await client.GetFromJsonAsync<List<MessageDTO>>(url);

                if (response!= null && response.Any())
                {
                    var newMessage = response.OrderBy(x => x.CreatedAt).ToList();
                    
                    if (isInitialLoad)
                    {
                        Messages = newMessage;
                        // Jump (no animation) to the bottom as soon as this renders.
                        RequestScrollToBottom(smooth: false);
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
                    hasMoreMesssages = response.Count >= messagePageSize;
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
                try
                {
                    // Unsubscribe from the previous channel if it exists
                    if (_channel != null)
                    {
                        _channel.Unsubscribe();
                        SupabaseClient.Realtime.Remove(_channel);
                        _channel = null;
                    }

                    _channel = SupabaseClient.Realtime.Channel(CurrentRoomId);

                    var presenceKey = CurrentUser ?? Guid.NewGuid().ToString();
                    var initialSyncTcs = new TaskCompletionSource<bool>();

                    // Setup message changes listener
                    _channel.Register(new PostgresChangesOptions("public", "messages", eventType: PostgresChangesOptions.ListenType.Inserts));

                    _channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.Inserts, async (sender, change) =>
                    {
                        var data = change.Model<Message>();

                        if(data == null || data.RoomId != CurrentRoomId)
                        {
                            return;
                        }

                        await InvokeAsync(() =>
                        {
                            // Already shown (e.g. it came in with the page load).
                            if (Messages.Any(m => m.Id == data.Id))
                            {
                                return;
                            }

                            // Our own message: replace the optimistic copy added in SendMessageAsync.
                            var pending = data.SenderId == CurrentUserId
                                ? Messages.FirstOrDefault(m => m.Id == null && m.RoomId == data.RoomId && m.Content == data.Content)
                                : null;

                            if (pending != null)
                            {
                                pending.Id = data.Id;
                                pending.CreatedAt = data.CreatedAt;
                                pending.SendTo = data.SendTo;
                            }
                            else
                            {
                                Messages.Add(new MessageDTO
                                {
                                    Id = data.Id,
                                    Username = data.Username,
                                    Content = data.Content,
                                    CreatedAt = data.CreatedAt,
                                    SendTo = data.SendTo,
                                    RoomId = data.RoomId,
                                    SenderId = data.SenderId
                                });
                                RequestScrollToBottom(smooth: true);
                            }

                            StateHasChanged();
                        });
                    });
                    
                    // Subscribe to channel
                    await _channel.Subscribe();

                    // Track user presence AFTER subscription + initial sync wait
                    // Force one immediate rebuild from the latest state.
                }
                catch (Exception ex)
                {
                    // Messages still load and send over the API; only live updates are lost.
                    Console.WriteLine($"Error in SetupRealtimeAsync: {ex.Message}");
                    Snackbar.Add("Live updates are unavailable right now. Refresh to see new messages.", Severity.Warning);
                }
            }
            
            public async Task SendMessageAsync()
            {
                if (string.IsNullOrWhiteSpace(NewMessage) || _isSending)
                    return;
                
                _isSending = true;

                var messageText = NewMessage;
                NewMessage = "";

                // Show the message straight away (Id == null marks it as not yet confirmed).
                // The realtime insert handler swaps in the saved copy when it arrives.
                var message = new MessageDTO
                {
                    Username = CurrentUser ?? "Anonymous",
                    Content = messageText,
                    CreatedAt = DateTime.UtcNow,
                    SendTo = sendToId,
                    RoomId = CurrentRoomId,
                    SenderId = CurrentUserId
                };
                Messages.Add(message);
                RequestScrollToBottom(smooth: true);
                StateHasChanged();

                try
                {
                    var client = ClientFactory.CreateClient("ChatAppAPI");

                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);

                    var request = await client.PostAsJsonAsync("/api/message", message);

                    if (request?.IsSuccessStatusCode != true)
                    {
                        Messages.Remove(message);
                        NewMessage = messageText;
                        Snackbar.Add("Failed to send. Try again.", Severity.Error);
                    }
                }
                catch (Exception ex)
                {
                    Messages.Remove(message);
                    NewMessage = messageText;
                    Snackbar.Add("Connection error.", Severity.Error);
                    Console.WriteLine(ex.Message);
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
            public async Task HandleLogout()
            {
                
                try
                {
                    await JSRuntime.InvokeVoidAsync("sessionStorage.removeItem", "username");
                    SupabaseClient.Realtime.Disconnect();
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                    
                    
                Navigation.NavigateTo("/");
            }
            public async ValueTask DisposeAsync()
            {
                try
                {
                    _presenceTimer?.Stop();
                    _presenceTimer?.Dispose();
                    _presenceTimer = null;

                    if (_channel != null)
                    {
                        _channel.Unsubscribe();
                        SupabaseClient.Realtime.Remove(_channel);
                    }

                    if (_presenceChannel != null)
                    {
                        _presenceChannel.Unsubscribe();
                        SupabaseClient.Realtime.Remove(_presenceChannel);
                    }

                    _dotNetRef?.Dispose();

                    SupabaseClient.Realtime.Disconnect();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during disposal: {ex.Message}");
                }
            }
            public async Task OnlineUsersAsync()
            {
                try
                {
                    _presenceChannel = SupabaseClient.Realtime.Channel("online-presence-only");

                    // 1. Listen ONLY for brand new people joining
                    var insertOptions = new PostgresChangesOptions("public", "online_users", 
                        eventType: PostgresChangesOptions.ListenType.Inserts);
                    
                    // 2. Listen ONLY for the Cron job (or logout) deleting someone
                    var deleteOptions = new PostgresChangesOptions("public", "online_users", 
                        eventType: PostgresChangesOptions.ListenType.Deletes);

                    _presenceChannel.Register(insertOptions);
                    _presenceChannel.Register(deleteOptions);

                    _presenceChannel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.Inserts, async (sender, change) =>
                    {
                        await RefreshOnlineUsersAsync();
                    });

                    _presenceChannel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.Deletes, async (sender, change) =>
                    {
                        await RefreshOnlineUsersAsync();
                    });

                    await _presenceChannel.Subscribe();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
            public async Task RefreshOnlineUsersAsync()
            {
                try
                {
                    var client = ClientFactory.CreateClient("ChatAppAPI");
                    client.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);

                    var response = await client.GetFromJsonAsync<List<PresenceDTO>>("api/presence/online");

                    if (response != null)
                    {
                        OnlineUsers = response.OrderByDescending(u => u.LastSeen).ToList();
                        StateHasChanged();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching online users: {ex.Message}");
                }
            }
            private void StartHeartbeatTimer(bool skipFirst = false)
            {
                _presenceTimer = new System.Timers.Timer(30000); // 30 seconds
                _presenceTimer.Elapsed += async (sender, e) => await SendHeartbeat();
                _presenceTimer.AutoReset = true;
                _presenceTimer.Enabled = true;

                if(!skipFirst)
                {
                    SendHeartbeat().ConfigureAwait(false);
                }
            }

            private async Task SendHeartbeat()
            {
                try
                {
                    var client = ClientFactory.CreateClient("ChatAppAPI");
                    client.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
                    
                    await client.PostAsync("api/presence/heartbeat", null);
                    Console.WriteLine($"Heartbeat sent at {DateTime.Now} for user {CurrentUser}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Heartbeat error: {ex.Message}");
                }
            }
            private async Task SelectedGlobalChat()
            {
                SelectedPrivateUser = 0;
                CurrentRoomId = "public-messages";
                await LoadMessagesAsync();
            }
            public async Task OpenPrivateChat(PresenceDTO targetUser)
            {
                if (targetUser.Username == CurrentUser)
                {
                    Snackbar.Add("You cannot chat with yourself!", Severity.Warning);
                    return;
                }
                sendToId = targetUser.UserId;
                SelectedPrivateUser = targetUser.UserId;
                CurrentRoomId = $"private-{Math.Min(CurrentUserId, targetUser.UserId)}-{Math.Max(CurrentUserId, targetUser.UserId)}";
                // Re-subscribe in parallel so the messages don't wait for Supabase's subscribe ack.
                var realtimeTask = SetupRealtimeAsync();
                await LoadMessagesAsync(); // scrolls to bottom once rendered
                await realtimeTask;
                Console.WriteLine($"Opened private chat with {targetUser.UserId} {targetUser.Username} in room {CurrentRoomId}");

            }
            public async Task ViewProfile(PresenceDTO user)
            {
                Snackbar.Add($"Viewing profile for {user.Username} (ID: {user.UserId})", Severity.Info);
                // Implement actual profile viewing logic here
            }
            public async Task SelectGlobalChat()
            {
                SelectedPrivateUser = 0;
                CurrentRoomId = "public-messages";
                sendToId = 0;
                var realtimeTask = SetupRealtimeAsync();
                await LoadMessagesAsync(); // scrolls to bottom once rendered
                await realtimeTask;
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