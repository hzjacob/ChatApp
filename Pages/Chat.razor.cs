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
            public List<UserDto> OnlineUsers { get; set; } = new();

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
            protected string? SelectedPrivateUser {get; set;} = null;
            protected string? CurrentRoomId {get; set;} = null;
            private int CurrentUserId {get; set;} = "public-messages".GetHashCode(); // Default to global chat ID
            private UserDto? globalChatUser = new UserDto { Id = 0, Username = "Global Chat", User_email = "", Password = "" };
            private int sendToId = 0;


            protected override async Task OnInitializedAsync()
            {
                RefreshToken = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "refreshToken");
                CurrentUser = await JSRuntime.InvokeAsync<string?>("sessionStorage.getItem", "username");
                Token = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "authToken");
                CurrentUserId = int.Parse(await JSRuntime.InvokeAsync<string?>("sessionStorage.getItem", "userId"));

                if (string.IsNullOrEmpty(CurrentUser) || string.IsNullOrEmpty(Token))
                {
                        Navigation.NavigateTo("/");
                        return;
                }
                CurrentRoomId = "public-messages";

                await SupabaseClient.Realtime.ConnectAsync();
                await LoadMessagesAsync();
                await SetupRealtimeAsync();                
                
                await OnlineUsersAsync();

                await SendHeartbeat(); // Send an immediate heartbeat on load
              
                StartHeartbeatTimer(skipFirst: true); // Start the timer
                
                await Task.Delay(100);
                
                await RefreshOnlineUsersAsync();

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
                    _channel = SupabaseClient.Realtime.Channel("public-messages");

                    var presenceKey = CurrentUser ?? Guid.NewGuid().ToString();
                    var initialSyncTcs = new TaskCompletionSource<bool>();

                    // Setup message changes listener
                    _channel.Register(new PostgresChangesOptions("public", "messages", eventType: PostgresChangesOptions.ListenType.Inserts));

                    _channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.Inserts, async (sender, change) =>
                    {
                        var data = change.Model<Message>();

                        if(data == null)
                        {
                            Console.WriteLine("Data is null");
                            return;
                        }

                        await InvokeAsync(async () =>
                        {
                            var displayMessage = new MessageDTO
                            {
                                Id = data.Id,
                                Username = data.Username,
                                Content = data.Content,
                                CreatedAt = data.CreatedAt,
                                SendTo = data.SendTo,
                                RoomId = data.RoomId
                            };

                            Messages = Messages.Append(displayMessage).ToList();
                            StateHasChanged();

                            await Task.Delay(50);
                            await JSRuntime.InvokeVoidAsync("ScrollToBottom", "chat-container");
                        });
                    });
                    
                    // Subscribe to channel
                    await _channel.Subscribe();

                    // Track user presence AFTER subscription + initial sync wait
                    // Force one immediate rebuild from the latest state.
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in SetupRealtimeAsync: {ex.Message}");
                    throw;
                }
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
                    var message = new MessageDTO
                    {
                        Username = CurrentUser ?? "Anonymous",
                        Content = messageText,
                        CreatedAt = DateTime.UtcNow,
                        SendTo = sendToId,
                        RoomId = CurrentRoomId,
                        SenderId = CurrentUserId
                    };

                    var client = ClientFactory.CreateClient("ChatAppAPI");
                    
                    client.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);

                    var request = await client.PostAsJsonAsync("/api/message", message);

                    if (request?.IsSuccessStatusCode != true)
                    {
                        NewMessage = messageText;
                        Snackbar.Add("Failed to send. Try again.", Severity.Error);
                    }
                }
                catch (Exception ex)
                {
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
                    if (_channel != null)
                    {
                        _channel.Unsubscribe();
                        SupabaseClient.Realtime.Remove(_channel);
                    }

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

                    var response = await client.GetFromJsonAsync<List<UserDto>>("api/presence/online");

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
                SelectedPrivateUser = null;
                CurrentRoomId = "public-messages";
                await LoadMessagesAsync();
            }
            public async Task OpenPrivateChat(UserDto targetUser)
            {
                if (targetUser.Username == CurrentUser)
                {
                    Snackbar.Add("You cannot chat with yourself!", Severity.Warning);
                    return;
                }
                SelectedPrivateUser = targetUser.Id.ToString();
                CurrentRoomId = $"private-{Math.Min(CurrentUserId, targetUser.Id)}-{Math.Max(CurrentUserId, targetUser.Id)}";
                await LoadMessagesAsync();
            }
            public async Task ViewProfile(UserDto user)
            {
                Snackbar.Add($"Viewing profile for {user.Username} (ID: {user.Id})", Severity.Info);
                // Implement actual profile viewing logic here
            }
            public async Task SelectUserForPrivateChat(UserDto user)
            {
                if (user.Username == CurrentUser)
                {
                    Snackbar.Add("You cannot chat with yourself!", Severity.Warning);
                    return;
                }
                SelectedPrivateUser = user.Id.ToString();
                CurrentRoomId = $"{Math.Min(CurrentUserId, user.Id)}-{Math.Max(CurrentUserId, user.Id)}";
                sendToId = user.Id;
                await LoadMessagesAsync();
            }
            public async Task SelectGlobalChat()
            {
                SelectedPrivateUser = null;
                CurrentRoomId = "public-messages";
                await LoadMessagesAsync();
                sendToId = 0;
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