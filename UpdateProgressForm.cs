using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// A small borderless toast near the tray that shows update progress and updates <em>in place</em>
/// (a balloon notification can't be updated — re-showing it per percent causes the hide/show
/// flicker). Shown while an update downloads/installs; closes when the app exits to relaunch.
/// </summary>
public sealed class UpdateProgressForm : Form
{
    private readonly Label _title = new();
    private readonly Label _pct = new();
    private readonly ProgressBar _bar = new();
    private readonly string _ver;

    // Show without stealing focus from whatever the user is doing.
    protected override bool ShowWithoutActivation => true;

    /// <param name="targetVersion">The version being installed, e.g. "1.4.3".</param>
    public UpdateProgressForm(string targetVersion)
    {
        _ver = targetVersion;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Size = new Size(340, 100);
        Icon = IconFactory.CreateNoteIcon();

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);

        _title.Text = $"Updating to v{_ver}…";
        _title.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(40, 40, 40);
        _title.AutoSize = false;
        _title.Location = new Point(16, 14);
        _title.Size = new Size(308, 22);

        _bar.Location = new Point(16, 48);
        _bar.Size = new Size(250, 18);
        _bar.Minimum = 0;
        _bar.Maximum = 100;
        _bar.Style = ProgressBarStyle.Marquee;
        _bar.MarqueeAnimationSpeed = 30;

        _pct.Text = "";
        _pct.Font = new Font("Segoe UI", 9.5f);
        _pct.ForeColor = Color.FromArgb(90, 90, 90);
        _pct.AutoSize = false;
        _pct.TextAlign = ContentAlignment.MiddleRight;
        _pct.Location = new Point(272, 47);
        _pct.Size = new Size(52, 20);

        Controls.Add(_title);
        Controls.Add(_bar);
        Controls.Add(_pct);
    }

    /// <summary>Update the toast for a phase (downloading/staging/applying) and percentage.</summary>
    public void SetProgress(string phase, int pct)
    {
        _title.Text = phase switch
        {
            "downloading" => $"Downloading v{_ver}…",
            "staging" => $"Preparing v{_ver}…",
            "applying" => $"Installing v{_ver} — restarting…",
            _ => $"Updating to v{_ver}…",
        };

        // The download reports a real 0–100%; other phases are indeterminate (marquee).
        if (phase == "downloading" && pct > 0)
        {
            if (_bar.Style != ProgressBarStyle.Continuous) _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = Math.Min(100, Math.Max(0, pct));
            _pct.Text = _bar.Value + "%";
        }
        else if (phase != "downloading")
        {
            if (_bar.Style != ProgressBarStyle.Marquee) _bar.Style = ProgressBarStyle.Marquee;
            _pct.Text = "";
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(210, 210, 210));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
