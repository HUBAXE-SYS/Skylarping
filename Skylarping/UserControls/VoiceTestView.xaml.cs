/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Skylarping.AI;
using Skylarping.Audio;

namespace Skylarping.UserControls
{
    public partial class VoiceTestView : UserControl
    {
        private const string TestLine = "Hello! This is how I'll sound on a call.";

        private CancellationTokenSource _run;

        public VoiceTestView()
        {
            InitializeComponent();
            Unloaded += (s, e) => _run?.Cancel(); // the window closed or another page opened
        }

        /// <summary>The server to test, as currently typed.</summary>
        public Func<LlmEndpoint> Endpoint { get; set; }

        /// <summary>The speed to use, as currently typed.</summary>
        public Func<int> SpeedPercent { get; set; }

        public bool IsRunning
        {
            get { return !TestButton.IsEnabled; }
        }

        /// <summary>Clears an old result, which describes settings that have since changed.</summary>
        public void Clear()
        {
            if (!IsRunning)
                TestResult.Text = "";
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            LlmEndpoint endpoint = Endpoint();
            int speed = SpeedPercent();

            string invalid = endpoint.ValidateUrl();
            if (invalid != null)
            {
                TestResult.Text = invalid;
                return;
            }

            var run = new CancellationTokenSource();
            _run = run;
            TestButton.IsEnabled = false;
            try
            {
                TestResult.Text = "Generating...";
                Stopwatch timer = Stopwatch.StartNew();
                byte[] wav = await TaskEx.Run(() => OpenAICompatClient.Synthesize(endpoint, TestLine, speed));
                timer.Stop();

                if (run.IsCancellationRequested)
                {
                    TestResult.Text = "";
                    return;
                }

                short[] samples = SpeechAudio.Decode(wav);
                double spoken = samples.Length / (double)SpeechAudio.SampleRate;
                TestResult.Text = "Took " + (timer.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s to generate " +
                                  spoken.ToString("0.0") + " s of speech. Playing it...";
                string result = TestResult.Text.Replace(" Playing it...", "");
                await TaskEx.Run(() => SpeechAudio.Play(samples, run.Token));
                TestResult.Text = result;
            }
            catch (LlmException ex)
            {
                TestResult.Text = ConnectionTest.Explain(ex);
            }
            catch (Exception ex)
            {
                TestResult.Text = "The voice test failed: " + ex.Message;
            }
            finally
            {
                TestButton.IsEnabled = true;
            }
        }
    }
}
