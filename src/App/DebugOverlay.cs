using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aly.App
{
    /// <summary>Tiny always-on-top readout of state and resource use. Exists only while enabled.</summary>
    sealed class DebugOverlay : Form
    {
        readonly Label label = new Label();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Func<string> source;

        public DebugOverlay(Func<string> textSource)
        {
            source = textSource;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(36, 30, 42);
            ForeColor = Color.FromArgb(240, 232, 248);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            label.AutoSize = true;
            label.Font = new Font("Consolas", 9f);
            label.Padding = new Padding(6);
            Controls.Add(label);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + 8, wa.Top + 8);
            timer.Interval = 1000;
            timer.Tick += delegate { label.Text = source(); };
            label.Text = source();
            timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                label.Font.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
