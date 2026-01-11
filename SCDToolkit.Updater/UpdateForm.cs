using System;
using System.Drawing;
using System.Windows.Forms;

namespace SCDToolkit.Updater;

internal sealed class UpdateForm : Form
{
    private readonly Label _status;
    private readonly ProgressBar _progress;

    public UpdateForm()
    {
        Text = "SCDToolkit Update";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 520;
        Height = 180;

        _status = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Top,
            Height = 60,
            Padding = new Padding(12, 12, 12, 0),
            Text = "Preparing..."
        };

        _progress = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 24,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        Controls.Add(_progress);
        Controls.Add(_status);
    }

    public void SetStatus(string text)
    {
        _status.Text = text;
    }

    public void SetProgress(int done, int total)
    {
        if (total <= 0)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            return;
        }

        _progress.Style = ProgressBarStyle.Continuous;
        var pct = (int)Math.Clamp((done * 100.0) / total, 0, 100);
        _progress.Value = pct;
    }
}
