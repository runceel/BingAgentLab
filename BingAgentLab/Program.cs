// 一部 Semantic Kernel のプレビュー機能を使うため、その警告の抑止を行っています。
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.AzureAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", true)
    .Build();

var kernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(
        configuration["AOAI:ModelDeploymentName"]!,
        configuration["AOAI:Endpoint"]!,
        new AzureCliCredential())
    .Build();

// プラグインを登録
kernel.Plugins.AddFromObject(new BingPlugin(configuration));

var catAgent = new ChatCompletionAgent
{
    Name = "Cat",
    Instructions = """
        あなたは猫型アシスタントです。猫っぽい言葉遣いで話してください。
        """,
    Kernel = kernel,
    // 自動的にプラグインを呼び出すように設定
    Arguments = new(new AzureOpenAIPromptExecutionSettings
    {
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
    }),
};

ChatHistory history = [
    new ChatMessageContent(AuthorRole.User, "最新の .NET のプレビューバージョンについて教えて"),
];
await foreach (var message in catAgent.InvokeAsync(history))
{
    Console.WriteLine($"{message.AuthorName}: {message.Content}");
}


// Bing 検索をプラグイン化
class BingPlugin(IConfiguration configuration)
{
    [KernelFunction, Description("Bing 検索を使用した回答を返します。")]
    public async Task<ChatMessageContent> BingSearchAsync(
        [Description("知りたいこと")] string question,
        [Description("検索結果の最大数")] int maxResults = 10)
    {
        // Bing を使う Agent を作成開始！
        // AI Foundary のプロジェクトを取得
        var projectClient = AzureAIAgent.CreateAzureAIClient(
            configuration["AgentService:ConnectionString"]!,
            new AzureCliCredential());
        // AgentClient を取得
        var agentsClient = projectClient.GetAgentsClient();

        // Bing に接続するための Connection を取得
        var bingConnection = await projectClient.GetConnectionsClient()
            .GetConnectionAsync(configuration["AgentService:BingConnectionId"]!);
        // Bing を使う Agent を作成
        var bingAgentDefinition = await agentsClient.CreateAgentAsync(
            configuration["AOAI:ModelDeploymentName"]!,
            name: "Bing",
            description: "Bing で検索するエージェント",
            instructions: """
                あなたは Bing で検索を行い回答をするエージェントです。
                """,
            tools: [
                new BingGroundingToolDefinition(new()
                {
                    ConnectionList = { new(bingConnection.Value.Id) },
                })
            ]);
        // Semantic Kernel の Agent でラップ
        AzureAIAgent bingAgent = new(bingAgentDefinition, agentsClient);

        // Bing で検索を考慮した回答を取得する
        var thread = await agentsClient.CreateThreadAsync();
        await bingAgent.AddChatMessageAsync(
            thread.Value.Id,
            new ChatMessageContent(AuthorRole.User, $"{question}。回答の後に {maxResults} 件の引用元タイトルとURLの一覧を出してください。"));
        var result = await bingAgent.InvokeAsync(thread.Value.Id).FirstOrDefaultAsync();
        return result ?? new ChatMessageContent(AuthorRole.Assistant, "見つかりませんでした。");
    }
}
