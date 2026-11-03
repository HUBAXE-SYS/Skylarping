/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Skylarping.AI;

namespace Skylarping.UserControls
{
    public partial class ConnectionTestView : UserControl
    {
        private static readonly Brush PassedBrush = Frozen(Color.FromRgb(0x2E, 0x8B, 0x2E));
        private static readonly Brush WarningBrush = Frozen(Color.FromRgb(0xB8, 0x86, 0x0B));
        private static readonly Brush FailedBrush = Frozen(Color.FromRgb(0xC0, 0x00, 0x00));
        private static readonly Brush NeutralBrush = Frozen(Color.FromRgb(0x80, 0x80, 0x80));

        private readonly ObservableCollection<StepRow> _rows = new ObservableCollection<StepRow>();

        public ConnectionTestView()
        {
            InitializeComponent();
            StepList.ItemsSource = _rows;
        }

        public bool IsRunning { get; private set; }

        public void Clear()
        {
            _rows.Clear();
            SummaryText.Visibility = Visibility.Collapsed;
        }

        /// <summary>Runs the test in the background, showing each step as it finishes.</summary>
        public async Task<ConnectionTestResult> Run(LlmEndpoint endpoint)
        {
            IsRunning = true;
            Clear();
            foreach (string name in ConnectionTest.StepNames)
                _rows.Add(new StepRow(name));

            var ui = Dispatcher;
            ConnectionTestResult result;
            try
            {
                result = await TaskEx.Run(() => ConnectionTest.Run(endpoint, step =>
                {
                    TestStepStatus status = step.Status;
                    string detail = step.Detail;
                    ui.BeginInvoke(new Action(() =>
                    {
                        foreach (StepRow row in _rows)
                            if (row.Name == step.Name)
                                row.Update(status, detail);
                    }));
                }));
            }
            finally
            {
                IsRunning = false;
            }

            // Steps posted above are queued ahead of this, so the rows are final now.
            SummaryText.Text = result.Summary;
            SummaryText.Foreground = result.Succeeded ? PassedBrush : FailedBrush;
            SummaryText.Visibility = Visibility.Visible;
            return result;
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private sealed class StepRow : INotifyPropertyChanged
        {
            public StepRow(string name)
            {
                Name = name;
                StatusText = "Waiting";
                Detail = "";
                Brush = NeutralBrush;
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public string Name { get; private set; }
            public string StatusText { get; private set; }
            public string Detail { get; private set; }
            public Brush Brush { get; private set; }

            public void Update(TestStepStatus status, string detail)
            {
                var step = new TestStep(Name) { Status = status };
                StatusText = step.StatusText;
                Detail = detail;
                switch (status)
                {
                    case TestStepStatus.Passed: Brush = PassedBrush; break;
                    case TestStepStatus.Warning: Brush = WarningBrush; break;
                    case TestStepStatus.Failed: Brush = FailedBrush; break;
                    default: Brush = NeutralBrush; break;
                }
                Notify("StatusText");
                Notify("Detail");
                Notify("Brush");
            }

            private void Notify(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
