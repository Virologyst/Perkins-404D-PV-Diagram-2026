using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace Engine_Practical
{
    partial class FormPV
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
            System.Windows.Forms.DataVisualization.Charting.ChartArea chartArea1 = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
            System.Windows.Forms.DataVisualization.Charting.Series series1 = new System.Windows.Forms.DataVisualization.Charting.Series();
            System.Windows.Forms.DataVisualization.Charting.Title title1 = new System.Windows.Forms.DataVisualization.Charting.Title();
            System.Windows.Forms.DataVisualization.Charting.ChartArea chartAreaPres = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
            System.Windows.Forms.DataVisualization.Charting.ChartArea chartAreaVol = new System.Windows.Forms.DataVisualization.Charting.ChartArea();

            this.mainLayout = new System.Windows.Forms.TableLayoutPanel();
            this.headerLabel = new System.Windows.Forms.Label();
            this.contentLayout = new System.Windows.Forms.TableLayoutPanel();
            this.chart1 = new System.Windows.Forms.DataVisualization.Charting.Chart();
            this.sidePanel = new System.Windows.Forms.TableLayoutPanel();
            this.chartPressureTime = new System.Windows.Forms.DataVisualization.Charting.Chart();
            this.chartVolumeTime = new System.Windows.Forms.DataVisualization.Charting.Chart();
            this.controlPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.button2 = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.buttonLoad = new System.Windows.Forms.Button();
            this.buttonStop = new System.Windows.Forms.Button();
            this.statusLabel = new System.Windows.Forms.Label();

            ((System.ComponentModel.ISupportInitialize)(this.chart1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.chartPressureTime)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.chartVolumeTime)).BeginInit();
            this.mainLayout.SuspendLayout();
            this.contentLayout.SuspendLayout();
            this.sidePanel.SuspendLayout();
            this.controlPanel.SuspendLayout();
            this.SuspendLayout();

            // MAIN LAYOUT
            this.mainLayout.ColumnCount = 1;
            this.mainLayout.RowCount = 3;
            this.mainLayout.Dock = DockStyle.Fill;
            this.mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            this.mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F));
            this.mainLayout.Controls.Add(this.headerLabel, 0, 0);
            this.mainLayout.Controls.Add(this.contentLayout, 0, 1);
            this.mainLayout.Controls.Add(this.controlPanel, 0, 2);

            // HEADER LABEL
            this.headerLabel.Dock = DockStyle.Fill;
            this.headerLabel.Text = "EGB322 Engine PV Diagram";
            this.headerLabel.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            this.headerLabel.TextAlign = ContentAlignment.MiddleCenter;
            this.headerLabel.BackColor = Color.FromArgb(0, 60, 113);
            this.headerLabel.ForeColor = Color.White;

            // CONTENT LAYOUT — main chart (75%) left, side charts (25%) right
            this.contentLayout.ColumnCount = 2;
            this.contentLayout.RowCount = 1;
            this.contentLayout.Dock = DockStyle.Fill;
            this.contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));
            this.contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            this.contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.contentLayout.Controls.Add(this.chart1, 0, 0);
            this.contentLayout.Controls.Add(this.sidePanel, 1, 0);

            // MAIN PV CHART
            chartArea1.Name = "ChartArea1";
            chartArea1.AxisX.Title = "Volume (m³)";
            chartArea1.AxisY.Title = "Pressure (bar)";
            this.chart1.ChartAreas.Add(chartArea1);
            this.chart1.Dock = DockStyle.Fill;
            this.chart1.BackColor = Color.WhiteSmoke;
            series1.ChartArea = "ChartArea1";
            series1.ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Line;
            series1.Name = "PV";
            this.chart1.Series.Add(series1);
            title1.Text = "P-V Diagram";
            title1.Font = new Font("Segoe UI", 20F);
            this.chart1.Titles.Add(title1);

            // SIDE PANEL — two stacked charts
            this.sidePanel.ColumnCount = 1;
            this.sidePanel.RowCount = 2;
            this.sidePanel.Dock = DockStyle.Fill;
            this.sidePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            this.sidePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            this.sidePanel.Controls.Add(this.chartPressureTime, 0, 0);
            this.sidePanel.Controls.Add(this.chartVolumeTime, 0, 1);

            // PRESSURE vs CRANK ANGLE CHART
            chartAreaPres.Name = "ChartAreaPres";
            chartAreaPres.AxisX.Title = "Crank Angle (°)";
            chartAreaPres.AxisY.Title = "Pressure";
            this.chartPressureTime.ChartAreas.Add(chartAreaPres);
            this.chartPressureTime.Dock = DockStyle.Fill;
            this.chartPressureTime.BackColor = Color.WhiteSmoke;
            var titlePres = new System.Windows.Forms.DataVisualization.Charting.Title();
            titlePres.Text = "Pressure vs Angle";
            titlePres.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.chartPressureTime.Titles.Add(titlePres);

            // VOLUME vs CRANK ANGLE CHART
            chartAreaVol.Name = "ChartAreaVol";
            chartAreaVol.AxisX.Title = "Crank Angle (°)";
            chartAreaVol.AxisY.Title = "Volume (cm³)";
            this.chartVolumeTime.ChartAreas.Add(chartAreaVol);
            this.chartVolumeTime.Dock = DockStyle.Fill;
            this.chartVolumeTime.BackColor = Color.WhiteSmoke;
            var titleVol = new System.Windows.Forms.DataVisualization.Charting.Title();
            titleVol.Text = "Volume vs Angle";
            titleVol.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            this.chartVolumeTime.Titles.Add(titleVol);

            // CONTROL PANEL
            this.controlPanel.Dock = DockStyle.Fill;
            this.controlPanel.FlowDirection = FlowDirection.LeftToRight;
            this.controlPanel.Padding = new Padding(20);
            this.controlPanel.Controls.Add(this.button2);
            this.controlPanel.Controls.Add(this.button1);
            this.controlPanel.Controls.Add(this.statusLabel);
            this.controlPanel.Controls.Add(this.buttonStop);
            this.controlPanel.Controls.Add(this.buttonLoad);

            // START BUTTON
            this.button2.Text = "Start";
            this.button2.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            this.button2.Width = 250;
            this.button2.Height = 50;
            this.button2.BackColor = Color.FromArgb(0, 60, 113);
            this.button2.ForeColor = Color.White;
            this.button2.Click += new System.EventHandler(this.button2_Click);

            // SAVE BUTTON
            this.button1.Text = "Save Data";
            this.button1.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            this.button1.Width = 180;
            this.button1.Height = 50;
            this.button1.Click += new System.EventHandler(this.button1_Click_1);

            // STOP BUTTON
            this.buttonStop.Name = "buttonStop";
            this.buttonStop.Text = "Stop";
            this.buttonStop.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            this.buttonStop.Width = 180;
            this.buttonStop.Height = 50;
            this.buttonStop.BackColor = Color.FromArgb(255, 0, 0);
            this.buttonStop.ForeColor = Color.White;
            this.buttonStop.Click += new System.EventHandler(this.buttonStop_Click);

            // LOAD BUTTON
            this.buttonLoad.Text = "Load";
            this.buttonLoad.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            this.buttonLoad.Width = 180;
            this.buttonLoad.Height = 50;
            this.buttonLoad.Click += new System.EventHandler(this.buttonLoad_Click);

            // Start hidden
            this.buttonStop.Visible = false;

            // STATUS LABEL
            this.statusLabel.Text = "Ready";
            this.statusLabel.Font = new Font("Segoe UI", 12F);
            this.statusLabel.AutoSize = true;
            this.statusLabel.Padding = new Padding(30, 10, 0, 0);

            // FORM
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 800);
            this.Controls.Add(this.mainLayout);
            this.Text = "Engine Practical PV Diagram";

            ((System.ComponentModel.ISupportInitialize)(this.chart1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.chartPressureTime)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.chartVolumeTime)).EndInit();
            this.mainLayout.ResumeLayout(false);
            this.contentLayout.ResumeLayout(false);
            this.sidePanel.ResumeLayout(false);
            this.controlPanel.ResumeLayout(false);
            this.controlPanel.PerformLayout();
            this.ResumeLayout(false);
        }
        
        

        #endregion
        
        private System.Windows.Forms.TableLayoutPanel mainLayout;
        private System.Windows.Forms.TableLayoutPanel contentLayout;
        private System.Windows.Forms.TableLayoutPanel sidePanel;
        private System.Windows.Forms.FlowLayoutPanel controlPanel;
        private System.Windows.Forms.Label headerLabel;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button buttonStop;
        private System.Windows.Forms.Button buttonLoad;
        private System.Windows.Forms.DataVisualization.Charting.Chart chart1;
        private System.Windows.Forms.DataVisualization.Charting.Chart chartPressureTime;
        private System.Windows.Forms.DataVisualization.Charting.Chart chartVolumeTime;
        
        
    }
}

