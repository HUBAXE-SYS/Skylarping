/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Step-by-step check of a language model connection, shared by the Connection Wizard,
// Tools > AI Services and Options > Advanced > Connection. Each step reports on its own
// so the user sees *what* failed: the URL, reaching the server, the API key, the model,
// or the chat call itself.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Skylarping.AI
{
    public enum TestStepStatus
    {
        Running,
        Passed,
        Warning,
        Failed,
        Skipped,
    }

    public sealed class TestStep
    {
        public TestStep(string name) { Name = name; Status = TestStepStatus.Running; Detail = ""; }

        public string Name { get; private set; }
        public TestStepStatus Status { get; internal set; }
        public string Detail { get; internal set; }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case TestStepStatus.Running: return "Testing...";
                    case TestStepStatus.Passed: return "OK";
                    case TestStepStatus.Warning: return "Warning";
                    case TestStepStatus.Failed: return "Failed";
                    default: return "Skipped";
                }
            }
        }
    }

    public sealed class ConnectionTestResult
    {
        public ConnectionTestResult(IList<TestStep> steps) { Steps = steps; }

        public IList<TestStep> Steps { get; private set; }
        /// <summary>Nothing failed (a step skipped for lack of information, e.g. no model list, is fine).</summary>
        public bool Succeeded { get { return Steps.All(s => s.Status != TestStepStatus.Failed && s.Status != TestStepStatus.Running); } }

        /// <summary>One line for the user: what worked or the first thing that didn't.</summary>
        public string Summary
        {
            get
            {
                TestStep failed = Steps.FirstOrDefault(s => s.Status == TestStepStatus.Failed);
                if (failed != null)
                    return "Not working yet: \"" + failed.Name + "\" failed (details above).";
                TestStep warning = Steps.FirstOrDefault(s => s.Status == TestStepStatus.Warning);
                if (warning != null)
                    return "Connected, but check the warning above.";
                return "Connected. Your language model is working.";
            }
        }
    }

    public static class ConnectionTest
    {
        public const string StepUrl = "Server address";
        public const string StepReach = "Reach the server";
        public const string StepModel = "Find the model";
        public const string StepChat = "Send a test message";

        public static string[] StepNames { get { return new[] { StepUrl, StepReach, StepModel, StepChat }; } }

        /// <summary>
        /// Runs every step in order (blocking; call it off the UI thread). <paramref name="progress"/>
        /// is called after each step changes, from the calling thread.
        /// </summary>
        public static ConnectionTestResult Run(LlmEndpoint endpoint, Action<TestStep> progress = null)
        {
            var steps = StepNames.Select(n => new TestStep(n)).ToList();
            Action<TestStep, TestStepStatus, string> set = (step, status, detail) =>
            {
                step.Status = status;
                step.Detail = detail ?? "";
                progress?.Invoke(step);
            };
            Action<int> skipFrom = index =>
            {
                for (int i = index; i < steps.Count; i++)
                    set(steps[i], TestStepStatus.Skipped, "");
            };

            // 1. URL
            string invalid = endpoint.ValidateUrl();
            if (invalid != null)
            {
                set(steps[0], TestStepStatus.Failed, invalid);
                skipFrom(1);
                return new ConnectionTestResult(steps);
            }
            set(steps[0], TestStepStatus.Passed, endpoint.BaseUrl);

            // 2. Reach the server via /models (also proves the API key, if one is needed).
            IList<string> models = null;
            bool modelsUnsupported = false;
            progress?.Invoke(steps[1]);
            try
            {
                var timer = Stopwatch.StartNew();
                models = OpenAICompatClient.ListModels(endpoint);
                set(steps[1], TestStepStatus.Passed,
                    "Connected in " + timer.ElapsedMilliseconds + " ms, " + models.Count + " model" + (models.Count == 1 ? "" : "s") + " available.");
            }
            catch (LlmException ex)
            {
                if (ex.Kind == LlmErrorKind.NotFound)
                {
                    // Some servers don't implement /models; the chat call below is the real test.
                    modelsUnsupported = true;
                    set(steps[1], TestStepStatus.Warning, "The server has no model list (/models). Trying a chat message instead.");
                }
                else
                {
                    set(steps[1], TestStepStatus.Failed, Explain(ex));
                    skipFrom(2);
                    return new ConnectionTestResult(steps);
                }
            }

            // 3. Model
            if (endpoint.Model.Length == 0)
            {
                if (models != null && models.Count > 1)
                    set(steps[2], TestStepStatus.Warning, "No model chosen; the server will pick one. Choose one from the model list to be sure.");
                else
                    set(steps[2], TestStepStatus.Passed, models != null && models.Count == 1 ? "Using the server's only model, " + models[0] + "." : "Using the server's default model.");
            }
            else if (modelsUnsupported || models == null)
                set(steps[2], TestStepStatus.Skipped, "Can't check without a model list.");
            else if (models.Any(m => string.Equals(m, endpoint.Model, StringComparison.OrdinalIgnoreCase)))
                set(steps[2], TestStepStatus.Passed, endpoint.Model);
            else
                set(steps[2], TestStepStatus.Warning, "\"" + endpoint.Model + "\" isn't in the server's model list. It may still load on demand.");

            // 4. A tiny chat completion.
            progress?.Invoke(steps[3]);
            try
            {
                var timer = Stopwatch.StartNew();
                LlmChatResult reply = OpenAICompatClient.Chat(endpoint,
                    new[] { LlmChatMessage.FromUser("Reply with the single word: OK") },
                    maxTokens: 16, timeoutMilliseconds: 120000); // any 200 reply counts, so keep it short
                string model = string.IsNullOrEmpty(reply.Model) ? "" : " from " + reply.Model;
                set(steps[3], TestStepStatus.Passed, "Got a reply" + model + " in " + (timer.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s.");
            }
            catch (LlmException ex)
            {
                set(steps[3], TestStepStatus.Failed, Explain(ex));
            }

            return new ConnectionTestResult(steps);
        }

        /// <summary>Turns a client error into advice.</summary>
        public static string Explain(LlmException ex)
        {
            switch (ex.Kind)
            {
                case LlmErrorKind.Unauthorized:
                    return "The server refused the API key (" + ex.Message + "). Check the key, or leave it blank for local servers.";
                case LlmErrorKind.NotFound:
                    return "Not found (" + ex.Message + "). The URL usually ends in /v1, as in http://ip:port/v1.";
                case LlmErrorKind.Unreachable:
                    return ex.Message + " Is the server running, and is the address and port right?";
                default:
                    return ex.Message;
            }
        }
    }
}
