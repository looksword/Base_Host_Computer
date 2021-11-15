namespace Base.Browser
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.text_Msg = new System.Windows.Forms.TextBox();
            this.text_error = new System.Windows.Forms.TextBox();
            this.groupBox5.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.text_Msg);
            this.groupBox5.Location = new System.Drawing.Point(560, 5);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(271, 348);
            this.groupBox5.TabIndex = 52;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "信息";
            // 
            // text_Msg
            // 
            this.text_Msg.Dock = System.Windows.Forms.DockStyle.Fill;
            this.text_Msg.Location = new System.Drawing.Point(3, 17);
            this.text_Msg.Multiline = true;
            this.text_Msg.Name = "text_Msg";
            this.text_Msg.ReadOnly = true;
            this.text_Msg.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.text_Msg.Size = new System.Drawing.Size(265, 328);
            this.text_Msg.TabIndex = 18;
            // 
            // text_error
            // 
            this.text_error.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.text_error.Location = new System.Drawing.Point(0, 362);
            this.text_error.Name = "text_error";
            this.text_error.ReadOnly = true;
            this.text_error.Size = new System.Drawing.Size(843, 21);
            this.text_error.TabIndex = 53;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(843, 383);
            this.Controls.Add(this.text_error);
            this.Controls.Add(this.groupBox5);
            this.Name = "Form1";
            this.Text = "Browser";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.groupBox5.ResumeLayout(false);
            this.groupBox5.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.TextBox text_Msg;
        private System.Windows.Forms.TextBox text_error;
    }
}

