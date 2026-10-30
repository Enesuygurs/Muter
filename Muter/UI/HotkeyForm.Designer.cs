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
            this.iconBox.Location = new System.Drawing.Point(16, 11);
            this.iconBox.Name = "iconBox";
            this.iconBox.Size = new System.Drawing.Size(20, 20);
            this.iconBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.iconBox.TabIndex = 0;
            this.iconBox.TabStop = false;
            this.iconBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.TitleBar_MouseDown);
            //
            // titleLabel
            //
            this.titleLabel.AutoSize = true;
            this.titleLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 11F, System.Drawing.FontStyle.Bold);
            this.titleLabel.ForeColor = System.Drawing.Color.FromArgb(243, 243, 243);
            this.titleLabel.Location = new System.Drawing.Point(42, 11);
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
            this.closeButton.Size = new System.Drawing.Size(46, 32);
            this.closeButton.TabIndex = 7;
            this.closeButton.TabStop = false;
            this.closeButton.Text = "";
            //
            // descriptionLabel
            //
            this.descriptionLabel.ForeColor = System.Drawing.Color.FromArgb(167, 167, 167);
            this.descriptionLabel.Location = new System.Drawing.Point(16, 46);
            this.descriptionLabel.Name = "descriptionLabel";
            this.descriptionLabel.Size = new System.Drawing.Size(348, 36);
            this.descriptionLabel.TabIndex = 2;
            this.descriptionLabel.Text = "Press the keys you want to use to mute and unmute your microphone from any app.";
            //
            // captureBox
            //
            this.captureBox.Font = new System.Drawing.Font("Segoe UI Semibold", 10.5F, System.Drawing.FontStyle.Bold);
            this.captureBox.Location = new System.Drawing.Point(16, 86);
            this.captureBox.Name = "captureBox";
            this.captureBox.Size = new System.Drawing.Size(348, 78);
            this.captureBox.TabIndex = 0;
            //
            // statusLabel
            //
            this.statusLabel.ForeColor = System.Drawing.Color.FromArgb(167, 167, 167);
            this.statusLabel.Location = new System.Drawing.Point(16, 172);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(348, 34);
            this.statusLabel.TabIndex = 3;
            //
            // resetButton
            //
            this.resetButton.ButtonStyle = Muter.UI.Controls.FlatButtonStyle.Link;
            this.resetButton.Location = new System.Drawing.Point(16, 214);
            this.resetButton.Name = "resetButton";
            this.resetButton.Size = new System.Drawing.Size(112, 28);
            this.resetButton.TabIndex = 4;
            this.resetButton.Text = "Reset to default";
            this.resetButton.Click += new System.EventHandler(this.ResetButton_Click);
            //
            // cancelButton
            //
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
            this.cancelButton.Location = new System.Drawing.Point(196, 212);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(80, 32);
            this.cancelButton.TabIndex = 5;
            this.cancelButton.Text = "Cancel";
            //
            // saveButton
            //
            this.saveButton.ButtonStyle = Muter.UI.Controls.FlatButtonStyle.Primary;
            this.saveButton.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
            this.saveButton.Location = new System.Drawing.Point(284, 212);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(80, 32);
            this.saveButton.TabIndex = 6;
            this.saveButton.Text = "Save";
            this.saveButton.Click += new System.EventHandler(this.SaveButton_Click);
            //
            // HotkeyForm
            //
            this.AcceptButton = this.saveButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(31, 31, 31);
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(380, 258);
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
            this.ForeColor = System.Drawing.Color.FromArgb(243, 243, 243);
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
