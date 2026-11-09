/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Writes through as you choose, like the other Options pages. The level bar listens to the
// chosen microphone for as long as the page is open.
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Skylarping.Audio;
using Skylarping.Preferences;
using Skylarping.Sounds;

namespace Skylarping.Forms.OptionPages.General
{
    public partial class Devices : Page
    {
        private const string DefaultDevice = "Windows default";
        private const int FramesPerLevelUpdate = 5; // 100 ms of 20 ms frames
        private const string TestSound = "IM";

        private static readonly int[] CallRates = { 0, AudioDevices.AlternativeCallRate, AudioDevices.PreferredCallRate };

        private readonly bool _loading;
        private MicrophoneCapture _meter;
        private double _peak;
        private int _frames;

        public Devices()
        {
            InitializeComponent();

            _loading = true;
            IList<string> speakers = AudioDevices.Speakers();
            Fill(MicrophoneBox, AudioDevices.Microphones(), Settings.MicrophoneDevice);
            Fill(SpeakerBox, speakers, Settings.SpeakerDevice);
            Fill(RingBox, speakers, Settings.RingDevice);
            int buffer = Settings.SpeakerBufferMilliseconds;
            BufferBox.Text = buffer > 0 ? buffer.ToString() : "";
            BufferHint.Text = "Lower makes the character answer and stop sooner on calls; raise it if their voice " +
                              "breaks up. Automatic is " + SpeechPlayer.AutomaticBufferMilliseconds + " ms on this Windows. " +
                              "Takes effect on the next call.";
            foreach (int rate in CallRates)
                RateBox.Items.Add(rate == 0 ? "Automatic (48 kHz)" : (rate / 1000.0).ToString("0.#") + " kHz");
            RateBox.SelectedIndex = Math.Max(0, Array.IndexOf(CallRates, Settings.CallSampleRate));
            LogBox.IsChecked = Settings.CallLog;
            LogHint.Text = "One file per call in " + CallLog.Folder + ". Nothing you say is written, only timing.";
            _loading = false;

            Loaded += (s, e) =>
            {
                StartMeter();
                Window.GetWindow(this).Closed += (w, c) => StopMeter(); // closing doesn't always unload the page
            };
            Unloaded += (s, e) => StopMeter();
        }

        private static void Fill(ComboBox box, IList<string> devices, string chosen)
        {
            box.Items.Add(DefaultDevice);
            foreach (string device in devices)
                box.Items.Add(device);
            box.SelectedIndex = Math.Max(0, devices.IndexOf(chosen) + 1);
        }

        private static string Chosen(ComboBox box)
        {
            return box.SelectedIndex > 0 ? (string)box.SelectedItem : "";
        }

        private void Microphone_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
                return;
            Settings.MicrophoneDevice = Chosen(MicrophoneBox);
            StopMeter();
            StartMeter();
        }

        private void Speaker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading)
                Settings.SpeakerDevice = Chosen(SpeakerBox);
        }

        private void Ring_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading)
                Settings.RingDevice = Chosen(RingBox);
        }

        private void Buffer_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading)
                return;
            string text = BufferBox.Text.Trim();
            int buffer;
            if (text.Length == 0)
                Settings.SpeakerBufferMilliseconds = 0;
            else if (int.TryParse(text, out buffer) && buffer >= SpeechPlayer.MinBufferMilliseconds && buffer <= SpeechPlayer.MaxBufferMilliseconds)
                Settings.SpeakerBufferMilliseconds = buffer;
            // Anything else is still being typed, or out of range: the saved value stays.
        }

        private void Rate_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading && RateBox.SelectedIndex >= 0)
                Settings.CallSampleRate = CallRates[RateBox.SelectedIndex];
        }

        private void Log_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                Settings.CallLog = LogBox.IsChecked == true;
        }

        private void CheckSpeakers_Click(object sender, RoutedEventArgs e)
        {
            SoundManager.Play(TestSound);
        }

        private void StartMeter()
        {
            LevelBar.Value = 0;
            if (!MicrophoneCapture.Available)
            {
                LevelHint.Text = "No microphone was found on this computer.";
                return;
            }

            var meter = new MicrophoneCapture();
            try
            {
                meter.Frame += OnFrame;
                meter.Failed += ex => Dispatcher.BeginInvoke(new Action(() => LevelHint.Text = "The microphone stopped: " + ex.Message));
                meter.Start();
                _meter = meter;
            }
            catch (Exception ex)
            {
                meter.Dispose();
                LevelHint.Text = "Couldn't open the microphone: " + ex.Message;
            }
        }

        private void StopMeter()
        {
            if (_meter == null)
                return;
            _meter.Dispose();
            _meter = null;
        }

        private void OnFrame(short[] frame)
        {
            _peak = Math.Max(_peak, MicrophoneCapture.Level(frame));
            if (++_frames < FramesPerLevelUpdate)
                return;

            double level = _peak;
            _peak = 0;
            _frames = 0;
            Dispatcher.BeginInvoke(new Action(() => LevelBar.Value = level));
        }
    }
}
