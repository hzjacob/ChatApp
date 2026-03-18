using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Supabase.Realtime;

namespace ChatApp.Services
{       

    public class States
    {
        private readonly NavigationManager _nav;
        private readonly IJSRuntime _iJs;
        private readonly Supabase.Client _supabase;
        public States(NavigationManager nav, IJSRuntime iJs, Supabase.Client supabase)
        {
            _nav = nav;
            _iJs = iJs;
            _supabase = supabase;
        }
        public async Task Logout(Supabase.Realtime.RealtimeChannel? realtimeChannel)
        {
            if (realtimeChannel != null )
            {
                realtimeChannel.Unsubscribe();
            }
            _supabase.Realtime.Disconnect();

            await _iJs.InvokeVoidAsync("localStorage.removeItem", "user");

            _nav.NavigateTo("/", true);
        }

    }
}