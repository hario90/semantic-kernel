// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Planning;
using RepoUtils;

// ReSharper disable CommentTypo
// ReSharper disable once InconsistentNaming
internal static class Example99_SequentialPlannerImprovements
{
    public static async Task RunAsync()
    {
        await PoetrySamplesAsync();
    }

    // TODO - change Skills to Plugins everywhere? (new code)
    private static async Task PoetrySamplesAsync()
    {
        Console.WriteLine("======== Sequential Planner - Create and Execute Poetry Plan ========");
        string serviceId = TestConfiguration.AzureOpenAI.ServiceId;
        string apiKey = TestConfiguration.AzureOpenAI.ApiKey;
        string deploymentName = TestConfiguration.AzureOpenAI.ChatDeploymentName;
        string endpoint = TestConfiguration.AzureOpenAI.Endpoint;

        if (serviceId == null || apiKey == null || deploymentName == null || endpoint == null)
        {
            Console.WriteLine("Azure serviceId, endpoint, apiKey, or deploymentName not found. Skipping example.");
            return;
        }

        var kernel = new KernelBuilder()
            .WithLoggerFactory(ConsoleLogger.LoggerFactory)
            .WithAzureChatCompletionService(
                TestConfiguration.AzureOpenAI.ChatDeploymentName,
                TestConfiguration.AzureOpenAI.Endpoint,
                TestConfiguration.AzureOpenAI.ApiKey)
            //.WithAzureTextCompletionService(
            //    TestConfiguration.AzureOpenAI.DeploymentName,
            //    TestConfiguration.AzureOpenAI.Endpoint,
            //    TestConfiguration.AzureOpenAI.ApiKey)
            .Build();

        string folder = RepoFiles.SampleSkillsPath();
        kernel.ImportSemanticSkillFromDirectory(folder,
            "SummarizeSkill",
            "WriterSkill");
        var selectSkillsPrompt = @"
Select the skills that are relevant to the goal given.
[AVAILABLE SKILLS]

{{$available_skills}}

[END AVAILABLE SKILLS]

Output only the skill names on one line, delimited by commas.
Begin!

<goal>{{$input}}</goal>

";
        var planner = new SequentialPlanner(kernel, selectSkillsPrompt: selectSkillsPrompt);
        var goal = "Write a poem about John Doe, then translate it into Italian.";

        Stopwatch sw = Stopwatch.StartNew();

        var plan = await planner.CreatePlanAsync(goal);

        sw.Stop();
        Console.WriteLine($"Old Create Plan took {sw.Elapsed.TotalMilliseconds} milliseconds");

        // Original plan:
        // Goal: Write a poem about John Doe, then translate it into Italian.

        // Steps:
        // - WriterSkill.ShortPoem INPUT='John Doe is a friendly guy who likes to help others and enjoys reading books.' =>
        // - WriterSkill.Translate language='Japanese' INPUT='' =>

        Console.WriteLine("Original plan:");
        Console.WriteLine(plan.ToPlanWithGoalString());

        var result = await kernel.RunAsync(plan);

        Console.WriteLine("Result:");
        Console.WriteLine(result.Result);

        sw.Restart();

        var plan2 = await planner.CreatePlan2Async(goal);

        sw.Stop();
        Console.WriteLine($"New Create Plan took {sw.Elapsed.TotalMilliseconds} milliseconds");

        Console.WriteLine("Selected Skills:");
        Console.WriteLine(plan2);
        //Console.WriteLine("New plan:");
        //Console.WriteLine(plan2.ToPlanWithGoalString());

        //var result2 = await kernel.RunAsync(plan2);

        //Console.WriteLine("Result:");
        //Console.WriteLine(result2.Result);
    }
}
