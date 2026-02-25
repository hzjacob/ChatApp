
using Microsoft.AspNetCore.Components;
using ChatApp.Models;
using System.Net.Http.Json;

namespace ChatApp.Pages
{
    public partial class Test 
    {
        public string responseCheck = "";
        [Inject] private Supabase.Client? SupabaseClient {get; set;}
        [Inject] private IHttpClientFactory ClientFactory {get; set;} = default!;
        protected override async Task OnInitializedAsync()
        {
            await CallApi();
        }
        public async Task CallApi()
        {
            var session = SupabaseClient.Auth.CurrentSession;
            var token = session?.AccessToken;
            var client = ClientFactory.CreateClient("ChatAppAPI");

            var result = await client.GetAsync("api/message/search?query=sea");

            if (result.IsSuccessStatusCode)
            {
                if(token == null)
                {
                    responseCheck = "Token is null";
                }
                else
                {
                    var messages = await result.Content.ReadFromJsonAsync<List<Message>>();
                    responseCheck = messages.Count.ToString();
                }

            }
            
        }
    }
}