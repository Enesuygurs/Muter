namespace Muter.UI
{
    partial class HotkeyForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.iconBox = new System.Windows.Forms.PictureBox();
            this.titleLabel = new System.Windows.Forms.Label();
            this.closeButton = new Muter.UI.Controls.FlatButton();
            this.descriptionLabel = new System.Windows.Forms.Label();
            this.captureBox = new Muter.UI.Controls.KeyCaptureBox();
            this.statusLabel = new System.Windows.Forms.Label();
            this.resetButton = new Muter.UI.Controls.FlatButton();
            this.cancelButton = new Muter.UI.Controls.FlatButton();
            this.saveButton = new Muter.UI.Controls.FlatButton();
            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).BeginInit();
            this.SuspendLayout();
            //
            // iconBox
            //
            this.iconBox.Location = new System.Drawing.Point(14, 12);
            this.iconBox.Name = "iconBox";
            this.iconBox.Size = new System.Drawing.Size(16, 16);
            this.iconBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.iconBox.TabIndex = 0;
            this.iconBox.TabStop = false;
            this.iconBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.TitleBar_MouseDown);
            //
            // titleLabel
            //
            this.titleLabel.AutoSize = true;
            this.titleLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.titleLabel.ForeColor = System.Drawing.Color.FromArgb(232, 234, 237);
            this.titleLabel.Location = new System.Drawing.Point(38, 12);
            this.titleLabel.Name = "titleLabel";
            this.titleLabel.Size = new System.Drawing.Size(94, 20);
            this.titleLabel.TabIndex = 1;
            this.titleLabel.Text = "Mute hotkey";
            this.titleLabel.MouseDown += new System.Windows.Forms.MouseEventHandler(this.TitleBar_MouseDown);
            //
            // closeButton
            //
            this.closeButton.ButtonStyle = Muter.UI.Controls.FlatButtonStyle.Close;
            this.closeButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.closeButton.Font = new System.Drawing.Font("Segoe MDL2 Assets", 8F);
            this.closeButton.Location = new System.Drawing.Point(333, 1);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(46, 39);
            this.closeButton.TabIndex = 7;
            this.closeButton.TabStop = false;
            this.closeButton.Text = "";
            //
            // descriptionLabel
            //
            this.descriptionLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F);
            this.descriptionLabel.ForeColor = System.Drawing.Color.FromArgb(232, 234, 237);
            this.descriptionLabel.Location = new System.Drawing.Point(24, 48);
            this.descriptionLabel.Name = "descriptionLabel";
            this.descriptionLabel.Size = new System.Drawing.Size(332, 36);
            this.descriptionLabel.TabIndex = 2;
            this.descriptionLabel.Text = "Press the keys you want to use to mute and unmute your microphone from any app.";
            //
            // captureBox
            //
            this.captureBox.Font = new System.Drawing.Font("Segoe UI Semibold", 11F);
            this.captureBox.Location = new System.Drawing.Point(24, 100);
            this.captureBox.Name = "captureBox";
            this.captureBox.Size = new System.Drawing.Size(332, 96);
            this.captureBox.TabIndex = 0;
            //
            // statusLabel
            //
            this.statusLabel.ForeColor = System.Drawing.Color.FromArgb(160, 166, 176);
            this.statusLabel.Location = new System.Drawing.Point(48, 208);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Padding = new System.Windows.Forms.Padding(0, 1, 0, 0);
            this.statusLabel.Size = new System.Drawing.Size(308, 34);
            this.statusLabel.TabIndex = 3;
            //
            // resetButton
            //
            this.resetButton.ButtonStyle = Muter.UI.Controls.FlatButtonStyle.Link;
            this.resetButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.resetButton.Location = new System.Drawing.Point(24, 278);
            this.resetButton.Name = "resetButton";
            this.resetButton.Size = new System.Drawing.Size(112, 28);
            this.resetButton.TabIndex = 4;
            this.resetButton.Text = "Reset to default";
            this.resetButton.Click += new System.EventHandler(this.ResetButton_Click);
            //
            // cancelButton
            //
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.cancelButton.Location = new System.Drawing.Point(172, 278);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(88, 32);
            this.cancelButton.TabIndex = 5;
            this.cancelButton.Text = "Cancel";
            //
            // saveButton
            //
            this.saveButton.ButtonStyle = Muter.UI.Controls.FlatButtonStyle.Primary;
            this.saveButton.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.saveButton.Location = new System.Drawing.Point(268, 278);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(88, 32);
            this.saveButton.TabIndex = 6;
            this.saveButton.Text = "Save";
            this.saveButton.Click += new System.EventHandler(this.SaveButton_Click);
            //
            // HotkeyForm
            //
            this.AcceptButton = this.saveButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(20, 22, 26);
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(380, 326);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.resetButton);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.captureBox);
            this.Controls.Add(this.descriptionLabel);
            this.Controls.Add(this.closeButton);
            this.Controls.Add(this.titleLabel);
            this.Controls.Add(this.iconBox);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.ForeColor = System.Drawing.Color.FromArgb(232, 234, 237);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "HotkeyForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Muter Hotkey";
            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox iconBox;
        private System.Windows.Forms.Label titleLabel;
        private Muter.UI.Controls.FlatButton closeButton;
        private System.Windows.Forms.Label descriptionLabel;
        private Muter.UI.Controls.KeyCaptureBox captureBox;
        private System.Windows.Forms.Label statusLabel;
        private Muter.UI.Controls.FlatButton resetButton;
        private Muter.UI.Controls.FlatButton cancelButton;
        private Muter.UI.Controls.FlatButton saveButton;
    }
}
