namespace Muter
{
    partial class Form1
    {
        /// <summary>
        ///Gerekli tasarımcı değişkeni.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///Kullanılan tüm kaynakları temizleyin.
        /// </summary>
        ///<param name="disposing">yönetilen kaynaklar dispose edilmeliyse doğru; aksi halde yanlış.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer üretilen kod

        /// <summary>
        /// Tasarımcı desteği için gerekli metot - bu metodun 
        ///içeriğini kod düzenleyici ile değiştirmeyin.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.notifyIcon1 = new System.Windows.Forms.NotifyIcon(this.components);
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.runStartup = new System.Windows.Forms.ToolStripMenuItem();
            this.hotkeyForm = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fadeOutTimer = new System.Windows.Forms.Timer(this.components);
            this.title = new System.Windows.Forms.Label();
            this.toggleText = new System.Windows.Forms.Label();
            this.bottomBar = new System.Windows.Forms.Panel();
            this.topBar = new System.Windows.Forms.Panel();
            this.microphoneChecker = new System.Windows.Forms.Timer(this.components);
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.contextMenuStrip1.SuspendLayout();
            this.bottomBar.SuspendLayout();
            this.topBar.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // notifyIcon1
            // 
            this.notifyIcon1.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
            this.notifyIcon1.ContextMenuStrip = this.contextMenuStrip1;
            this.notifyIcon1.Icon = ((System.Drawing.Icon)(resources.GetObject("notifyIcon1.Icon")));
            this.notifyIcon1.Text = "Muter";
            this.notifyIcon1.Visible = true;
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.runStartup,
            this.hotkeyForm,
            this.exitToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(138, 70);
            // 
            // runStartup
            // 
            this.runStartup.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
            this.runStartup.Name = "runStartup";
            this.runStartup.Size = new System.Drawing.Size(137, 22);
            this.runStartup.Text = "Run Startup";
            this.runStartup.Click += new System.EventHandler(this.runStartupToolStripMenuItem_Click);
            // 
            // hotkeyForm
            // 
            this.hotkeyForm.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
            this.hotkeyForm.Name = "hotkeyForm";
            this.hotkeyForm.Size = new System.Drawing.Size(137, 22);
            this.hotkeyForm.Text = "Hotkey";
            this.hotkeyForm.Click += new System.EventHandler(this.hotkeyForm_Click);
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(137, 22);
            this.exitToolStripMenuItem.Text = "Exit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
            // 
            // fadeOutTimer
            // 
            this.fadeOutTimer.Enabled = true;
            this.fadeOutTimer.Interval = 25;
            this.fadeOutTimer.Tick += new System.EventHandler(this.fadeOutTimer_Tick_1);
            // 
            // title
            // 
            this.title.AutoSize = true;
            this.title.BackColor = System.Drawing.Color.Black;
            this.title.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold);
            this.title.ForeColor = System.Drawing.Color.White;
            this.title.Location = new System.Drawing.Point(19, 2);
            this.title.Name = "title";
            this.title.Size = new System.Drawing.Size(85, 19);
            this.title.TabIndex = 5;
            this.title.Text = "Microphone";
            // 
            // toggleText
            // 
            this.toggleText.AutoSize = true;
            this.toggleText.BackColor = System.Drawing.Color.Black;
            this.toggleText.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold);
            this.toggleText.ForeColor = System.Drawing.Color.White;
            this.toggleText.Location = new System.Drawing.Point(46, 2);
            this.toggleText.Name = "toggleText";
            this.toggleText.Size = new System.Drawing.Size(31, 19);
            this.toggleText.TabIndex = 6;
            this.toggleText.Text = "ON";
            // 
            // bottomBar
            // 
            this.bottomBar.BackColor = System.Drawing.Color.Black;
            this.bottomBar.Controls.Add(this.toggleText);
            this.bottomBar.Location = new System.Drawing.Point(12, 140);
            this.bottomBar.Name = "bottomBar";
            this.bottomBar.Size = new System.Drawing.Size(122, 23);
            this.bottomBar.TabIndex = 7;
            // 
            // topBar
            // 
            this.topBar.BackColor = System.Drawing.Color.Black;
            this.topBar.Controls.Add(this.title);
            this.topBar.Location = new System.Drawing.Point(12, 12);
            this.topBar.Name = "topBar";
            this.topBar.Size = new System.Drawing.Size(122, 23);
            this.topBar.TabIndex = 8;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackColor = System.Drawing.Color.Black;
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.pictureBox1.Location = new System.Drawing.Point(12, 40);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(122, 96);
            this.pictureBox1.TabIndex = 1;
            this.pictureBox1.TabStop = false;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Lime;
            this.ClientSize = new System.Drawing.Size(146, 173);
            this.Controls.Add(this.topBar);
            this.Controls.Add(this.bottomBar);
            this.Controls.Add(this.pictureBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form1";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Muter";
            this.TopMost = true;
            this.TransparencyKey = System.Drawing.Color.Lime;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing_1);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.contextMenuStrip1.ResumeLayout(false);
            this.bottomBar.ResumeLayout(false);
            this.bottomBar.PerformLayout();
            this.topBar.ResumeLayout(false);
            this.topBar.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.NotifyIcon notifyIcon1;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem runStartup;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem hotkeyForm;
        private System.Windows.Forms.Timer fadeOutTimer;
        private System.Windows.Forms.Label title;
        private System.Windows.Forms.Label toggleText;
        private System.Windows.Forms.Panel bottomBar;
        private System.Windows.Forms.Panel topBar;
        private System.Windows.Forms.Timer microphoneChecker;
    }
}

