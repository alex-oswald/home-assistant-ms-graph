using Azure.Identity;
using Microsoft.Graph;

namespace HomeAssistantMicrosoftGraph.CalendarApp;

public interface IGraphServiceClientFactory
{
    GraphServiceClient CreateServiceClient();
}

public class GraphServiceClientFactory : IGraphServiceClientFactory
{
    public GraphServiceClient CreateServiceClient()
    {
        var scopes = new[] { "User.Read" };
        var tenantId = "common";

        // Entra ID application client ID
        var clientId = "164977e9-d1b1-4288-b4d5-568c04135619";

        // Using Azure.Identity;
        var options = new DeviceCodeCredentialOptions
        {
            AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
            ClientId = clientId,
            TenantId = tenantId,
            // Callback function that receives the user prompt
            // Prompt contains the generated device code that user must
            // enter during the auth process in the browser
            DeviceCodeCallback = (code, cancellation) =>
            {
                Console.WriteLine(code.Message);
                return Task.FromResult(0);
            },
        };

        // https://learn.microsoft.com/dotnet/api/azure.identity.devicecodecredential
        var deviceCodeCredential = new DeviceCodeCredential(options);

        var graphClient = new GraphServiceClient(deviceCodeCredential, scopes);
        return graphClient;
    }
}

public static class GraphServiceClientFactoryExtensions
{
    public static IServiceCollection AddGraphServiceClient(this IServiceCollection services)
    {
        services.AddTransient<IGraphServiceClientFactory, GraphServiceClientFactory>();
        services.AddTransient(sp =>
        {
            var graphServiceClientFactory = new GraphServiceClientFactory();
            var client = graphServiceClientFactory.CreateServiceClient();
            return client;
        });
        return services;
    }
}
