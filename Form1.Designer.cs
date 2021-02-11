namespace CoinbaseProToolsForm
{
	partial class Form1
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
			this.panel1 = new System.Windows.Forms.Panel();
			this.console = new System.Windows.Forms.RichTextBox();
			this.panel2 = new System.Windows.Forms.Panel();
			this.eventWindow = new System.Windows.Forms.RichTextBox();
			this.splitter1 = new System.Windows.Forms.Splitter();
			this.panel1.SuspendLayout();
			this.panel2.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.console);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Left;
			this.panel1.Location = new System.Drawing.Point(0, 0);
			this.panel1.Margin = new System.Windows.Forms.Padding(2);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(900, 454);
			this.panel1.TabIndex = 0;
			// 
			// console
			// 
			this.console.BackColor = System.Drawing.Color.Black;
			this.console.Dock = System.Windows.Forms.DockStyle.Fill;
			this.console.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.console.ForeColor = System.Drawing.Color.White;
			this.console.Location = new System.Drawing.Point(0, 0);
			this.console.Margin = new System.Windows.Forms.Padding(2);
			this.console.Name = "console";
			this.console.Size = new System.Drawing.Size(900, 454);
			this.console.TabIndex = 0;
			this.console.Text = "";
			this.console.KeyDown += new System.Windows.Forms.KeyEventHandler(this.console_KeyDown);
			// 
			// panel2
			// 
			this.panel2.Controls.Add(this.eventWindow);
			this.panel2.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel2.Location = new System.Drawing.Point(900, 0);
			this.panel2.Margin = new System.Windows.Forms.Padding(2);
			this.panel2.Name = "panel2";
			this.panel2.Size = new System.Drawing.Size(269, 454);
			this.panel2.TabIndex = 1;
			// 
			// eventWindow
			// 
			this.eventWindow.BackColor = System.Drawing.Color.Black;
			this.eventWindow.Dock = System.Windows.Forms.DockStyle.Fill;
			this.eventWindow.Font = new System.Drawing.Font("Consolas", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.eventWindow.ForeColor = System.Drawing.Color.White;
			this.eventWindow.Location = new System.Drawing.Point(0, 0);
			this.eventWindow.Margin = new System.Windows.Forms.Padding(2);
			this.eventWindow.Name = "eventWindow";
			this.eventWindow.ReadOnly = true;
			this.eventWindow.Size = new System.Drawing.Size(269, 454);
			this.eventWindow.TabIndex = 0;
			this.eventWindow.Text = "";
			// 
			// splitter1
			// 
			this.splitter1.Location = new System.Drawing.Point(900, 0);
			this.splitter1.Margin = new System.Windows.Forms.Padding(2);
			this.splitter1.Name = "splitter1";
			this.splitter1.Size = new System.Drawing.Size(2, 454);
			this.splitter1.TabIndex = 2;
			this.splitter1.TabStop = false;
			// 
			// Form1
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(1169, 454);
			this.Controls.Add(this.splitter1);
			this.Controls.Add(this.panel2);
			this.Controls.Add(this.panel1);
			this.Margin = new System.Windows.Forms.Padding(2);
			this.Name = "Form1";
			this.Text = "Form1";
			this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
			this.Load += new System.EventHandler(this.Form1_Load);
			this.panel1.ResumeLayout(false);
			this.panel2.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.Panel panel2;
		private System.Windows.Forms.Splitter splitter1;
		private System.Windows.Forms.RichTextBox console;
		private System.Windows.Forms.RichTextBox eventWindow;
	}
}

