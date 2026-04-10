using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aist.Core;

namespace Aist.Mcp;

internal sealed class AistApiClient : IDisposable
{
    private readonly HttpClientHandler _handler;
    private readonly HttpClient _httpClient;
    private static readonly Uri BaseUri = GetBaseUri();

    private static Uri GetBaseUri()
    {
        var url = Environment.GetEnvironmentVariable("AIST_API_URL") ?? "http://localhost:5192/api/v1/";
        if (!url.EndsWith('/'))
        {
            url += "/";
        }
        return new Uri(url, UriKind.Absolute);
    }

    public AistApiClient()
    {
        _handler = CreateHandler();
        _httpClient = new HttpClient(_handler, disposeHandler: false)
        {
            BaseAddress = BaseUri
        };
    }

    private static HttpClientHandler CreateHandler()
    {
        var handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = true
        };

        if (BaseUri.IsLoopback)
        {
            handler.UseProxy = false;
        }

        return handler;
    }

    public async Task<List<ProjectResponse>?> GetProjectsAsync() =>
        await _httpClient.GetFromJsonAsync("projects", AistJsonContext.Default.ListProjectResponse).ConfigureAwait(false);

    public async Task<ProjectResponse?> CreateProjectAsync(string title)
    {
        var response = await _httpClient.PostAsJsonAsync("projects", new CreateProjectRequest(title), AistJsonContext.Default.CreateProjectRequest).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync(AistJsonContext.Default.ProjectResponse).ConfigureAwait(false);
    }

    public async Task DeleteProjectAsync(string projectId)
    {
        var response = await _httpClient.DeleteAsync($"projects/{projectId}").ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<List<JobResponse>?> GetJobsAsync(string? projectId = null)
    {
        var path = string.IsNullOrWhiteSpace(projectId) ? "jobs" : $"jobs?projectId={Uri.EscapeDataString(projectId)}";
        return await _httpClient.GetFromJsonAsync(path, AistJsonContext.Default.ListJobResponse).ConfigureAwait(false);
    }

    public async Task<JobResponse?> GetJobAsync(string jobId) =>
        await _httpClient.GetFromJsonAsync($"jobs/{jobId}", AistJsonContext.Default.JobResponse).ConfigureAwait(false);

    public async Task<JobResponse?> CreateJobAsync(CreateJobRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("jobs", request, AistJsonContext.Default.CreateJobRequest).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync(AistJsonContext.Default.JobResponse).ConfigureAwait(false);
    }

    public async Task UpdateJobStatusAsync(string jobId, JobStatus status)
    {
        var response = await _httpClient.PatchAsJsonAsync($"jobs/{jobId}/status", new UpdateJobStatusRequest(status), AistJsonContext.Default.UpdateJobStatusRequest).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task UpdateJobAsync(string jobId, UpdateJobRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"jobs/{jobId}", request, AistJsonContext.Default.UpdateJobRequest).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task DeleteJobAsync(string jobId)
    {
        var response = await _httpClient.DeleteAsync($"jobs/{jobId}").ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<List<UserStoryResponse>?> GetStoriesByJobAsync(string jobId) =>
        await _httpClient.GetFromJsonAsync($"userstories/by-job/{jobId}", AistJsonContext.Default.ListUserStoryResponse).ConfigureAwait(false);

    public async Task<UserStoryResponse?> CreateStoryAsync(CreateUserStoryRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("userstories", request, AistJsonContext.Default.CreateUserStoryRequest).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync(AistJsonContext.Default.UserStoryResponse).ConfigureAwait(false);
    }

    public async Task SetStoryCompleteAsync(string storyId, bool isComplete)
    {
        var response = await _httpClient.PatchAsJsonAsync($"userstories/{storyId}/complete", new UpdateUserStoryCompleteRequest(isComplete), AistJsonContext.Default.UpdateUserStoryCompleteRequest).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<List<AcceptanceCriteriaResponse>?> GetCriteriaByStoryAsync(string storyId) =>
        await _httpClient.GetFromJsonAsync($"acceptancecriterias/by-story/{storyId}", AistJsonContext.Default.ListAcceptanceCriteriaResponse).ConfigureAwait(false);

    public async Task<AcceptanceCriteriaResponse?> CreateCriteriaAsync(CreateAcceptanceCriteriaRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("acceptancecriterias", request, AistJsonContext.Default.CreateAcceptanceCriteriaRequest).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync(AistJsonContext.Default.AcceptanceCriteriaResponse).ConfigureAwait(false);
    }

    public async Task SetCriteriaAsync(string criteriaId, bool isMet)
    {
        var response = await _httpClient.PatchAsJsonAsync($"acceptancecriterias/{criteriaId}", new UpdateAcceptanceCriteriaRequest(isMet), AistJsonContext.Default.UpdateAcceptanceCriteriaRequest).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<List<ProgressLogResponse>?> GetLogsByStoryAsync(string storyId) =>
        await _httpClient.GetFromJsonAsync($"progresslogs/by-story/{storyId}", AistJsonContext.Default.ListProgressLogResponse).ConfigureAwait(false);

    public async Task<ProgressLogResponse?> AddLogAsync(CreateProgressLogRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("progresslogs", request, AistJsonContext.Default.CreateProgressLogRequest).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync(AistJsonContext.Default.ProgressLogResponse).ConfigureAwait(false);
    }

    public async Task<JsonNode> HealthAsync()
    {
        var response = await _httpClient.GetAsync("../health").ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonNode.Parse(body) ?? new JsonObject { ["status"] = "unknown" };
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HttpRequestException($"Backend returned {(int)response.StatusCode} {response.StatusCode}. {body}");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }
}
