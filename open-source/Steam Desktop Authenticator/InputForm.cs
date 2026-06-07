using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public partial class InputForm : Form
    {
        public bool Canceled = false;
        private bool userClosed = true;

        public InputForm(string label, bool password = false)
        {
            InitializeComponent();
            Icon = Branding.LoadAppIcon();
            ModernUi.AttachWindowChrome(this, false, false);
            ModernUi.ShiftControlsDown(this, ModernUi.HeaderHeight - 2);
            ApplyTheme();
            this.labelText.Text = label;

            if (password)
            {
                this.txtBox.PasswordChar = '*';
            }
        }

        private void ApplyTheme()
        {
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            labelText.ForeColor = Branding.HeadingText;
            ModernUi.WrapTextBox(txtBox, 12, 8);
            ModernUi.RoundButton(btnAccept, true);
            ModernUi.RoundButton(btnCancel, false);
            Paint += ModernUi.PaintGlassBackground;
        }

        private void btnAccept_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(this.txtBox.Text))
            {
                this.Canceled = true;
                this.userClosed = false;
                this.Close();
            }
            else
            {
                this.Canceled = false;
                this.userClosed = false;
                this.Close();
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Canceled = true;
            this.userClosed = false;
            this.Close();
        }

        private void InputForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.userClosed)
            {
                // Set Canceled = true when the user hits the X button.
                this.Canceled = true;
            }
        }
    }
}
