// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.Planning.Sequential;
using Microsoft.SemanticKernel.Skills.Core;
using RepoUtils;
using Skills;

// ReSharper disable CommentTypo
// ReSharper disable once InconsistentNaming
internal static class Example99_SequentialPlannerImprovements
{
    public static async Task RunAsync()
    {
        await RunCreatePlanComparisons();
    }

    private static List<string> GetSampleSkillNames(string folderPath)
    {
        var skills = new List<string>();
        string[] subdirectories = Directory.GetDirectories(folderPath);
        foreach (string subdirectoryPath in subdirectories)
        {
            skills.Add(Path.GetFileName(subdirectoryPath));
        }
        return skills;
    }

    private static void ImportSkills(IKernel kernel)
    {
        string folder = RepoFiles.SampleSkillsPath();
        var allSampleSkills = Example99_SequentialPlannerImprovements.GetSampleSkillNames(folder);
        kernel.ImportSemanticSkillFromDirectory(folder,
            allSampleSkills.ToArray());
        kernel.ImportSkill(new MathSkill(), "MathSkill");
        kernel.ImportSkill(new ConversationSummarySkill(kernel), "ConversationSummarySkill");
        kernel.ImportSkill(new FileIOSkill(), "FileIOSkill");
        kernel.ImportSkill(new HttpSkill(), "HttpSkill");
        kernel.ImportSkill(new TextMemorySkill(NullMemory.Instance), "TextMemorySkill");
        kernel.ImportSkill(new TextSkill(), "TextSkill");
        kernel.ImportSkill(new TimeSkill(), "TimeSkill");
        kernel.ImportSkill(new WaitSkill(), "WaitSkill");
        kernel.ImportSkill(new ConsoleSkill(), "ConsoleSkill");
    }

    private static void WritePassed(bool passed)
    {
        var color = passed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.ForegroundColor = color;
        Console.Write(passed);
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
    }

    private static void WriteResult(string result, ConsoleColor color = ConsoleColor.Yellow)
    {
        Console.ForegroundColor = color;
        Console.Write(result);
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
    }

    private static async Task<(double Duration, Plan Plan)> RunNewPlan(string goal, SequentialPlanner planner, Stopwatch sw)
    {
        sw.Restart();
        var plan = await planner.CreatePlan2Async(goal);

        sw.Stop();

        return (Duration: sw.Elapsed.TotalMilliseconds, Plan: plan);
    }

    private static bool PlansEqual(Plan plan1, Plan plan2)
    {
        if (plan1.Steps.Count != plan2.Steps.Count) return false;

        for (int i = 0; i < plan1.Steps.Count; i++)
        {
            var step1 = plan1.Steps[i];
            var step2 = plan2.Steps[i];
            if (step1.SkillName != step2.SkillName) return false;
        }
        return true;
    }

    private static void PrintAccuracyResults(Plan originalPlan, Plan newPlan)
    {
        var plansMatch = Example99_SequentialPlannerImprovements.PlansEqual(originalPlan, newPlan); // originalPlan.ToPlanString() == newPlan.ToPlanString();
        Console.Write("Plans match? ");
        Example99_SequentialPlannerImprovements.WritePassed(plansMatch);

        if (!plansMatch)
        {
            Console.WriteLine("New Plan: ");
            Console.WriteLine(newPlan.ToPlanString());
        }
    }

    private static void PrintSpeedResults(double original, double newSpeed)
    {
        Console.Write("New plan was faster? ");
        Example99_SequentialPlannerImprovements.WritePassed(newSpeed < original);
    }

    private static async Task RunNewCreatePlan(string goal, SequentialPlanner planner, Stopwatch sw, double oldDuration, Plan oldPlan, string planType)
    {
        var newPlan = await Example99_SequentialPlannerImprovements.RunNewPlan(goal, planner, sw);

        Console.Write($"New {planType} took: ");
        Example99_SequentialPlannerImprovements.WriteResult($"{newPlan.Duration}ms");

        Example99_SequentialPlannerImprovements.PrintAccuracyResults(oldPlan, newPlan.Plan);

        Example99_SequentialPlannerImprovements.PrintSpeedResults(oldDuration, newPlan.Duration);
    }

    private static async Task RunCreatePlanComparisons()
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
            .WithAzureTextEmbeddingGenerationService(
                TestConfiguration.AzureOpenAIEmbeddings.DeploymentName,
                TestConfiguration.AzureOpenAIEmbeddings.Endpoint,
                TestConfiguration.AzureOpenAIEmbeddings.ApiKey)
            .WithMemoryStorage(new VolatileMemoryStore())
            .WithAzureChatCompletionService(
                TestConfiguration.AzureOpenAI.ChatDeploymentName,
                TestConfiguration.AzureOpenAI.Endpoint,
                TestConfiguration.AzureOpenAI.ApiKey)
            .Build();
        Example99_SequentialPlannerImprovements.ImportSkills(kernel);
        var planner = new SequentialPlanner(kernel, config: new SequentialPlannerConfig { RelevancyThreshold = 0.65, Memory = kernel.Memory });
        var plannerWithSkillFiltererFunctionEnabled = new SequentialPlanner(kernel, config: new SequentialPlannerConfig { RelevancyThreshold = 0.65, Memory = kernel.Memory, UseSemanticFunctionForFunctionLookup = true });
        string[] goals = {
            "Write a poem about John Doe, then translate it into Italian.",
            "Get the sum of 5 and 14, then log just the result to the console.",
            "Concat the text '5 - 14 = ' with the difference of 5 and 14.",
            "Given a conversation log, write a poem containing the action items from the conversation.",
            "Send a GET request to https://en.wikipedia.org/wiki/Tree and summarize the response body",
            "Summarize a conversation and generate a list of topics from the summary"
        };
        double originalCreatePlanDuration = 0;
        double newCreatePlanWithMemoryDuration = 0;
        double newCreatePlanWithSemanticFunctionDuration = 0;

        for (int i = 0; i < goals.Length; i++)
        {
            Console.ForegroundColor = ConsoleColor.White;
            var goal = goals[i];
            Console.WriteLine("---------------------------");
            Console.WriteLine($"Goal: {goal}");
            Stopwatch sw = Stopwatch.StartNew();

            Console.WriteLine("Running old create plan........");
            var plan = await planner.CreatePlanAsync(goal);

            sw.Stop();
            Console.Write($"Old Create Plan took: ");
            Example99_SequentialPlannerImprovements.WriteResult($"{sw.Elapsed.TotalMilliseconds}ms");
            originalCreatePlanDuration = sw.Elapsed.TotalMilliseconds;

            Console.WriteLine("Original plan:");
            Console.WriteLine(plan.ToPlanString());

            //var result = await kernel.RunAsync(plan);

            //Console.WriteLine("Result:");
            //Console.WriteLine(result.Result);

            Console.WriteLine();
            Console.WriteLine("Running create plan using semantic memory to filter skills........");

            await Example99_SequentialPlannerImprovements.RunNewCreatePlan(goal, planner, sw, originalCreatePlanDuration, plan, "Create plan using memory for skill filtering");

            Console.WriteLine();
            Console.WriteLine("Running create plan using semantic function to filter skills........");

            await Example99_SequentialPlannerImprovements.RunNewCreatePlan(goal, plannerWithSkillFiltererFunctionEnabled, sw, originalCreatePlanDuration, plan, "Create plan using semantic function for skill filtering");
            //var result2 = await kernel.RunAsync(plan2);

            //Console.WriteLine("Result:");
            //Console.WriteLine(result2.Result);
        }
    }
}
