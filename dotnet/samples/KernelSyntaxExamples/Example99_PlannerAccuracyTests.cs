// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Planning.Sequential;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel;
using RepoUtils;
using Microsoft.SemanticKernel.Memory;
using System.Diagnostics;
using Microsoft.SemanticKernel.Skills.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class Example99_PlannerAccuracyTests
{
    public static async Task RunAsync()
    {
        await RunCreatePlanComparisons();
    }

    private static async Task RunCreatePlanComparisons()
    {
        Console.WriteLine("======== Sequential Planner - Speed tests ========");
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
        //Example99_PlannerSpeedTests.ImportSkills(kernel);

        var planner = new SequentialPlanner(kernel, config: new SequentialPlannerConfig { RelevancyThreshold = 0.65, Memory = kernel.Memory, AllowMissingFunctions = true });

        string[] goals = {
            "Summarize a conversation and anonymize the personal information",
        };

        var results = new double[goals.Length];
        var rounds = 5;
        for (int round = 0; round < rounds; round++)
        {
            Console.WriteLine($"Round {round}--------------");
            for (int i = 0; i < goals.Length; i++)
            {
                Console.ForegroundColor = ConsoleColor.White;
                var goal = goals[i];

                Console.WriteLine("---------------------------");
                Console.WriteLine($"Goal: {goal}");
                Stopwatch sw = Stopwatch.StartNew();

                Console.WriteLine("Running old create plan........");
                var plan = await planner.CreatePlanAsync(goal, FunctionManual1);

                sw.Stop();
                Console.Write($"Old Create Plan took: ");
                Example99_PlannerAccuracyTests.WriteResult($"{sw.Elapsed.TotalMilliseconds}ms");
                double originalCreatePlanDuration = sw.Elapsed.TotalMilliseconds;

                Console.WriteLine("Original plan:");
                Console.WriteLine(plan.ToPlanString());
                Console.WriteLine();

                await Example99_PlannerAccuracyTests.RunNewCreatePlan(goal, planner, sw, originalCreatePlanDuration, plan, FunctionManual2, results, i);
            }
        }
        var averageDifferences = results.ToList().Select(sum => sum / rounds).ToArray();
        Console.WriteLine("Summary ------------------------------");
        for (int i = 0; i < goals.Length; i++)
        {
            var goal = goals[i];
            var diff = averageDifferences[i];
            var fasterSlower = diff < 0 ? "slower" : "faster";
            Console.WriteLine($"For Goal: {goal}, the new planner was {Math.Abs(diff)}ms {fasterSlower} than the old plan (on average).");
        }
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

    private static async Task RunNewCreatePlan(string goal, SequentialPlanner planner, Stopwatch sw, double oldDuration, Plan oldPlan, string functionManual, double[] results, int i)
    {
        Console.WriteLine("Running new create plan........");
        var newPlan = await Example99_PlannerAccuracyTests.RunNewPlan(goal, planner, sw, functionManual);

        Console.Write($"New planner took: ");
        Example99_PlannerAccuracyTests.WriteResult($"{newPlan.Duration}ms");

        Example99_PlannerAccuracyTests.PrintAccuracyResults(oldPlan, newPlan.Plan);

        Example99_PlannerAccuracyTests.PrintSpeedResults(oldDuration, newPlan.Duration, results, i);
    }

    private static async Task<(double Duration, Plan Plan)> RunNewPlan(string goal, SequentialPlanner planner, Stopwatch sw, string functionManual)
    {
        sw.Restart();
        var plan = await planner.CreatePlanAsync(goal, functionManual);

        sw.Stop();

        return (Duration: sw.Elapsed.TotalMilliseconds, Plan: plan);
    }

    private static void PrintAccuracyResults(Plan originalPlan, Plan newPlan)
    {
        var plansMatch = Example99_PlannerAccuracyTests.PlansEqual(originalPlan, newPlan);
        Console.Write("Plans match? ");
        Example99_PlannerAccuracyTests.WritePassed(plansMatch);

        if (!plansMatch)
        {
            Console.WriteLine("New Plan: ");
            Console.WriteLine(newPlan.ToPlanString());
        }
    }

    private static void PrintSpeedResults(double original, double newSpeed, double[] results, int i)
    {
        Console.Write("New plan was faster? ");
        Example99_PlannerAccuracyTests.WritePassed(newSpeed < original);
        results[i] += original - newSpeed;
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
        var allSampleSkills = Example99_PlannerAccuracyTests.GetSampleSkillNames(folder);
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
    }

    private const string FunctionManual1 = @"
MiscSkill.MakeBriefer:
  description: I don't feel like being helpful.
  inputs:
    - input: a transcript.


ChatSkill.PIIExtractor:

    Description: Still don't feel helpful.
    Inputs:
        message: The chat message to be anonymized.
";

    private const string FunctionManual2 = @"
MiscSkill:
  description: Contains skills for summarizing conversations
ChatSkill:
  description: contains skills for anonymizing text to remove personal information.

MiscSkill.MakeBriefer:
  description: I don't feel like being helpful
  inputs:
    - input: a transcript.


ChatSkill.PIIExtractor:

    Description: Still don't feel helpful.
    Inputs:
        message: The chat message to be anonymized.
";
}
