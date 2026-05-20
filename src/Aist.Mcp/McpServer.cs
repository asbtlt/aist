using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aist.Core;

namespace Aist.Mcp;

internal sealed class McpServer
{
    private const string JsonRpcVersion = "2.0";
    private const string ProtocolVersion = "2024-11-05";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions ToolResultJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly AistApiClient _apiClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _useLineDelimitedJsonRpc;

    public McpServer(Stream input, Stream output, AistApiClient apiClient)
    {
        _input = input;
        _output = output;
        _apiClient = apiClient;
        _jsonOptions = JsonOptions;
        _useLineDelimitedJsonRpc = string.Equals(
            Environment.GetEnvironmentVariable("AIST_MCP_TRANSPORT"),
            "jsonl",
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                break;
            }

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                {
                    await WriteErrorAsync(null, -32600, "Invalid request: empty batch", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        await WriteErrorAsync(null, -32600, "Invalid request: batch item must be object", cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await HandleRequestAsync(item, cancellationToken).ConfigureAwait(false);
                }
                continue;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                await WriteErrorAsync(null, -32600, "Invalid request: expected JSON object", cancellationToken).ConfigureAwait(false);
                continue;
            }

            await HandleRequestAsync(root, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleRequestAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("method", out var methodProperty))
        {
            if (TryGetId(root, out var malformedId))
            {
                await WriteErrorAsync(malformedId, -32600, "Invalid request: missing method", cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        var method = methodProperty.GetString() ?? string.Empty;
        var hasId = TryGetId(root, out var id);
        var parameters = root.TryGetProperty("params", out var paramsProperty) ? paramsProperty : default;

        try
        {
            JsonNode? result = method switch
            {
                "initialize" => HandleInitialize(),
                "ping" => new JsonObject(),
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolsCallAsync(parameters).ConfigureAwait(false),
                "notifications/initialized" => null,
                _ => throw new InvalidOperationException($"Method not found: {method}")
            };

            if (hasId && result is not null)
            {
                await WriteResultAsync(id, result, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (!hasId)
            {
                return;
            }

            var code = ex is InvalidOperationException ? -32601 : -32000;
            await WriteErrorAsync(id, code, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static JsonNode HandleInitialize()
    {
        return new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "aist-mcp",
                ["version"] = "0.1.0"
            }
        };
    }

    private static JsonNode HandleToolsList()
    {
        return new JsonObject
        {
            ["tools"] = new JsonArray
            {
                Tool("health_check", "Check backend health.", Obj()),

                Tool("project_list", "List all projects.", Obj()),
                Tool("project_create", "Create project.", Obj(
                    Req("title", "string", "Project title.")), "title"),
                Tool("project_delete", "Delete project.", Obj(
                    Req("projectId", "string", "Project GUID.")), "projectId"),

                Tool("job_list", "List jobs. Optionally filter by project.", Obj(
                    Opt("projectId", "string", "Project GUID filter."))),
                Tool("job_get", "Get job by id.", Obj(
                    Req("jobId", "string", "Job GUID.")), "jobId"),
                Tool("job_create", "Create new job.", Obj(
                    Req("projectId", "string", "Project GUID."),
                    Req("shortSlug", "string", "Short slug."),
                    Req("title", "string", "Job title."),
                    ReqEnum("type", "Job type.", nameof(JobType.Feature), nameof(JobType.Fix), nameof(JobType.Refactor), nameof(JobType.Chore), nameof(JobType.Fmt), nameof(JobType.Doc)),
                    Req("description", "string", "Job description.")), "projectId", "shortSlug", "title", "type", "description"),
                Tool("job_update_status", "Update job status.", Obj(
                    Req("jobId", "string", "Job GUID."),
                    ReqEnum("status", "Job status.", nameof(JobStatus.Todo), nameof(JobStatus.InProgress), nameof(JobStatus.Done))), "jobId", "status"),
                Tool("job_update", "Update job fields.", Obj(
                    Req("jobId", "string", "Job GUID."),
                    Req("shortSlug", "string", "Short slug."),
                    Req("title", "string", "Job title."),
                    ReqEnum("type", "Job type.", nameof(JobType.Feature), nameof(JobType.Fix), nameof(JobType.Refactor), nameof(JobType.Chore), nameof(JobType.Fmt), nameof(JobType.Doc)),
                    Req("description", "string", "Job description.")), "jobId", "shortSlug", "title", "type", "description"),
                Tool("job_delete", "Soft-delete job.", Obj(
                    Req("jobId", "string", "Job GUID.")), "jobId"),

                Tool("story_list_by_job", "List stories by job.", Obj(
                    Req("jobId", "string", "Job GUID.")), "jobId"),
                Tool("story_create", "Create user story.", Obj(
                    Req("jobId", "string", "Job GUID."),
                    Req("title", "string", "Story title."),
                    Req("who", "string", "As a..."),
                    Req("what", "string", "I want..."),
                    Req("why", "string", "So that..."),
                    Req("priority", "integer", "Story priority.")), "jobId", "title", "who", "what", "why", "priority"),
                Tool("story_set_complete", "Set story completion status.", Obj(
                    Req("storyId", "string", "Story GUID."),
                    Req("isComplete", "boolean", "Completion flag.")), "storyId", "isComplete"),
                Tool("story_update_status", "Update story status.", Obj(
                    Req("storyId", "string", "Story GUID."),
                    ReqEnum("status", "Story status.", nameof(StoryStatus.Todo), nameof(StoryStatus.Done))), "storyId", "status"),
                Tool("story_update", "Update story fields.", Obj(
                    Req("storyId", "string", "Story GUID."),
                    Req("title", "string", "Story title."),
                    Req("who", "string", "As a..."),
                    Req("what", "string", "I want..."),
                    Req("why", "string", "So that..."),
                    Req("priority", "integer", "Story priority.")), "storyId", "title", "who", "what", "why", "priority"),

                Tool("criteria_list_by_story", "List criteria by story.", Obj(
                    Req("storyId", "string", "Story GUID.")), "storyId"),
                Tool("criteria_create", "Create acceptance criteria.", Obj(
                    Req("storyId", "string", "Story GUID."),
                    Req("description", "string", "Criteria text.")), "storyId", "description"),
                Tool("criteria_set_met", "Set acceptance criteria status.", Obj(
                    Req("criteriaId", "string", "Criteria GUID."),
                    Req("isMet", "boolean", "Met flag.")), "criteriaId", "isMet"),
                Tool("criteria_check", "Alias for criteria_set_met.", Obj(
                    Req("criteriaId", "string", "Criteria GUID."),
                    Req("isMet", "boolean", "Met flag.")), "criteriaId", "isMet"),
                Tool("criteria_update", "Update acceptance criteria fields.", Obj(
                    Req("criteriaId", "string", "Criteria GUID."),
                    Req("description", "string", "Criteria text."),
                    Req("isMet", "boolean", "Met flag.")), "criteriaId", "description", "isMet"),

                Tool("log_list_by_story", "List progress logs by story.", Obj(
                    Req("storyId", "string", "Story GUID.")), "storyId"),
                Tool("log_add", "Add progress log.", Obj(
                    Req("storyId", "string", "Story GUID."),
                    Req("text", "string", "Log text.")), "storyId", "text")
            }
        };
    }

    private async Task<JsonNode> HandleToolsCallAsync(JsonElement parameters)
    {
        var name = GetRequiredString(parameters, "name");
        var args = GetArguments(parameters);

        object? data = name switch
        {
            "health_check" => await _apiClient.HealthAsync().ConfigureAwait(false),

            "project_list" => await _apiClient.GetProjectsAsync().ConfigureAwait(false),
            "project_create" => await _apiClient.CreateProjectAsync(GetRequiredString(args, "title")).ConfigureAwait(false),
            "project_delete" => await ExecuteNoResultAsync(() => _apiClient.DeleteProjectAsync(GetRequiredString(args, "projectId"))).ConfigureAwait(false),

            "job_list" => await _apiClient.GetJobsAsync(GetOptionalString(args, "projectId")).ConfigureAwait(false),
            "job_get" => await _apiClient.GetJobAsync(GetRequiredString(args, "jobId")).ConfigureAwait(false),
            "job_create" => await _apiClient.CreateJobAsync(new CreateJobRequest(
                ParseGuid(GetRequiredString(args, "projectId"), "projectId"),
                GetRequiredString(args, "shortSlug"),
                GetRequiredString(args, "title"),
                ParseJobType(GetRequiredString(args, "type")),
                GetRequiredString(args, "description"))).ConfigureAwait(false),
            "job_update_status" => await ExecuteNoResultAsync(() => _apiClient.UpdateJobStatusAsync(
                GetRequiredString(args, "jobId"),
                ParseJobStatus(GetRequiredString(args, "status")))).ConfigureAwait(false),
            "job_update" => await ExecuteNoResultAsync(() => _apiClient.UpdateJobAsync(
                GetRequiredString(args, "jobId"),
                new UpdateJobRequest(
                    GetRequiredString(args, "shortSlug"),
                    GetRequiredString(args, "title"),
                    ParseJobType(GetRequiredString(args, "type")),
                    GetRequiredString(args, "description")))).ConfigureAwait(false),
            "job_delete" => await ExecuteNoResultAsync(() => _apiClient.DeleteJobAsync(GetRequiredString(args, "jobId"))).ConfigureAwait(false),

            "story_list_by_job" => await _apiClient.GetStoriesByJobAsync(GetRequiredString(args, "jobId")).ConfigureAwait(false),
            "story_create" => await _apiClient.CreateStoryAsync(new CreateUserStoryRequest(
                ParseGuid(GetRequiredString(args, "jobId"), "jobId"),
                GetRequiredString(args, "title"),
                GetRequiredString(args, "who"),
                GetRequiredString(args, "what"),
                GetRequiredString(args, "why"),
                GetRequiredInt(args, "priority"))).ConfigureAwait(false),
            "story_set_complete" => await ExecuteNoResultAsync(() => _apiClient.SetStoryCompleteAsync(
                GetRequiredString(args, "storyId"),
                GetRequiredBool(args, "isComplete"))).ConfigureAwait(false),
            "story_update_status" => await ExecuteNoResultAsync(() => _apiClient.SetStoryCompleteAsync(
                GetRequiredString(args, "storyId"),
                ParseStoryStatus(GetRequiredString(args, "status")) == StoryStatus.Done)).ConfigureAwait(false),
            "story_update" => await ExecuteNoResultAsync(() => _apiClient.UpdateStoryAsync(
                GetRequiredString(args, "storyId"),
                new UpdateUserStoryRequest(
                    GetRequiredString(args, "title"),
                    GetRequiredString(args, "who"),
                    GetRequiredString(args, "what"),
                    GetRequiredString(args, "why"),
                    GetRequiredInt(args, "priority")))).ConfigureAwait(false),

            "criteria_list_by_story" => await _apiClient.GetCriteriaByStoryAsync(GetRequiredString(args, "storyId")).ConfigureAwait(false),
            "criteria_create" => await _apiClient.CreateCriteriaAsync(new CreateAcceptanceCriteriaRequest(
                ParseGuid(GetRequiredString(args, "storyId"), "storyId"),
                GetRequiredString(args, "description"))).ConfigureAwait(false),
            "criteria_set_met" or "criteria_check" => await ExecuteNoResultAsync(() => _apiClient.SetCriteriaAsync(
                GetRequiredString(args, "criteriaId"),
                GetRequiredBool(args, "isMet"))).ConfigureAwait(false),
            "criteria_update" => await ExecuteNoResultAsync(() => _apiClient.UpdateCriteriaAsync(
                GetRequiredString(args, "criteriaId"),
                new UpdateAcceptanceCriteriaDetailsRequest(
                    GetRequiredString(args, "description"),
                    GetRequiredBool(args, "isMet")))).ConfigureAwait(false),

            "log_list_by_story" => await _apiClient.GetLogsByStoryAsync(GetRequiredString(args, "storyId")).ConfigureAwait(false),
            "log_add" => await _apiClient.AddLogAsync(new CreateProgressLogRequest(
                ParseGuid(GetRequiredString(args, "storyId"), "storyId"),
                GetRequiredString(args, "text"))).ConfigureAwait(false),

            _ => throw new InvalidOperationException($"Unknown tool: {name}")
        };

        return ToolResult(data);
    }

    private static async Task<object> ExecuteNoResultAsync(Func<Task> action)
    {
        await action().ConfigureAwait(false);
        return new { success = true };
    }

    private static JsonNode ToolResult(object? data)
    {
        var serialized = JsonSerializer.Serialize(data, ToolResultJsonOptions);

        var structured = JsonNode.Parse(serialized) ?? new JsonObject();

        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = serialized
                }
            },
            ["structuredContent"] = structured
        };
    }

    private static JsonElement GetArguments(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        if (parameters.TryGetProperty("arguments", out var arguments))
        {
            return arguments;
        }

        if (parameters.TryGetProperty("args", out arguments))
        {
            return arguments;
        }

        return default;
    }

    private static string GetRequiredString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var element))
        {
            throw new InvalidOperationException($"Missing required argument: {name}");
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Argument '{name}' is empty.");
        }

        return value;
    }

    private static string? GetOptionalString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var element))
        {
            return null;
        }

        return element.GetString();
    }

    private static int GetRequiredInt(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var element))
        {
            throw new InvalidOperationException($"Missing required argument: {name}");
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            return number;
        }

        throw new InvalidOperationException($"Argument '{name}' must be integer.");
    }

    private static bool GetRequiredBool(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var element))
        {
            throw new InvalidOperationException($"Missing required argument: {name}");
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException($"Argument '{name}' must be boolean.")
        };
    }

    private static Guid ParseGuid(string value, string argName)
    {
        if (Guid.TryParse(value, out var guid))
        {
            return guid;
        }
        throw new InvalidOperationException($"Argument '{argName}' must be GUID.");
    }

    private static JobStatus ParseJobStatus(string value)
    {
        if (Enum.TryParse<JobStatus>(value, true, out var parsed))
        {
            return parsed;
        }
        throw new InvalidOperationException("status must be one of: Todo, InProgress, Done.");
    }

    private static JobType ParseJobType(string value)
    {
        if (Enum.TryParse<JobType>(value, true, out var parsed))
        {
            return parsed;
        }
        throw new InvalidOperationException("type must be one of: Feature, Fix, Refactor, Chore, Fmt, Doc.");
    }

    private static StoryStatus ParseStoryStatus(string value)
    {
        if (Enum.TryParse<StoryStatus>(value, true, out var parsed))
        {
            return parsed;
        }
        throw new InvalidOperationException("status must be one of: Todo, Done.");
    }

    private static JsonObject Tool(string name, string description, JsonObject inputSchema, params string[] required)
    {
        if (required.Length > 0)
        {
            inputSchema["required"] = new JsonArray(required.Select(static item => JsonValue.Create(item)).ToArray());
        }

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private static JsonObject Obj(params KeyValuePair<string, JsonNode?>[] properties)
    {
        var props = new JsonObject();
        foreach (var (name, schema) in properties)
        {
            props[name] = schema;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props
        };
    }

    private static KeyValuePair<string, JsonNode?> Req(string name, string type, string description)
        => new(name, new JsonObject
        {
            ["type"] = type,
            ["description"] = description
        });

    private static KeyValuePair<string, JsonNode?> ReqEnum(string name, string description, params string[] values)
        => new(name, new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = new JsonArray(values.Select(static item => JsonValue.Create(item)).ToArray())
        });

    private static KeyValuePair<string, JsonNode?> Opt(string name, string type, string description)
        => Req(name, type, description);

    private static bool TryGetId(JsonElement root, out JsonNode? id)
    {
        id = null;
        if (!root.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        id = idElement.ValueKind switch
        {
            JsonValueKind.Number when idElement.TryGetInt64(out var number) => JsonValue.Create(number),
            JsonValueKind.String => JsonValue.Create(idElement.GetString()),
            JsonValueKind.Null => null,
            _ => JsonValue.Create(idElement.GetRawText())
        };

        return true;
    }

    private async Task<byte[]?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var firstLine = await ReadHeaderLineAsync(cancellationToken).ConfigureAwait(false);
        if (firstLine is null)
        {
            return null;
        }

        if (IsJsonLine(firstLine))
        {
            _useLineDelimitedJsonRpc = true;
            return await ReadJsonLineMessageAsync(firstLine, cancellationToken).ConfigureAwait(false);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddHeader(headers, firstLine);
        while (true)
        {
            var line = await ReadHeaderLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            if (IsJsonLine(line))
            {
                _useLineDelimitedJsonRpc = true;
                return await ReadJsonLineMessageAsync(line, cancellationToken).ConfigureAwait(false);
            }

            AddHeader(headers, line);
        }

        if (!headers.TryGetValue("Content-Length", out var lengthValue) || !int.TryParse(lengthValue, out var length) || length < 0)
        {
            throw new InvalidOperationException("Invalid Content-Length header.");
        }

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await _input.ReadAsync(body.AsMemory(read, length - read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }
            read += count;
        }

        return body;
    }

    private static bool IsJsonLine(string line)
    {
        var trimmed = line.AsSpan().TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    private static void AddHeader(Dictionary<string, string> headers, string line)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return;
        }

        var name = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();
        headers[name] = value;
    }

    private async Task<byte[]?> ReadJsonLineMessageAsync(string firstLine, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(firstLine.Trim());

        while (true)
        {
            var candidate = builder.ToString();
            if (TryParseJson(candidate))
            {
                return Encoding.UTF8.GetBytes(candidate);
            }

            var nextLine = await ReadHeaderLineAsync(cancellationToken).ConfigureAwait(false);
            if (nextLine is null)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading JSON line message.");
            }

            builder.AppendLine(nextLine);
        }
    }

    private static bool TryParseJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<string?> ReadHeaderLineAsync(CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(64);
        while (true)
        {
            var buffer = new byte[1];
            var read = await _input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray());
            }

            var current = buffer[0];
            if (current == (byte)'\n')
            {
                if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }
                return Encoding.UTF8.GetString(bytes.ToArray());
            }

            bytes.Add(current);
        }
    }

    private async Task WriteResultAsync(JsonNode? id, JsonNode result, CancellationToken cancellationToken)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["id"] = id,
            ["result"] = result
        };

        await WriteMessageAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteErrorAsync(JsonNode? id, int code, string message, CancellationToken cancellationToken)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        await WriteMessageAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);

        if (_useLineDelimitedJsonRpc)
        {
            await _output.WriteAsync(payload.AsMemory(0, payload.Length), cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(new byte[] { (byte)'\n' }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
            await _output.WriteAsync(header.AsMemory(0, header.Length), cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(payload.AsMemory(0, payload.Length), cancellationToken).ConfigureAwait(false);
        }

        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
