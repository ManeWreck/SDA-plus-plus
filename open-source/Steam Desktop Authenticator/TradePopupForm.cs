using SteamAuth;
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
    public partial class TradePopupForm : Form
    {
        private SteamGuardAccount acc;
        private List<PendingConfirmation> requests = new List<PendingConfirmation>();
        private bool deny2, accept2;

        public TradePopupForm()
        {
            InitializeComponent();
            lblStatus.Text = "";
        }

        public SteamGuardAccount Account
        {
            get { return acc; }
            set { acc = value; lblAccount.Text = acc.AccountName; }
        }

        public Confirmation[] Confirmations
        {
            get { return requests.Select(request => request.Confirmation).ToArray(); }
            set { requests = new List<PendingConfirmation>((value ?? Array.Empty<Confirmation>()).Select(confirmation => new PendingConfirmation(acc, confirmation))); }
        }

        public PendingConfirmation[] Requests
        {
            get { return requests.ToArray(); }
            set { requests = new List<PendingConfirmation>(value ?? Array.Empty<PendingConfirmation>()); }
        }

        private void TradePopupForm_Load(object sender, EventArgs e)
        {
            this.Location = (Point)Size.Subtract(Screen.GetWorkingArea(this).Size, this.Size);
        }

        private void btnAccept_Click(object sender, EventArgs e)
        {
            if (!accept2)
            {
                // Allow user to confirm first
                lblStatus.Text = "Нажмите «Принять» еще раз для подтверждения";
                btnAccept.BackColor = Color.FromArgb(128, 255, 128);
                accept2 = true;
            }
            else
            {
                lblStatus.Text = "Подтверждение...";
                PendingConfirmation request = requests[0];
                request.Account.AcceptConfirmation(request.Confirmation);
                requests.RemoveAt(0);
                Reset();
            }
        }

        private void btnDeny_Click(object sender, EventArgs e)
        {
            if (!deny2)
            {
                lblStatus.Text = "Нажмите «Отклонить» еще раз для подтверждения";
                btnDeny.BackColor = Color.FromArgb(255, 255, 128);
                deny2 = true;
            }
            else
            {
                lblStatus.Text = "Отклонение...";
                PendingConfirmation request = requests[0];
                request.Account.DenyConfirmation(request.Confirmation);
                requests.RemoveAt(0);
                Reset();
            }
        }

        private void Reset()
        {
            deny2 = false;
            accept2 = false;
            btnAccept.BackColor = Color.FromArgb(192, 255, 192);
            btnDeny.BackColor = Color.FromArgb(255, 255, 192);

            btnAccept.Text = "Принять";
            btnDeny.Text = "Отклонить";
            lblAccount.Text = "";
            lblStatus.Text = "";

            if (requests.Count == 0)
            {
                this.Hide();
            }
            else
            {
                acc = requests[0].Account;
                lblAccount.Text = acc.AccountName;
                //TODO: Re-add confirmation description support to SteamAuth.
                lblDesc.Text = "Подтверждение";
            }
        }

        public void Popup()
        {
            Reset();
            this.Show();
        }

        public sealed class PendingConfirmation
        {
            public PendingConfirmation(SteamGuardAccount account, Confirmation confirmation)
            {
                Account = account ?? throw new ArgumentNullException(nameof(account));
                Confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
            }

            public SteamGuardAccount Account { get; }

            public Confirmation Confirmation { get; }
        }
    }
}
