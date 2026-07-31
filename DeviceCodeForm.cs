using System.Diagnostics;
using System.Windows.Forms;

namespace QuickOneNote;

/// <summary>
/// Modal that walks the user through device-code sign-in: shows the code, opens the sign-in
/// page, and polls for completion. DialogResult.OK means signed in.
/// </summary>
public sealed class DeviceCodeForm : Form
{
    private readonly GraphAuth _auth;
    private readonly CancellationTokenSource _cts = new();

    private readonly Label _intro = new();
    private readonly TextBox _code = new();
    private readonly LinkLabel _link = new();
    private readonly Label _status = new();
    private readonly Button _openBtn = new();
    private readonly Button _copyBtn = new();

    private GraphAuth.DeviceCode? _dc;

    public DeviceCodeForm(GraphAuth auth)
    {
        _auth = auth;
        BuildLayout();
        Shown += async (_, _) => await StartAsync();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void BuildLayout()
    {
        Text = "Sign in to Microsoft";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 250);
        Icon = IconFactory.CreateNoteIcon();

        int x = 16, w = 388;

        _intro.Location = new Point(x, 14);
        _intro.Size = new Size(w, 40);
        _intro.Text = "1) Open the sign-in page.  2) Enter this code.  3) Approve access.";

        var lblCode = new Label { Text = "Your code:", Location = new Point(x, 62), AutoSize = true };
        _code.Location = new Point(x, 84);
        _code.Size = new Size(w, 34);
        _code.ReadOnly = true;
        _code.Font = new Font("Consolas", 16, FontStyle.Bold);
        _code.TextAlign = HorizontalAlignment.Center;
        _code.Text = "…";

        _copyBtn.Text = "Copy code";
        _copyBtn.Location = new Point(x, 126);
        _copyBtn.Size = new Size(120, 30);
        _copyBtn.Click += (_, _) => { try { if (_dc != null) Clipboard.SetText(_dc.UserCode); } catch { } };

        _openBtn.Text = "Open sign-in page";
        _openBtn.Location = new Point(x + 130, 126);
        _openBtn.Size = new Size(150, 30);
        _openBtn.Click += (_, _) => OpenSignInPage();

        _link.Location = new Point(x, 168);
        _link.Size = new Size(w, 20);
        _link.Text = "";
        _link.LinkClicked += (_, _) => OpenSignInPage();

        _status.Location = new Point(x, 196);
        _status.Size = new Size(w, 20);
        _status.ForeColor = SystemColors.GrayText;
        _status.Text = "Starting…";

        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(x + w - 90, 210), Size = new Size(90, 30) };

        Controls.AddRange(new Control[] { _intro, lblCode, _code, _copyBtn, _openBtn, _link, _status, cancel });
        CancelButton = cancel;
    }

    private void OpenSignInPage()
    {
        if (_dc == null) return;
        try { Process.Start(new ProcessStartInfo(_dc.VerificationUri) { UseShellExecute = true }); } catch { }
    }

    private async Task StartAsync()
    {
        try
        {
            _status.Text = "Contacting Microsoft…";
            _dc = await _auth.StartDeviceCodeAsync();
            _code.Text = _dc.UserCode;
            _link.Text = _dc.VerificationUri;
            _status.Text = "Waiting for you to finish signing in…";

            OpenSignInPage();

            bool ok = await _auth.PollForTokenAsync(_dc, _cts.Token);
            if (ok)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _status.Text = "Sign-in was not completed. Close and try again.";
            }
        }
        catch (OperationCanceledException) { /* cancelled */ }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
        }
    }
}
