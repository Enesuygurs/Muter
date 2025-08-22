namespace Muter
{
    partial class hotkeyForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(hotkeyForm));
            this.key = new System.Windows.Forms.TextBox();
            this.setButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.topPanel = new System.Windows.Forms.Panel();
            this.closeButton = new System.Windows.Forms.Label();
            this.title = new System.Windows.Forms.Label();
            this.topPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // key
            // 
            this.key.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.key.CharacterCasing = System.Windows.Forms.CharacterCasing.Upper;
            this.key.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold);
            this.key.ForeColor = System.Drawing.Color.Black;
            this.key.Location = new System.Drawing.Point(45, 54);
            this.key.Margin = new System.Windows.Forms.Padding(0);
            this.key.Name = "key";
            this.key.ReadOnly = true;
            this.key.Size = new System.Drawing.Size(119, 18);
            this.key.TabIndex = 0;
            this.key.Text = "SHIFT + F";
            this.key.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.key.Click += new System.EventHandler(this.key_Click);
            this.key.KeyDown += new System.Windows.Forms.KeyEventHandler(this.key_KeyDown);
            // 
            // setButton
            // 
            this.setButton.BackColor = System.Drawing.Color.OrangeRed;
            this.setButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.setButton.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.setButton.ForeColor = System.Drawing.SystemColors.Control;
            this.setButton.Location = new System.Drawing.Point(45, 77);
            this.setButton.Name = "setButton";
            this.setButton.Size = new System.Drawing.Size(119, 26);
            this.setButton.TabIndex = 1;
            this.setButton.Text = "Set Key";
            this.setButton.UseVisualStyleBackColor = false;
            this.setButton.Click += new System.EventHandler(this.setButton_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold);
            this.label1.Location = new System.Drawing.Point(88, 29);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(32, 19);
            this.label1.TabIndex = 2;
            this.label1.Text = "Key";
            // 
            // topPanel
            // 
            this.topPanel.BackColor = System.Drawing.Color.OrangeRed;
            this.topPanel.Controls.Add(this.closeButton);
            this.topPanel.Controls.Add(this.title);
            this.topPanel.Location = new System.Drawing.Point(0, 0);
            this.topPanel.Name = "topPanel";
            this.topPanel.Size = new System.Drawing.Size(208, 20);
            this.topPanel.TabIndex = 3;
            this.topPanel.MouseDown += new System.Windows.Forms.MouseEventHandler(this.DraggableControl_MouseDown);
            // 
            // closeButton
            // 
            this.closeButton.AutoSize = true;
            this.closeButton.Font = new System.Drawing.Font("MS UI Gothic", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.closeButton.ForeColor = System.Drawing.Color.White;
            this.closeButton.Location = new System.Drawing.Point(190, 3);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(16, 13);
            this.closeButton.TabIndex = 5;
            this.closeButton.Text = "X";
            this.closeButton.Click += new System.EventHandler(this.closeButton_Click);
            // 
            // title
            // 
            this.title.AutoSize = true;
            this.title.Font = new System.Drawing.Font("MS UI Gothic", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.title.ForeColor = System.Drawing.Color.White;
            this.title.Location = new System.Drawing.Point(78, 3);
            this.title.Name = "title";
            this.title.Size = new System.Drawing.Size(52, 13);
            this.title.TabIndex = 4;
            this.title.Text = "Hotkey";
            this.title.MouseDown += new System.Windows.Forms.MouseEventHandler(this.DraggableControl_MouseDown);
            // 
            // hotkeyForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(208, 120);
            this.Controls.Add(this.topPanel);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.setButton);
            this.Controls.Add(this.key);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximumSize = new System.Drawing.Size(208, 120);
            this.MinimumSize = new System.Drawing.Size(208, 120);
            this.Name = "hotkeyForm";
            this.Text = "Hotkey";
            this.Load += new System.EventHandler(this.hotkeyForm_Load);
            this.topPanel.ResumeLayout(false);
            this.topPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox key;
        private System.Windows.Forms.Button setButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Panel topPanel;
        private System.Windows.Forms.Label title;
        private System.Windows.Forms.Label closeButton;
    }
}