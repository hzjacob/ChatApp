using Microsoft.AspNetCore.Components;

namespace ChatApp.Services
{
    public class UnauthorizedHandler: DelegatingHandler
    {
        private readonly NavigationManager? _nav;

        public UnauthorizedHandler(NavigationManager nav)
        {
            _nav = nav;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var response = await base.SendAsync(request, cancellation);
            if(response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _nav.NavigateTo("/", forceLoad:true);
            }

            return response;
        }
    }
}