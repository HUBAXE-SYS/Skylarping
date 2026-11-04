/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Skylarping.AI;
using Skylarping.Audio;

namespace Skylarping.UserControls
{
    public partial class SpeechTestView : UserControl
    {
        private const int TestMilliseconds = 5000;

        public SpeechTestView()
        {
            InitializeComponent();
        }

        /// <summary>The server to test, as currently typed.</summary>
        public Func<LlmEndpoint> Endpoint { get; set; }

        /// <summary>The language to send, as currently typed.</summary>
        public Func<string> Language { get; set; }

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
            string language = Language();

            string invalid = endpoint.ValidateUrl();
            if (invalid != null)
            {
                TestResult.Text = invalid;
                return;
            }
            if (!MicrophoneCapture.Available)
            {
                TestResult.Text = "No microphone was found on this computer.";
                return;
            }

            TestButton.IsEnabled = false;
            try
            {
                TestResult.Text = "Recording... say something.";
                byte[] pcm = await TaskEx.Run(() => MicrophoneCapture.Record(TestMilliseconds));

                TestResult.Text = "Transcribing...";
                Stopwatch timer = Stopwatch.StartNew();
                string text = await TaskEx.Run(() =>
                    OpenAICompatClient.Transcribe(endpoint, WavFile.FromPcm(pcm, MicrophoneCapture.SampleRate), language));
                timer.Stop();

                string seconds = (timer.ElapsedMilliseconds / 1000.0).ToString("0.0");
                TestResult.Text = text.Length > 0
                    ? "Heard in " + seconds + " s: " + text
                    : "The server answered in " + seconds + " s but heard no speech.";
            }
            catch (LlmException ex)
            {
                TestResult.Text = ConnectionTest.Explain(ex);
            }
            catch (Exception ex)
            {
                TestResult.Text = "The microphone test failed: " + ex.Message;
            }
            finally
            {
                TestButton.IsEnabled = true;
            }
        }
    }
}
