﻿using System;
using System.Windows.Forms;

namespace AutoModPlugins.GUI
{
    partial class SimpleHexEditor
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
            this.B_Update = new System.Windows.Forms.Button();
            this.PG_BlockView = new System.Windows.Forms.PropertyGrid();
            this.CB_AutoRefresh = new System.Windows.Forms.CheckBox();
            this.CB_CopyMethod = new System.Windows.Forms.ComboBox();
            this.RTB_RAM = new AutoModPlugins.GUI.HexRichTextBox();
            this.RT_Label = new System.Windows.Forms.Label();
            this.RT_Timer = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.RT_Timer)).BeginInit();
            this.SuspendLayout();
            // 
            // B_Update
            // 
            this.B_Update.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.B_Update.Location = new System.Drawing.Point(9, 308);
            this.B_Update.Name = "B_Update";
            this.B_Update.Size = new System.Drawing.Size(114, 26);
            this.B_Update.TabIndex = 1;
            this.B_Update.Text = "Update";
            this.B_Update.UseVisualStyleBackColor = true;
            this.B_Update.Click += new System.EventHandler(this.Update_Click);
            // 
            // PG_BlockView
            // 
            this.PG_BlockView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.PG_BlockView.Location = new System.Drawing.Point(9, 10);
            this.PG_BlockView.Name = "PG_BlockView";
            this.PG_BlockView.Size = new System.Drawing.Size(114, 130);
            this.PG_BlockView.TabIndex = 15;
            this.PG_BlockView.Visible = false;
            // 
            // CB_AutoRefresh
            // 
            this.CB_AutoRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CB_AutoRefresh.AutoSize = true;
            this.CB_AutoRefresh.Location = new System.Drawing.Point(319, 314);
            this.CB_AutoRefresh.Name = "CB_AutoRefresh";
            this.CB_AutoRefresh.Size = new System.Drawing.Size(63, 17);
            this.CB_AutoRefresh.TabIndex = 16;
            this.CB_AutoRefresh.Text = "Refresh";
            this.CB_AutoRefresh.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.CB_AutoRefresh.UseVisualStyleBackColor = true;
            this.CB_AutoRefresh.CheckedChanged += new System.EventHandler(this.CB_AutoRefresh_CheckedChanged);
            // 
            // CB_CopyMethod
            // 
            this.CB_CopyMethod.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CB_CopyMethod.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.CB_CopyMethod.FormattingEnabled = true;
            this.CB_CopyMethod.Location = new System.Drawing.Point(131, 312);
            this.CB_CopyMethod.Name = "CB_CopyMethod";
            this.CB_CopyMethod.Size = new System.Drawing.Size(92, 21);
            this.CB_CopyMethod.TabIndex = 17;
            this.CB_CopyMethod.SelectedIndexChanged += new System.EventHandler(this.ChangeCopyMethod);
            // 
            // RTB_RAM
            // 
            this.RTB_RAM.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.RTB_RAM.Font = new System.Drawing.Font("Courier New", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RTB_RAM.Location = new System.Drawing.Point(9, 10);
            this.RTB_RAM.Name = "RTB_RAM";
            this.RTB_RAM.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.ForcedVertical;
            this.RTB_RAM.Size = new System.Drawing.Size(312, 292);
            this.RTB_RAM.TabIndex = 0;
            this.RTB_RAM.Text = "";
            // 
            // RT_Label
            // 
            this.RT_Label.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.RT_Label.AutoSize = true;
            this.RT_Label.Location = new System.Drawing.Point(226, 316);
            this.RT_Label.Name = "RT_Label";
            this.RT_Label.Size = new System.Drawing.Size(33, 13);
            this.RT_Label.TabIndex = 19;
            this.RT_Label.Text = "Timer";
            // 
            // RT_Timer
            // 
            this.RT_Timer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.RT_Timer.Location = new System.Drawing.Point(261, 312);
            this.RT_Timer.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.RT_Timer.Name = "RT_Timer";
            this.RT_Timer.Size = new System.Drawing.Size(52, 20);
            this.RT_Timer.TabIndex = 20;
            this.RT_Timer.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // SimpleHexEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(384, 343);
            this.Controls.Add(this.RT_Timer);
            this.Controls.Add(this.RT_Label);
            this.Controls.Add(this.CB_CopyMethod);
            this.Controls.Add(this.CB_AutoRefresh);
            this.Controls.Add(this.PG_BlockView);
            this.Controls.Add(this.B_Update);
            this.Controls.Add(this.RTB_RAM);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SimpleHexEditor";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "RAMEdit";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SimpleHexEditor_FormClosing);
            ((System.ComponentModel.ISupportInitialize)(this.RT_Timer)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }
        #endregion

        private HexRichTextBox RTB_RAM;
        private Button B_Update;
        public PropertyGrid PG_BlockView;
        private CheckBox CB_AutoRefresh;
        private ComboBox CB_CopyMethod;
        private Label RT_Label;
        private NumericUpDown RT_Timer;
    }
}
