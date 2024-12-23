using System.CommandLine;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

#nullable disable
#nullable enable annotations

namespace AzDevAgentRunner;

public abstract class TaskOperationBase(IConsole Console)
{
    public required string TaskUrl;
    public required string AdoToken;

    public double PollSeconds = 1;

    public bool Verbose = false;

    protected BuildUri adoBuildUri;
    protected TaskInfo taskInfo;
    protected VssConnection connection;
    protected BuildHttpClient client;
    protected TaskHttpClient taskClient;
    protected TaskAgentHttpClient agentClient;
    protected Build build;

    public async Task<int> RunAsync()
    {
        await InitilializeAsync();

        return await RunCoreAsync();
    }

    protected abstract Task<int> RunCoreAsync();

    private async Task InitilializeAsync()
    {
        adoBuildUri = BuildUri.ParseBuildUri(TaskUrl);
        taskInfo = adoBuildUri.DeserializeFromParameters<TaskInfo>();

        connection = new VssConnection(adoBuildUri.OrganizationUri, new VssBasicCredential(AdoToken, string.Empty));
        client = connection.GetClient<BuildHttpClient>();
        taskClient = connection.GetClient<TaskHttpClient>();
        agentClient = connection.GetClient<TaskAgentHttpClient>();

        build = await client.GetBuildAsync(adoBuildUri.Project, adoBuildUri.BuildId);
    }

    protected Task<PropertiesCollection> GetBuildProperties() =>
        client.GetBuildPropertiesAsync(build.Project.Id, build.Id);

    protected Task SetBuildProperties(PropertiesCollection properties) =>
        client.UpdateBuildPropertiesAsync(properties, build.Project.Id, build.Id);

    protected enum FileEnvVar
    {
        GITHUB_OUTPUT,
        GITHUB_ENV
    }

    protected enum OutputNames
    {
        hasMoreJobs
    }

    protected void AppendLinesToEnvFile(FileEnvVar file, params string[] lines)
    {
        Console.WriteLine($"Writing: {string.Join(Environment.NewLine, [file.ToString(), .. lines])}");
        if (Environment.GetEnvironmentVariable(file.ToString()) is string fileName && !string.IsNullOrEmpty(fileName))
        {
            File.AppendAllLines(fileName, lines);
        }
    }

    protected bool IsCompleted(Build build)
    {
        var result = build.Result;
        return result != null && result != BuildResult.None;
    }
}
