
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace threadsafe_show_dialog
{
    public partial class MainForm : Form
    {
        public MainForm() => InitializeComponent();
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Disposed += (sender, e) =>
            {
                _cts.Cancel();
                FaultDetectedScreen.Dispose();
            };
            for (int i = 0; i < 3; i++) 
            {
                var button = new Button
                {
                    Name = $"buttonScreen{i}",
                    Text = $"Show Screen {i}",
                    Location = new Point(50, 50 + (i * 60)),
                    Size = new Size(200, 50),
                };
                button.Click += (sender, e) =>
                {
                    if (
                        sender is Button button
                        &&
                        button.Name is string name
                        &&
                        Application.OpenForms[name.Replace("button", "form")] is Form form
                        &&
                        !form.Visible)
                        form.Show(this);
                };
                var screen = new MockScreen
                {
                    Name = $"formScreen{i}",
                    Text = $"Screen {i}",
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(Location.X + 10 + Width, Location.Y + (i * (Height + 10))),
                    Size = Size,
                };
                screen.Show(this);
                screen.VisibleChanged += (sender, e) => BringToFront(); // 'Last child closed' workaround.
                screen.Fault += Any_ScreenFault;
                Controls.Add(button);
            }
            FaultDetectedScreen.Location = new Point(Location.X + 10, Location.Y + 10);
            FaultDetectedScreen.Size = new Size(Size.Width - 20, Size.Height - 20);
            FaultDetectedScreen.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            _ = GenerateRandomFaults(_cts.Token);
        }

        private void Any_ScreenFault(object? sender, EventArgs e)
        {
            Debug.WriteLine($"InvokeRequired: {InvokeRequired}");
            BeginInvoke(() =>
            {
                if (sender is MockScreen screen)
                {
                    FaultDetectedScreen.Report(screen);
                }
                if (!(Disposing || FaultDetectedScreen.Visible))
                {
                    FaultDetectedScreen.ShowDialog(this);
                }
            });
        }

        private readonly static Random _rando = new Random(4);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private async Task GenerateRandomFaults(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(5 + (5 * _rando.NextDouble())),
                    token);
                var activeScreens = Application.OpenForms.OfType<MockScreen>().ToArray();
                activeScreens[_rando.Next(activeScreens.Length)]
                    .RaiseMockFault(onDifferentThread: _rando.Next(2) == 1);
            }
        }
        FrmFaultDetected FaultDetectedScreen { get; } = new FrmFaultDetected();
    }
    class MockScreen : Form
    {
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if(e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }
        public void RaiseMockFault(bool onDifferentThread)
        {
            if (onDifferentThread) Task.Run(() => Fault?.Invoke(this, EventArgs.Empty));
            else Fault?.Invoke(this, EventArgs.Empty);
        }
        public event EventHandler? Fault;
    }
    class FrmFaultDetected : Form 
    {
        RichTextBox _richTextBox = new RichTextBox
        {
            Name = nameof(RichTextBox),
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(0x22, 0x22, 0x22),
        };
        public FrmFaultDetected() 
        {
            StartPosition = FormStartPosition.Manual;
            Padding = new Padding(5);
            Controls.Add(_richTextBox);
            var button = new Button
            {
                Name = nameof(Button),
                Text = "OK",
                Dock = DockStyle.Bottom,
                Height = 50,
            };
            button.Click += (sender, e) => DialogResult = DialogResult.OK;
            Controls.Add(button);
        }
        private readonly Dictionary<MockScreen, int> _counts = new Dictionary<MockScreen, int>();
        public void Report(MockScreen screen)
        {
            if (!_counts.ContainsKey(screen))
            {
                _counts[screen] = 1;
            }
            else _counts[screen]++;
            var count = _counts[screen];
            switch (count.CompareTo(2))
            {
                case -1: _richTextBox.SelectionColor = Color.LightSalmon; break;
                case 0: _richTextBox.SelectionColor = Color.Yellow; break;
                case 1: _richTextBox.SelectionColor = Color.Red; break;
            }
            _richTextBox
                .AppendText(
                $@"[{DateTime.Now.TimeOfDay:hh\:mm\:ss\:ff}] {screen.Name.Replace("form", string.Empty)} Errors={count}{Environment.NewLine}");
        }
    }
}
