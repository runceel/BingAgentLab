using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", true)
    .Build();

var kernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(
        configuration["AOAI:ModelDeploymentName"]!,
        configuration["AOAI:Endpoint"]!,
        new AzureCliCredential());
