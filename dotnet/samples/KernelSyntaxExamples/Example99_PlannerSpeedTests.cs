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

public static class Example99_PlannerSpeedTests
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
            "Write a poem about John Doe, then translate it into Italian.",
            "Given a conversation log, write a poem containing the action items from the conversation.",
            "Send a GET request to https://en.wikipedia.org/wiki/Tree and summarize the response body",
            "Summarize a conversation and anonymize the personal information",
            "Create a children's book about a dog named Dot and publish",
            "Convert 'print hello five times' to Python code and execute the code",
            //"Write a chapter of a novel about a dog named Dot and provide a synopses of the chapter"
            // "Get the sum of 5 and 14, then log just the result to the console.",
            //"Concat the text '5 - 14 = ' with the difference of 5 and 14.",
        };

        string[] functionManuals =
        {
            FunctionManual1,
            FunctionManual2,
            FunctionManual3,
            FunctionManual4,
            FunctionManual5,
            FunctionManual6
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
                var functionManual = functionManuals[i];

                Console.WriteLine("---------------------------");
                Console.WriteLine($"Goal: {goal}");
                Stopwatch sw = Stopwatch.StartNew();

                Console.WriteLine("Running old create plan........");
                var plan = await planner.CreatePlanAsync(goal, OriginalFunctionManual);

                sw.Stop();
                Console.Write($"Old Create Plan took: ");
                Example99_PlannerSpeedTests.WriteResult($"{sw.Elapsed.TotalMilliseconds}ms");
                double originalCreatePlanDuration = sw.Elapsed.TotalMilliseconds;

                Console.WriteLine("Original plan:");
                Console.WriteLine(plan.ToPlanString());
                Console.WriteLine();

                await Example99_PlannerSpeedTests.RunNewCreatePlan(goal, planner, sw, originalCreatePlanDuration, plan, functionManual, results, i);
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
        var newPlan = await Example99_PlannerSpeedTests.RunNewPlan(goal, planner, sw, functionManual);

        Console.Write($"New planner took: ");
        Example99_PlannerSpeedTests.WriteResult($"{newPlan.Duration}ms");

        Example99_PlannerSpeedTests.PrintAccuracyResults(oldPlan, newPlan.Plan);

        Example99_PlannerSpeedTests.PrintSpeedResults(oldDuration, newPlan.Duration, results, i);
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
        var plansMatch = Example99_PlannerSpeedTests.PlansEqual(originalPlan, newPlan);
        Console.Write("Plans match? ");
        Example99_PlannerSpeedTests.WritePassed(plansMatch);

        if (!plansMatch)
        {
            Console.WriteLine("New Plan: ");
            Console.WriteLine(newPlan.ToPlanString());
        }
    }

    private static void PrintSpeedResults(double original, double newSpeed, double[] results, int i)
    {
        Console.Write("New plan was faster? ");
        Example99_PlannerSpeedTests.WritePassed(newSpeed < original);
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
        var allSampleSkills = Example99_PlannerSpeedTests.GetSampleSkillNames(folder);
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
WriterSkill.ShortPoem:
  description: Turn a scenario into a short and entertaining poem.
  inputs:
    - input: The scenario to turn into a poem.

WriterSkill.StoryGen:
  description: Generate a list of synopsis for a novel or novella with sub-chapters
  inputs:


WriterSkill.TellMeMore:
  description: Summarize given text or any text document
  inputs:


WriterSkill.Translate:
  description: Translate the input into a language of your choice
  inputs:";

    private const string FunctionManual2 = @"
WriterSkill.ShortPoem:
  description: Turn a scenario into a short and entertaining poem.
  inputs:
    - input: The scenario to turn into a poem.


ConversationSummarySkill.func49e4ac9b0fbc4663bbeba0bbfcfd7232:
  description: Analyze a conversation transcript and extract key topics worth remembering.
  inputs:


ConversationSummarySkill.func6f523e05a7e54ba8b256a0457b6be413:
  description: Given a section of a conversation transcript, summarize the part of the conversation.
  inputs:


ConversationSummarySkill.GetConversationActionItems:
  description: Given a long conversation transcript, identify action items.
  inputs:
    - input: A long conversation transcript.

ConversationSummarySkill.GetConversationTopics:
  description: Given a long conversation transcript, identify topics worth remembering.
  inputs:
    - input: A long conversation transcript.

ConversationSummarySkill.SummarizeConversation:
  description: Given a long conversation transcript, summarize the conversation.
  inputs:
    - input: A long conversation transcript.
";
    private const string FunctionManual3 = @"
HttpSkill.Get:
  description: Makes a GET request to a uri
  inputs:
    - uri: The URI of the request


HttpSkill.Ping:

    Description: Send a ping request to check the availability of a server or website.
    Inputs:
        url: The URL to ping.


SummarizeSkill.Summarize:
  description: Summarize given text or any text document
  inputs:
    - input: Text to summarize
";

    private const string FunctionManual4 = @"
ConversationSummarySkill.SummarizeConversation:
  description: Given a long conversation transcript, summarize the conversation.
  inputs:
    - input: A long conversation transcript.


ChatSkill.ChatAnonymizer:

    Description: Anonymize chat messages by replacing personal information with placeholders.
    Inputs:
        message: The chat message to be anonymized.
";

    private const string FunctionManual5 = @"
ChildrensBookSkill.CreateBook:
  description: Creates a children's book from the given input with a suggested number of words per page and a specific total number of pages
  inputs:


ChildrensBookSkill.PublishBook:

    Description: Publish a children's book created using the ChildrensBookSkill.CreateBook function.
    Inputs:
        book_content: The content of the children's book to be published.
";

    private const string FunctionManual6 = @"
CodingSkill.CodePython:
  description: Turns natural language into Python code like a Python Copilot.
  inputs:


CodingSkill.CommandLinePython:
  description: Turns natural language into Python command line scripts. Reads variables from args, operates on stdin, out
  inputs:


CodingSkill.DOSScript:
  description: Turns your intent into a SAFE DOS batch script
  inputs:


CodingSkill.ExecutePython:
  description: Executes python code.
  inputs:
    code: String containing python 
";
    private const string OriginalFunctionManual = @"
ChatSkill.Chat:
  description: Chat with the AI
  inputs:


ChatSkill.ChatAnonymizer:

    Description: Anonymize chat messages by replacing personal information with placeholders.
    Inputs:
        message: The chat message to be anonymized.


ChatSkill.ChatFilter:
  description: Given a chat message decide whether to block it
  inputs:


ChatSkill.ChatGPT:
  description:
  inputs:


ChatSkill.ChatModeration:

    Description: Filter and moderate chat messages for inappropriate content.
    Inputs:
        message: The chat message to be moderated.


ChatSkill.ChatTranslate:

    Description: Translate chat messages between different languages.
    Inputs:
        message: The chat message to be translated.
        source_language: The source language of the message.
        target_language: The target language for translation.


ChatSkill.ChatSentimentAnalysis:

    Description: Analyze the sentiment of a chat message (e.g., positive, negative, neutral).
    Inputs:
        message: The chat message for sentiment analysis.


ChatSkill.ChatUser:
  description: A chat bot that plays a persona or role
  inputs:


ChatSkill.ChatV2:
  description: A friendly chat where AI helps, avoiding bad topics
  inputs:


ChildrensBookSkill.BookIdeas:
  description: Given a topic description generate a number of children's book ideas with short descriptions
  inputs:


ChildrensBookSkill.CreateBook:
  description: Creates a children's book from the given input with a suggested number of words per page and a specific total number of pages
  inputs:


ChildrensBookSkill.PublishBook:

    Description: Publish a children's book created using the ChildrensBookSkill.CreateBook function.
    Inputs:
        book_content: The content of the children's book to be published.


ClassificationSkill.Importance:
  description: Tell you the urgency level of the given text
  inputs:


ClassificationSkill.Question:
  description: Tells you the sentence type (i.e. Question or Statement) of a given sentence
  inputs:


CodingSkill.Code:
  description: Turn natural language into code
  inputs:


CodingSkill.CodePython:
  description: Turns natural language into Python code like a Python Copilot.
  inputs:


CodingSkill.CommandLinePython:
  description: Turns natural language into Python command line scripts. Reads variables from args, operates on stdin, out
  inputs:


CodingSkill.DOSScript:
  description: Turns your intent into a SAFE DOS batch script
  inputs:


CodingSkill.ExecutePython:
  description: Executes python code.
  inputs:
    code: String containing python 


CodingSkill.EmailSearch:
  description: Search the Microsoft Graph for Email
  inputs:


CodingSkill.Entity:
  description: Given text, annotate all recognized entities. You specify the tags to use.
  inputs:


ConversationSummarySkill.func49e4ac9b0fbc4663bbeba0bbfcfd7232:
  description: Analyze a conversation transcript and extract key topics worth remembering.
  inputs:


ConversationSummarySkill.func6f523e05a7e54ba8b256a0457b6be413:
  description: Given a section of a conversation transcript, summarize the part of the conversation.
  inputs:


ConversationSummarySkill.GetConversationActionItems:
  description: Given a long conversation transcript, identify action items.
  inputs:
    - input: A long conversation transcript.

ConversationSummarySkill.GetConversationTopics:
  description: Given a long conversation transcript, identify topics worth remembering.
  inputs:
    - input: A long conversation transcript.

ConversationSummarySkill.SummarizeConversation:
  description: Given a long conversation transcript, summarize the conversation.
  inputs:
    - input: A long conversation transcript.

FileIOSkill.Read:
  description: Read a file
  inputs:
    - path: Source file

FileIOSkill.Write:
  description: Write a file
  inputs:
    - path: Destination file
  - content: File content

FunSkill.Excuses:
  description: Turn a scenario into a creative or humorous excuse to send your boss
  inputs:


FunSkill.Joke:
  description: Generate a funny joke
  inputs:
    - input: Joke subject
  - style: Give a hint about the desired joke style

FunSkill.Limerick:
  description: Generate a funny limerick about a person
  inputs:
    - name:  (default value: Bob)
  - input:  (default value: Dogs)

FunSkill.RiddleGenerator:

    Description: Generate a challenging riddle for entertainment.
    Inputs:
        riddle_description: Description or theme for the riddle.

GroundingSkill.ExciseEntities:
  description: Remove a list of ungrounded entities from a given text in a coherent manner. Returns the input text without the ungrounded entities in the list
  inputs:
    - input: The text from which the entities are to be removed
  - ungrounded_entities: The entities to remove. This is a list of strings.

GroundingSkill.ExtractEntities:
  description: Extract entities related to a specified topic from the supplied input text. Returns the entities and the source text
  inputs:
    - input: The text from which the entities are to be extracted
  - topic: The topic of interest; the extracted entities should be related to this topic
  - example_entities: A list of example entities from the topic. This can help guide the entity extraction

HttpSkill.Delete:
  description: Makes a DELETE request to a uri
  inputs:
    - uri: The URI of the request

HttpSkill.Get:
  description: Makes a GET request to a uri
  inputs:
    - uri: The URI of the request


HttpSkill.Ping:

    Description: Send a ping request to check the availability of a server or website.
    Inputs:
        url: The URL to ping.


HttpSkill.Post:
  description: Makes a POST request to a uri
  inputs:
    - uri: The URI of the request
  - body: The body of the request

HttpSkill.Put:
  description: Makes a PUT request to a uri
  inputs:
    - uri: The URI of the request
  - body: The body of the request

IntentDetectionSkill.AssistantIntent:
  description: Given a query and a list of possible intents, detect which intent the input matches
  inputs:


MathSkill.Add:
  description: Adds an amount to a value
  inputs:
    - value: The value to add
  - amount: Amount to add

MathSkill.Subtract:
  description: Subtracts an amount from a value
  inputs:
    - value: The value to subtract
  - amount: Amount to subtract

MiscSkill.Continue:
  description: Given a text input, continue it with additional text.
  inputs:
    - input: The text to continue.

MiscSkill.ElementAtIndex:
  description: Get an element from an array at a specified index
  inputs:
    - input: The input array
  - index: The index of the element to retrieve
  - count: The number of items in the input

QASkill.AssistantResults:
  description:
  inputs:


QASkill.ContextQuery:
  description: Ask the AI for answers contextually relevant to you based on your name, address and pertinent information retrieved from your personal secondary memory
  inputs:


QASkill.Form:
  description:
  inputs:


QASkill.GitHubMemoryQuery:
  description:
  inputs:


QASkill.QNA:
  description: Ask AI for a list of question and answers based on text source
  inputs:


QASkill.Question:
  description: Answer any question
  inputs:


SummarizeSkill.MakeAbstractReadable:
  description: Given a scientific white paper abstract, rewrite it to make it more readable
  inputs:


SummarizeSkill.Notegen:
  description: Automatically generate compact notes for any text or text document.
  inputs:


SummarizeSkill.Summarize:
  description: Summarize given text or any text document
  inputs:
    - input: Text to summarize

SummarizeSkill.Topics:
  description: Analyze given text or document and extract key topics worth remembering
  inputs:


TextMemorySkill.Recall:
  description: Semantic search and return up to N memories related to the input text
  inputs:
    - input: The input text to find related memories for
  - collection: Memories collection to search (default value: generic)
  - relevance: The relevance score, from 0.0 to 1.0, where 1.0 means perfect match (default value: 0)
  - limit: The maximum number of relevant memories to recall (default value: 1)

TextMemorySkill.Remove:
  description: Remove specific memory
  inputs:
    - collection: Memories collection associated with the information to save (default value: generic)
  - key: The key associated with the information to save

TextMemorySkill.Retrieve:
  description: Key-based lookup for a specific memory
  inputs:
    - collection: Memories collection associated with the memory to retrieve (default value: generic)
  - key: The key associated with the memory to retrieve

TextMemorySkill.Save:
  description: Save information to semantic memory
  inputs:
    - input: The information to save
  - collection: Memories collection associated with the information to save (default value: generic)
  - key: The key associated with the information to save

TextSkill.Concat:
  description: Concat two strings into one.
  inputs:
    - input: First input to concatenate with
  - input2: Second input to concatenate with

TextSkill.Echo:
  description: Echo the input string. Useful for capturing plan input for use in multiple functions.
  inputs:
    - text: Input string to echo.

TextSkill.Length:
  description: Get the length of a string.
  inputs:
    - input:

TextSkill.Lowercase:
  description: Convert a string to lowercase.
  inputs:
    - input:

TextSkill.Trim:
  description: Trim whitespace from the start and end of a string.
  inputs:
    - input:

TextSkill.TrimEnd:
  description: Trim whitespace from the end of a string.
  inputs:
    - input:

TextSkill.TrimStart:
  description: Trim whitespace from the start of a string.
  inputs:
    - input:

TextSkill.Uppercase:
  description: Convert a string to uppercase.
  inputs:
    - input:

TimeSkill.Date:
  description: Get the current date
  inputs:


TimeSkill.DateMatchingLastDayName:
  description: Get the date of the last day matching the supplied week day name in English. Example: Che giorno era 'Martedi' scorso -> dateMatchingLastDayName 'Tuesday' => Tuesday, 16 May, 2023
  inputs:
    - input: The day name to match

TimeSkill.Day:
  description: Get the current day of the month
  inputs:


TimeSkill.DayOfWeek:
  description: Get the current day of the week
  inputs:


TimeSkill.DaysAgo:
  description: Get the date offset by a provided number of days from today
  inputs:
    - input: The number of days to offset from today

TimeSkill.Hour:
  description: Get the current clock hour
  inputs:


TimeSkill.HourNumber:
  description: Get the current clock 24-hour number
  inputs:


TimeSkill.Minute:
  description: Get the minutes on the current hour
  inputs:


TimeSkill.Month:
  description: Get the current month name
  inputs:


TimeSkill.MonthNumber:
  description: Get the current month number
  inputs:


TimeSkill.Now:
  description: Get the current date and time in the local time zone
  inputs:


TimeSkill.Second:
  description: Get the seconds on the current minute
  inputs:


TimeSkill.Time:
  description: Get the current time
  inputs:


TimeSkill.TimeZoneName:
  description: Get the local time zone name
  inputs:


TimeSkill.TimeZoneOffset:
  description: Get the local time zone offset from UTC
  inputs:


TimeSkill.Today:
  description: Get the current date
  inputs:


TimeSkill.UtcNow:
  description: Get the current UTC date and time
  inputs:


TimeSkill.Year:
  description: Get the current year
  inputs:


WaitSkill.Seconds:
  description: Wait a given amount of seconds
  inputs:
    - seconds: The number of seconds to wait

WriterSkill.Acronym:
  description: Generate an acronym for the given concept or phrase
  inputs:


WriterSkill.AcronymGenerator:
  description: Given a request to generate an acronym from a string, generate an acronym and provide the acronym explanation.
  inputs:


WriterSkill.AcronymReverse:
  description: Given a single word or acronym, generate the expanded form matching the acronym letters.
  inputs:


WriterSkill.Brainstorm:
  description: Given a goal or topic description generate a list of ideas
  inputs:
    - input: A topic description or goal.

WriterSkill.EmailGen:
  description: Write an email from the given bullet points
  inputs:


WriterSkill.EmailTo:
  description: Turn bullet points into an email to someone, using a polite tone
  inputs:


WriterSkill.EnglishImprover:
  description: Translate text to English and improve it
  inputs:


WriterSkill.NovelChapter:
  description: Write a chapter of a novel.
  inputs:
    - input: A synopsis of what the chapter should be about.
  - theme: The theme or topic of this novel.
  - previousChapter: The synopsis of the previous chapter.
  - chapterIndex: The number of the chapter to write. (default value: <!--===ENDPART===-->)

WriterSkill.NovelChapterWithNotes:
  description: Write a chapter of a novel using notes about the chapter to write.
  inputs:
    - input: What the novel should be about.
  - theme: The theme of this novel.
  - notes: Notes useful to write this chapter.
  - previousChapter: The previous chapter synopsis.
  - chapterIndex: The number of the chapter to write.

WriterSkill.NovelOutline:
  description: Generate a list of chapter synopsis for a novel or novella
  inputs:
    - input: What the novel should be about.
  - chapterCount: The number of chapters to generate.
  - endMarker: The marker to use to end each chapter. (default value: <!--===ENDPART===-->)

WriterSkill.Rewrite:
  description: Automatically generate compact notes for any text or text document
  inputs:


WriterSkill.ShortPoem:
  description: Turn a scenario into a short and entertaining poem.
  inputs:
    - input: The scenario to turn into a poem.

WriterSkill.StoryGen:
  description: Generate a list of synopsis for a novel or novella with sub-chapters
  inputs:


WriterSkill.TellMeMore:
  description: Summarize given text or any text document
  inputs:


WriterSkill.Translate:
  description: Translate the input into a language of your choice
  inputs:


WriterSkill.TwoSentenceSummary:
  description: Summarize given text in two sentences or less
  inputs:
";
    private const string UpdatedFunctionManual = @"
ChatSkill.Chat:
  description: Chat with the AI
  inputs:


ChatSkill.ChatAnonymizer:

    Description: Anonymize chat messages by replacing personal information with placeholders.
    Inputs:
        message: The chat message to be anonymized.


ChatSkill.ChatFilter:
  description: Given a chat message decide whether to block it
  inputs:


ChatSkill.ChatGPT:
  description:
  inputs:


ChatSkill.ChatModeration:

    Description: Filter and moderate chat messages for inappropriate content.
    Inputs:
        message: The chat message to be moderated.


ChatSkill.ChatTranslate:

    Description: Translate chat messages between different languages.
    Inputs:
        message: The chat message to be translated.
        source_language: The source language of the message.
        target_language: The target language for translation.


ChatSkill.ChatSentimentAnalysis:

    Description: Analyze the sentiment of a chat message (e.g., positive, negative, neutral).
    Inputs:
        message: The chat message for sentiment analysis.


ChatSkill.ChatUser:
  description: A chat bot that plays a persona or role
  inputs:


ChatSkill.ChatV2:
  description: A friendly chat where AI helps, avoiding bad topics
  inputs:


ChildrensBookSkill.BookIdeas:
  description: Given a topic description generate a number of children's book ideas with short descriptions
  inputs:


ChildrensBookSkill.CreateBook:
  description: Creates a children's book from the given input with a suggested number of words per page and a specific total number of pages
  inputs:


ChildrensBookSkill.PublishBook:

    Description: Publish a children's book created using the ChildrensBookSkill.CreateBook function.
    Inputs:
        book_content: The content of the children's book to be published.


ClassificationSkill.Importance:
  description: Tell you the urgency level of the given text
  inputs:


ClassificationSkill.Question:
  description: Tells you the sentence type (i.e. Question or Statement) of a given sentence
  inputs:


CodingSkill.Code:
  description: Turn natural language into code
  inputs:


CodingSkill.CodePython:
  description: Turns natural language into Python code like a Python Copilot.
  inputs:


CodingSkill.CommandLinePython:
  description: Turns natural language into Python command line scripts. Reads variables from args, operates on stdin, out
  inputs:


CodingSkill.DOSScript:
  description: Turns your intent into a SAFE DOS batch script
  inputs:


CodingSkill.ExecutePython:
  description: Executes python code.
  inputs:
    code: String containing python 


CodingSkill.EmailSearch:
  description: Search the Microsoft Graph for Email
  inputs:


CodingSkill.Entity:
  description: Given text, annotate all recognized entities. You specify the tags to use.
  inputs:


ConversationSummarySkill.func49e4ac9b0fbc4663bbeba0bbfcfd7232:
  description: Analyze a conversation transcript and extract key topics worth remembering.
  inputs:


ConversationSummarySkill.func6f523e05a7e54ba8b256a0457b6be413:
  description: Given a section of a conversation transcript, summarize the part of the conversation.
  inputs:


ConversationSummarySkill.func91d19b880cd34250831b30778ffadc1c:
  description: Given a section of a conversation transcript, identify action items.
  inputs:


ConversationSummarySkill.GetConversationActionItems:
  description: Given a long conversation transcript, identify action items.
  inputs:
    - input: A long conversation transcript.

ConversationSummarySkill.GetConversationTopics:
  description: Given a long conversation transcript, identify topics worth remembering.
  inputs:
    - input: A long conversation transcript.

ConversationSummarySkill.SummarizeConversation:
  description: Given a long conversation transcript, summarize the conversation.
  inputs:
    - input: A long conversation transcript.

FileIOSkill.Read:
  description: Read a file
  inputs:
    - path: Source file

FileIOSkill.Write:
  description: Write a file
  inputs:
    - path: Destination file
  - content: File content

FunSkill.Excuses:
  description: Turn a scenario into a creative or humorous excuse to send your boss
  inputs:


FunSkill.Joke:
  description: Generate a funny joke
  inputs:
    - input: Joke subject
  - style: Give a hint about the desired joke style

FunSkill.Limerick:
  description: Generate a funny limerick about a person
  inputs:
    - name:  (default value: Bob)
  - input:  (default value: Dogs)

FunSkill.RiddleGenerator:

    Description: Generate a challenging riddle for entertainment.
    Inputs:
        riddle_description: Description or theme for the riddle.

GroundingSkill.ExciseEntities:
  description: Remove a list of ungrounded entities from a given text in a coherent manner. Returns the input text without the ungrounded entities in the list
  inputs:
    - input: The text from which the entities are to be removed
  - ungrounded_entities: The entities to remove. This is a list of strings.

GroundingSkill.ExtractEntities:
  description: Extract entities related to a specified topic from the supplied input text. Returns the entities and the source text
  inputs:
    - input: The text from which the entities are to be extracted
  - topic: The topic of interest; the extracted entities should be related to this topic
  - example_entities: A list of example entities from the topic. This can help guide the entity extraction

HttpSkill.Delete:
  description: Makes a DELETE request to a uri
  inputs:
    - uri: The URI of the request

HttpSkill.Get:
  description: Makes a GET request to a uri
  inputs:
    - uri: The URI of the request


HttpSkill.Ping:

    Description: Send a ping request to check the availability of a server or website.
    Inputs:
        url: The URL to ping.


HttpSkill.Post:
  description: Makes a POST request to a uri
  inputs:
    - uri: The URI of the request
  - body: The body of the request

HttpSkill.Put:
  description: Makes a PUT request to a uri
  inputs:
    - uri: The URI of the request
  - body: The body of the request

IntentDetectionSkill.AssistantIntent:
  description: Given a query and a list of possible intents, detect which intent the input matches
  inputs:


MathSkill.Add:
  description: Adds an amount to a value
  inputs:
    - value: The value to add
  - amount: Amount to add

MathSkill.Subtract:
  description: Subtracts an amount from a value
  inputs:
    - value: The value to subtract
  - amount: Amount to subtract

MiscSkill.Continue:
  description: Given a text input, continue it with additional text.
  inputs:
    - input: The text to continue.

MiscSkill.ElementAtIndex:
  description: Get an element from an array at a specified index
  inputs:
    - input: The input array
  - index: The index of the element to retrieve
  - count: The number of items in the input

QASkill.AssistantResults:
  description:
  inputs:


QASkill.ContextQuery:
  description: Ask the AI for answers contextually relevant to you based on your name, address and pertinent information retrieved from your personal secondary memory
  inputs:


QASkill.Form:
  description:
  inputs:


QASkill.GitHubMemoryQuery:
  description:
  inputs:


QASkill.QNA:
  description: Ask AI for a list of question and answers based on text source
  inputs:


QASkill.Question:
  description: Answer any question
  inputs:


SummarizeSkill.MakeAbstractReadable:
  description: Given a scientific white paper abstract, rewrite it to make it more readable
  inputs:


SummarizeSkill.Notegen:
  description: Automatically generate compact notes for any text or text document.
  inputs:


SummarizeSkill.Summarize:
  description: Summarize given text or any text document
  inputs:
    - input: Text to summarize

SummarizeSkill.Topics:
  description: Analyze given text or document and extract key topics worth remembering
  inputs:


TextMemorySkill.Recall:
  description: Semantic search and return up to N memories related to the input text
  inputs:
    - input: The input text to find related memories for
  - collection: Memories collection to search (default value: generic)
  - relevance: The relevance score, from 0.0 to 1.0, where 1.0 means perfect match (default value: 0)
  - limit: The maximum number of relevant memories to recall (default value: 1)

TextMemorySkill.Remove:
  description: Remove specific memory
  inputs:
    - collection: Memories collection associated with the information to save (default value: generic)
  - key: The key associated with the information to save

TextMemorySkill.Retrieve:
  description: Key-based lookup for a specific memory
  inputs:
    - collection: Memories collection associated with the memory to retrieve (default value: generic)
  - key: The key associated with the memory to retrieve

TextMemorySkill.Save:
  description: Save information to semantic memory
  inputs:
    - input: The information to save
  - collection: Memories collection associated with the information to save (default value: generic)
  - key: The key associated with the information to save

TextSkill.Concat:
  description: Concat two strings into one.
  inputs:
    - input: First input to concatenate with
  - input2: Second input to concatenate with

TextSkill.Echo:
  description: Echo the input string. Useful for capturing plan input for use in multiple functions.
  inputs:
    - text: Input string to echo.

TextSkill.Length:
  description: Get the length of a string.
  inputs:
    - input:

TextSkill.Lowercase:
  description: Convert a string to lowercase.
  inputs:
    - input:

TextSkill.Trim:
  description: Trim whitespace from the start and end of a string.
  inputs:
    - input:

TextSkill.TrimEnd:
  description: Trim whitespace from the end of a string.
  inputs:
    - input:

TextSkill.TrimStart:
  description: Trim whitespace from the start of a string.
  inputs:
    - input:

TextSkill.Uppercase:
  description: Convert a string to uppercase.
  inputs:
    - input:

TimeSkill.Date:
  description: Get the current date
  inputs:


TimeSkill.DateMatchingLastDayName:
  description: Get the date of the last day matching the supplied week day name in English. Example: Che giorno era 'Martedi' scorso -> dateMatchingLastDayName 'Tuesday' => Tuesday, 16 May, 2023
  inputs:
    - input: The day name to match

TimeSkill.Day:
  description: Get the current day of the month
  inputs:


TimeSkill.DayOfWeek:
  description: Get the current day of the week
  inputs:


TimeSkill.DaysAgo:
  description: Get the date offset by a provided number of days from today
  inputs:
    - input: The number of days to offset from today

TimeSkill.Hour:
  description: Get the current clock hour
  inputs:


TimeSkill.HourNumber:
  description: Get the current clock 24-hour number
  inputs:


TimeSkill.Minute:
  description: Get the minutes on the current hour
  inputs:


TimeSkill.Month:
  description: Get the current month name
  inputs:


TimeSkill.MonthNumber:
  description: Get the current month number
  inputs:


TimeSkill.Now:
  description: Get the current date and time in the local time zone
  inputs:


TimeSkill.Second:
  description: Get the seconds on the current minute
  inputs:


TimeSkill.Time:
  description: Get the current time
  inputs:


TimeSkill.TimeZoneName:
  description: Get the local time zone name
  inputs:


TimeSkill.TimeZoneOffset:
  description: Get the local time zone offset from UTC
  inputs:


TimeSkill.Today:
  description: Get the current date
  inputs:


TimeSkill.UtcNow:
  description: Get the current UTC date and time
  inputs:


TimeSkill.Year:
  description: Get the current year
  inputs:


WaitSkill.Seconds:
  description: Wait a given amount of seconds
  inputs:
    - seconds: The number of seconds to wait

WriterSkill.Acronym:
  description: Generate an acronym for the given concept or phrase
  inputs:


WriterSkill.AcronymGenerator:
  description: Given a request to generate an acronym from a string, generate an acronym and provide the acronym explanation.
  inputs:


WriterSkill.AcronymReverse:
  description: Given a single word or acronym, generate the expanded form matching the acronym letters.
  inputs:


WriterSkill.Brainstorm:
  description: Given a goal or topic description generate a list of ideas
  inputs:
    - input: A topic description or goal.

WriterSkill.EmailGen:
  description: Write an email from the given bullet points
  inputs:


WriterSkill.EmailTo:
  description: Turn bullet points into an email to someone, using a polite tone
  inputs:


WriterSkill.EnglishImprover:
  description: Translate text to English and improve it
  inputs:


WriterSkill.NovelChapter:
  description: Write a chapter of a novel.
  inputs:
    - input: A synopsis of what the chapter should be about.
  - theme: The theme or topic of this novel.
  - previousChapter: The synopsis of the previous chapter.
  - chapterIndex: The number of the chapter to write. (default value: <!--===ENDPART===-->)

WriterSkill.NovelChapterWithNotes:
  description: Write a chapter of a novel using notes about the chapter to write.
  inputs:
    - input: What the novel should be about.
  - theme: The theme of this novel.
  - notes: Notes useful to write this chapter.
  - previousChapter: The previous chapter synopsis.
  - chapterIndex: The number of the chapter to write.

WriterSkill.NovelOutline:
  description: Generate a list of chapter synopsis for a novel or novella
  inputs:
    - input: What the novel should be about.
  - chapterCount: The number of chapters to generate.
  - endMarker: The marker to use to end each chapter. (default value: <!--===ENDPART===-->)

WriterSkill.Rewrite:
  description: Automatically generate compact notes for any text or text document
  inputs:


WriterSkill.ShortPoem:
  description: Turn a scenario into a short and entertaining poem.
  inputs:
    - input: The scenario to turn into a poem.

WriterSkill.StoryGen:
  description: Generate a list of synopsis for a novel or novella with sub-chapters
  inputs:


WriterSkill.TellMeMore:
  description: Summarize given text or any text document
  inputs:


WriterSkill.Translate:
  description: Translate the input into a language of your choice
  inputs:


WriterSkill.TwoSentenceSummary:
  description: Summarize given text in two sentences or less
  inputs:
";
}
