using System.Text;
using Aist.Mcp;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

using var apiClient = new AistApiClient();
var server = new McpServer(Console.OpenStandardInput(), Console.OpenStandardOutput(), apiClient);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await server.RunAsync(cts.Token).ConfigureAwait(false);
