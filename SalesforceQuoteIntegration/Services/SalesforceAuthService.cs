using Newtonsoft.Json;

namespace SalesforceQuoteIntegration.Services;

public class SalesforceAuthService
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _loginUrl;

    private string? _accessToken;
    private string? _instanceUrl;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public SalesforceAuthService(string clientId, string clientSecret, string loginUrl)
    {
        var handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = false
        };
        _httpClient = new HttpClient(handler);
        //_httpClient = new HttpClient();
        _clientId = clientId;
        _clientSecret = clientSecret;
        _loginUrl = loginUrl;
    }
    public async Task<(string accessToken, string instanceUrl)> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            return (_accessToken!, _instanceUrl!);

        var content = new FormUrlEncodedContent(new[]
        {
        new KeyValuePair<string, string>("grant_type", "client_credentials"),
        new KeyValuePair<string, string>("client_id", _clientId),
        new KeyValuePair<string, string>("client_secret", _clientSecret)
    });

        var response = await _httpClient.PostAsync($"{_loginUrl}/services/oauth2/token", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(json)
            ?? throw new Exception("Failed to deserialize Salesforce auth response");

        _accessToken = (string)result.access_token;
        _instanceUrl = (string)result.instance_url;

        // Salesforce may return issued_at; if present, we could use it, but the
        // safest approach for the client-credentials flow is a conservative fixed
        // window well under any typical session timeout. 10 minutes forces a fresh
        // token often enough that we never ride an expired one into a 403.
        _tokenExpiry = DateTime.UtcNow.AddMinutes(10);

        return (_accessToken!, _instanceUrl!);
    }

    /// <summary>
    /// Forces the next GetTokenAsync call to fetch a brand-new token,
    /// discarding any cached one. Call this when a 403 indicates the
    /// current session/token has been invalidated.
    /// </summary>
    public void InvalidateToken()
    {
        _accessToken = null;
        _tokenExpiry = DateTime.MinValue;
    }
}
