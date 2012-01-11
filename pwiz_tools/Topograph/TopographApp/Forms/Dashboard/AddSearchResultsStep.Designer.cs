﻿namespace pwiz.Topograph.ui.Forms.Dashboard
{
    partial class AddSearchResultsStep
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.labelDescription = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnAddSearchResults = new System.Windows.Forms.Button();
            this.panel1 = new System.Windows.Forms.Panel();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // labelDescription
            // 
            this.labelDescription.AutoSize = true;
            this.labelDescription.Dock = System.Windows.Forms.DockStyle.Top;
            this.labelDescription.Location = new System.Drawing.Point(0, 0);
            this.labelDescription.Name = "labelDescription";
            this.labelDescription.Size = new System.Drawing.Size(424, 13);
            this.labelDescription.TabIndex = 1;
            this.labelDescription.Text = "Topograph reads search results from peptide search engines such as Sequest or Mas" +
                "cot";
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblStatus.Location = new System.Drawing.Point(0, 13);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(303, 13);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.Text = "No peptide search results have been added to this workspace.";
            // 
            // btnAddSearchResults
            // 
            this.btnAddSearchResults.AutoSize = true;
            this.btnAddSearchResults.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnAddSearchResults.Location = new System.Drawing.Point(0, 0);
            this.btnAddSearchResults.Name = "btnAddSearchResults";
            this.btnAddSearchResults.Size = new System.Drawing.Size(120, 23);
            this.btnAddSearchResults.TabIndex = 3;
            this.btnAddSearchResults.Text = "Add Search Results...";
            this.btnAddSearchResults.UseVisualStyleBackColor = true;
            this.btnAddSearchResults.Click += new System.EventHandler(this.btnAddSearchResults_Click);
            // 
            // panel1
            // 
            this.panel1.AutoSize = true;
            this.panel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.panel1.Controls.Add(this.btnAddSearchResults);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel1.Location = new System.Drawing.Point(0, 26);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(458, 26);
            this.panel1.TabIndex = 4;
            // 
            // AddSearchResultsStep
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.labelDescription);
            this.Name = "AddSearchResultsStep";
            this.Size = new System.Drawing.Size(458, 189);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label labelDescription;
        private System.Windows.Forms.Button btnAddSearchResults;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Panel panel1;
    }
}
