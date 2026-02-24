
using Microsoft.AspNetCore.Components;

namespace ChatApp.Pages
{
    public partial class Test 
    {
        public string responseCheck = "";
        [Inject] private IHttpClientFactory ClientFactory {get; set;} = default!;
        protected override async Task OnInitializedAsync()
        {
            await CallApi();
        }
        public async Task CallApi()
        {
            var client = ClientFactory.CreateClient("ChatAppAPI");

            var result = await client.GetAsync("api/message");

            if (result.IsSuccessStatusCode)
            {
                responseCheck = "success";
            }
            
        }
    }
}