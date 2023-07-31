using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Primitives;
using Microsoft.Identity.Client;

namespace azureai.src.Dataverse;

internal class DataverseClient
{
    private readonly string _baseUri;

    private readonly IPublicClientApplication _clientApplication;

    public DataverseClient(string baseUri, string clientAppId)
    {
        _baseUri = baseUri;

        _clientApplication = PublicClientApplicationBuilder.Create(clientAppId)
            .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithDefaultRedirectUri()
            .Build();
    }

    public enum DateComparer
    {
        Before,
        On,
        After,
    }

    public enum TargetField
    {
        CreatedOn,
        ModifiedOn,
        WindowStart,
        WindowEnd,
    }

    public async Task<IReadOnlyList<string>> GetEntitiesByDateAsync(
        string entityName,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter,
        TargetField targetField,
        int limit = 10)
    {
        string fieldName = targetField switch
        {
            TargetField.CreatedOn => "createdon",
            TargetField.ModifiedOn => "modifiedon",
            TargetField.WindowStart => "msdyn_datewindowstart",
            TargetField.WindowEnd => "msdyn_datewindowend",
            _ => throw new NotImplementedException(),
        };

        string? notBeforeVal = null;
        if (notBefore is DateTimeOffset nb)
        {
            notBeforeVal = $"{fieldName} gt '{nb}'";
        }

        string? notAfterVal = null;
        if (notAfter is DateTimeOffset na)
        {
            notAfterVal = $"{fieldName} lt '{na}'";
        }

        string combinedFilter = string.Join(" and ", new[] { notBeforeVal, notAfterVal }.Where(x => x != null));

        const string apiVersion = "v9.2";
        string url = $"{_baseUri}/api/data/{apiVersion}/{entityName}?$top={limit}&$filter={combinedFilter}";

        Console.WriteLine($"Hitting uri: {url}");

        using HttpClient httpClient = await CreateClient();

        HttpResponseMessage response = await httpClient.GetAsync(url);

        string responseContent = await response.Content.ReadAsStringAsync();

        ODataList oDataList = JsonSerializer.Deserialize<ODataList>(responseContent) ?? throw new Exception("Could not parse response.");

        return oDataList.Value.Select(j => JsonSerializer.Serialize(j) ?? throw new InvalidDataException("Could not parse json for work order")).ToList();
    }

    public async Task<string> GetTableSchemaAsync(string tableName)
    {
        // GET [Organization URI]/api/data/v9.2/EntityDefinitions?$select=SchemaName&$filter=LogicalName eq 'account' or LogicalName eq 'contact'&$expand=Attributes($select=LogicalName;$filter=IsValidForCreate eq true)
        using HttpClient httpClient = await CreateClient();

        string url = $"{_baseUri}/api/data/v9.2/EntityDefinitions?$top=5";

        HttpResponseMessage response = await httpClient.GetAsync(url);

        string responseContent = await response.Content.ReadAsStringAsync();

        return "empty";
    }

    public async Task<string> SearchDataverse(RelevancySearchQuery searchQuery, bool mustMatchAll = true)
    {
        using HttpClient httpClient = await CreateClient();

        string url = $"{_baseUri}/api/search/v1.0/query";

        string? dateQuery = BuildDateQueryString(searchQuery);

        string searchMode = mustMatchAll ? "all" : "any";

        string queryJson;
        if (dateQuery is not null)
        {
            queryJson = JsonSerializer.Serialize(new
            {
                search = searchQuery.RelevancySearchQueryText,
                top = 10,
                searchmode = searchMode,
                filter = dateQuery,
            });
        }
        else
        {
            queryJson = JsonSerializer.Serialize(new
            {
                search = searchQuery.RelevancySearchQueryText,
                top = 10,
                searchmode = searchMode,
            });
        }

        using var requestBody = new StringContent(queryJson, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await httpClient.PostAsync(url, requestBody);

        string responseContent = await response.Content.ReadAsStringAsync();

        return responseContent;
    }

    public async Task<string> GetEntityJsonByIdAsync(string entityName, string entityId)
    {
        using HttpClient httpClient = await CreateClient();

        const string apiVersion = "v9.2";

        var id = Guid.Parse(entityId);

        string url = $"{_baseUri}/api/data/{apiVersion}/{entityName}({id})";
        HttpResponseMessage response = await httpClient.GetAsync(url);

        string responseContent = await response.Content.ReadAsStringAsync();

        return responseContent;
    }

    public async Task<string> GetWorkOrderJsonByIdAsync(string id) => await GetEntityJsonByIdAsync("msdyn_workorders", id);

    public class RelevancySearchQuery
    {
        public string RelevancySearchQueryText { get; init; } = string.Empty;

        public string? DateFieldName { get; set; }

        public DateTimeOffset? NotBeforeUtc { get; set; }

        public DateTimeOffset? NotAfterUtc { get; set; }
    }

    private static string? BuildDateQueryString(RelevancySearchQuery searchQuery)
    {
        if (searchQuery.DateFieldName is null)
        {
            return null;
        }

        if (searchQuery.NotBeforeUtc is null && searchQuery.NotAfterUtc is null)
        {
            return null;
        }

        string notBefore = string.Empty;
        if (searchQuery.NotBeforeUtc is DateTimeOffset nb)
        {
            notBefore = $"{searchQuery.DateFieldName} gt {nb}";
        }

        string notAfter = string.Empty;
        if (searchQuery.NotAfterUtc is DateTimeOffset na)
        {
            notAfter = $"{searchQuery.DateFieldName} lt {na}";
        }

        string combinedFilter = string.Join(" and ", new[] { notBefore, notAfter }.Where(x => x != string.Empty));

        return combinedFilter;
    }

    private async Task<HttpClient> CreateClient()
    {
        string token = await AcquireTokenAsync();

        var httpClient = new HttpClient();

        httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        httpClient.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return httpClient;
    }

    private async Task<string> AcquireTokenAsync()
    {
        string[] scopes = new[] { $"{_baseUri}/user_impersonation" };

        IEnumerable<IAccount> accountsWithCachedTokens = await _clientApplication.GetAccountsAsync();

        if (accountsWithCachedTokens.FirstOrDefault() is IAccount account)
        {
            AuthenticationResult acquired = await _clientApplication.AcquireTokenSilent(scopes, account).ExecuteAsync();

            return acquired.AccessToken;
        }
        else
        {
            AuthenticationResult acquired = await _clientApplication.AcquireTokenInteractive(scopes).ExecuteAsync();

            return acquired.AccessToken;
        }
    }

    private class ODataList
    {
        [JsonPropertyName("value")]
        public List<object> Value { get; set; } = new List<object>();
    }
}
