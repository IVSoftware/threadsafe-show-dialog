## Threadsafe Show Dialog

You describe a systemic and ongoing situation where "each week" you try something different with the marshaling, so I'll show a minimal mock to experiment with and adapt, and hopefully get your issue solved. Your code indicates that you're maintaining multiple screen objects in a stack named `m_openScreens`, so this class will be used to represent a simplified non-modal screen that produces some kind of fault.

```
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
    public void RaiseMockFault() => Fault?.Invoke(this, EventArgs.Empty);
    public event EventHandler? Fault;
}
```
___

To mock the "really simple form with a close button on it" envision a form that pops up modally whenever a new error occurs with the behavior that if it's still popped up when a new error occurs on any of the screens, it adds the new error to the report already displayed.

[![modal error dialog][1]][1]

```
class FaultDetectedScreen : Form 
{
    RichTextBox _richTextBox = new RichTextBox
    {
        Name = nameof(RichTextBox),
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(0x22, 0x22, 0x22),
    };
    public FaultDetectedScreen() 
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
```

On this basis, to handle the fault regardless of whether it was raised on the UI thread or not, you could use this pattern.

```
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
```

___
#### Simulation

Here's the code I used to test this answer:

[![screenshot][2]][2]

```
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
                .RaiseMockFault(_rando.Next(2) == 1);
        }
    }
    FaultDetectedScreen FaultDetectedScreen { get; } = new FaultDetectedScreen();
}
```


  [1]: https://i.stack.imgur.com/h6IoM.png
  [2]: https://i.stack.imgur.com/g7OFa.png