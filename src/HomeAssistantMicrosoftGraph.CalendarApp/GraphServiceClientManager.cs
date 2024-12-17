using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace HomeAssistantMicrosoftGraph.CalendarApp;

public interface IGraphServiceClientManager
{
    event EventHandler<DeviceCodeCallbackEventArgs> OnDeviceCodeCallback;

    GraphServiceClient Client { get; }
}

public class GraphServiceClientManager : IGraphServiceClientManager
{
    private readonly ILogger<GraphServiceClientManager> _logger;
    private readonly GraphServiceClientManagerOptions _options;
    private readonly string _authenticationRecordFullPath;
    private GraphServiceClient _client;

    public GraphServiceClientManager(
        ILogger<GraphServiceClientManager> logger,
        IOptions<GraphServiceClientManagerOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _authenticationRecordFullPath = Path.Combine(_options.AuthenticationRecordDirectory, _options.AuthenticationRecordFileName);
    }

    public event EventHandler<DeviceCodeCallbackEventArgs> OnDeviceCodeCallback;

    public GraphServiceClient Client
    {
        get
        {
            if (_client is null)
            {
                _client = GetClientAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            return _client!;
        }
        private set => _client = value;
    }

    private async Task<GraphServiceClient> GetClientAsync(CancellationToken cancellationToken)
    {
        var scopes = new[] { "User.Read", "Calendars.Read" };
        var tenantId = "common";

        AuthenticationRecord? authRecord = await ReadPersistedAuthenticationRecordAsync(cancellationToken).ConfigureAwait(false);

        DeviceCodeCredentialOptions options = new()
        {
            AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
            ClientId = _options.EntraIdApplicationClientId,
            TenantId = tenantId,
            DeviceCodeCallback = DeviceCodeCallback,
            AuthenticationRecord = authRecord,
            TokenCachePersistenceOptions = TokenCachePersistenceOptions,
        };

        DeviceCodeCredential deviceCodeCredential = new(options);

        if (authRecord is null)
        {
            authRecord = await deviceCodeCredential.AuthenticateAsync(
                new TokenRequestContext(scopes), cancellationToken).ConfigureAwait(false);
            await PersistAuthenticationRecordAsync(authRecord, cancellationToken).ConfigureAwait(false);
        }

        var graphClient = new GraphServiceClient(deviceCodeCredential);
        return graphClient;
    }

    private Task DeviceCodeCallback(DeviceCodeInfo code, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Device code callback received");
        OnDeviceCodeCallback.Invoke(this, new DeviceCodeCallbackEventArgs { DeviceCodeInfo = code });
        _logger.LogInformation(code.Message);
        return Task.CompletedTask;
    }

    private async Task PersistAuthenticationRecordAsync(AuthenticationRecord authenticationRecord, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Saving the authentication record file '{path}'", _authenticationRecordFullPath);
        using var stream = new FileStream(_authenticationRecordFullPath, FileMode.Create, FileAccess.Write);
        await authenticationRecord.SerializeAsync(stream, cancellationToken).ConfigureAwait(false);
    }
    
    private async Task<AuthenticationRecord?> ReadPersistedAuthenticationRecordAsync(CancellationToken cancellationToken)
    {
        _logger.LogTrace("Checking to see if authentication record file '{path}' exists", _authenticationRecordFullPath);
        if (!File.Exists(_authenticationRecordFullPath))
        {
            _logger.LogDebug("Authentication record file '{path}' does not exist", _authenticationRecordFullPath);
            return null;
        }

        try
        {
            _logger.LogTrace("Attempting to read authentication record file '{path}'", _authenticationRecordFullPath);
            using var stream = new FileStream(_authenticationRecordFullPath, FileMode.Open, FileAccess.Read);
            var record = await AuthenticationRecord.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
            return record;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading the authentication record");
            return null;
        }
    }

    private readonly static TokenCachePersistenceOptions TokenCachePersistenceOptions = new()
    {
        Name = "home-assistant-m365",
        UnsafeAllowUnencryptedStorage = true,
    };
}


public class GraphServiceClientManagerOptions
{
    public const string Section = nameof(GraphServiceClientManager);
    public string AuthenticationRecordDirectory { get; set; } = string.Empty;
    public string AuthenticationRecordFileName { get; set; } = "authenticationRecord.txt";
    public string EntraIdApplicationClientId { get; set; } = string.Empty;
}

public class DeviceCodeCallbackEventArgs : EventArgs
{
    public DeviceCodeInfo DeviceCodeInfo { get; set; }
}

public static class GraphServiceClientFactoryExtensions
{
    public static IServiceCollection AddGraphServiceClient(this IServiceCollection services)
    {
        services.AddSingleton<IGraphServiceClientManager, GraphServiceClientManager>();
        return services;
    }
}
