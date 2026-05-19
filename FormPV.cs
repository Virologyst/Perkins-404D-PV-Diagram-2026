using NationalInstruments.DAQmx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using DaqTask = NationalInstruments.DAQmx.Task;
using ThreadTask = System.Threading.Tasks.Task;
using System.Threading;


namespace Engine_Practical
{
	public partial class FormPV : Form
	
	{
		// --------------------------------------------------------------------------------
		// Switch between pressure units
		// --------------------------------------------------------------------------------
		private enum PressureUnit
		{
			Bar,
			KPa,
			PSI
		}
		
		// --------------------------------------------------------------------------------
		// Define Stroke Stages
		// --------------------------------------------------------------------------------
		private enum Stroke
		{
			Intake,	  // 0–180
			Compression, // 180–360
			Power,	   // 360–540
			Exhaust	  // 540–720
		}
		
		// --------------------------------------------------------------------------------
		// Pressure signal phase calibration
		// --------------------------------------------------------------------------------
		// The analog pressure measurement chain (Kistler 6056B → charge amp → PCB 482B06 → 
		// NI 9223) introduces a small but measurable delay between the actual cylinder 
		// pressure event and the voltage arriving at the DAQ. This manifests on the P-V 
		// diagram as peak pressure appearing too far to the right of TDC (at larger volumes 
		// than physically expected).
		//
		// For a diesel under normal operation, peak pressure should occur approximately
		// 5–15° after compression TDC. If the peak appears further advanced (later) than 
		// this on the diagram, increase PRESSURE_LAG_DEGREES to shift the pressure trace 
		// back toward TDC.
		//
		// Tuning:
		//   - Start at 0 and observe where peak pressure falls on the PV diagram
		//   - Increase by 5° at a time until peak sits 5–15° ATDC (just right of TDC marker)
		//   - A value of ~40–50° is typical for this sensor chain
		//
		//
		// This shift is applied by subtracting PRESSURE_LAG_DEGREES from each pressure 
		// sample's crank angle before plotting.
		//
		private const double PRESSURE_LAG_DEGREES = 35.0;
		
		
		// --------------------------------------------------------------------------------
		// BDC Pegging reference pressure
		// --------------------------------------------------------------------------------
		// Piezoelectric pressure sensors through AC-coupled charge amplifiers measure
		// changes in pressure, not absolute pressure. The DC component is filtered out,
		// leaving a signal centered around zero volts rather than atmospheric pressure.
		//
		// BDC Pegging compensates for this by assuming that at intake BDC (180°), the
		// cylinder pressure equals atmospheric pressure. The voltage reading at that
		// angle is used as the zero-reference for the entire cycle, re-anchoring all
		// pressure readings to the absolute scale.
		//
		// For engines running at altitude or in non-standard atmospheric conditions,
		// adjust this value to the local ambient pressure in bar.
		//   - Sea level / standard conditions: 1.01325 bar
		//   - At 500 m elevation: ~0.955 bar
		//   - At 1000 m elevation: ~0.899 bar
		//
		private const double ATMOSPHERIC_PRESSURE_BAR = 1.01325;

		// --------------------------------------------------------------------------------
		// Define Stroke Angles
		// --------------------------------------------------------------------------------
		private Stroke GetStroke(double angle)
		{
			if (angle < 180) return Stroke.Intake;
			if (angle < 360) return Stroke.Compression;
			if (angle < 540) return Stroke.Power;
			return Stroke.Exhaust;
		}

		private PressureUnit currentUnit = PressureUnit.Bar;
		
		// --------------------------------------------------------------------------------
		// Live labels
		// --------------------------------------------------------------------------------
		private Label lblProbe;
		private Label lblRPM;
		
		// --------------------------------------------------------------------------------
		// Graphing
		// --------------------------------------------------------------------------------
		public string series = "PV";
		private DateTime lastUiUpdate = DateTime.MinValue;
		
		// --------------------------------------------------------------------------------
		// State flags — tracks which one-shot messages have already been shown
		// --------------------------------------------------------------------------------
		private readonly HashSet<string> _shownMessages = new HashSet<string>();
		
		// --------------------------------------------------------------------------------
		// Data collection flags
		// --------------------------------------------------------------------------------
		public bool RunDataCollection = false;
		private bool isAcquiring = false;
		private double _currentRpm = 0;
		
		// --------------------------------------------------------------------------------
		// National Instruments DAQ setup
		// --------------------------------------------------------------------------------
		public DaqTask DAQ_Task_TDC;
		public DaqTask DAQ_Task_Pres;
		
		private AnalogSingleChannelReader AnalogReader = null;
		private DigitalSingleChannelReader DigitalReader = null;
		
		// --------------------------------------------------------------------------------
		// Data collection configuration
		// --------------------------------------------------------------------------------
		public const int SAMPLES = 10000;
		public const int SAMPLE_PER_SECOND = SAMPLES;
		
		private const string SHARED_SAMPLE_CLOCK_SOURCE = "/Pressure/ai/SampleClock";

		// --------------------------------------------------------------------------------
		// Pressure scaling
		// --------------------------------------------------------------------------------
		private const double CHARGE_AMP_ZERO_BAR_VOLTS = 0.0;
		private const double CHARGE_AMP_BAR_PER_VOLT = 58.82;

		// --------------------------------------------------------------------------------
		// Raw data buffers
		// --------------------------------------------------------------------------------
		private double[] aData = null;
		private Int16[] dData = null;

		// --------------------------------------------------------------------------------
		// Processed storage for plotting and save
		// --------------------------------------------------------------------------------
		public List<double> listX = new List<double>(); // volume m^3
		public List<double> listY = new List<double>(); // pressure bar
		public List<double> listAngle = new List<double>(); // crank angle degrees (phase-corrected)
		public List<double> listPressureVolts = new List<double>(); // raw pressure volts
		public List<double> listRawAngle = new List<double>(); // raw angle before phase correction

		// --------------------------------------------------------------------------------
		// Temporary processing storage
		// --------------------------------------------------------------------------------
		public List<int> TDCList = new List<int>();
		public List<int> IndexList = new List<int>();
		public List<int> Tracker = new List<int>();
		public List<double> allIndex = new List<double>();
		public List<double> allIndexAngle = new List<double>();

		// --------------------------------------------------------------------------------
		// Engine geometry
		// --------------------------------------------------------------------------------
		private const double CLEARANCE_VOLUME_M3 = 0.00002485;
		private const double DISPLACEMENT_VOLUME_M3 = 0.0002771;
		private const double ROD_TO_CRANK_RATIO = 4.3;

		// --------------------------------------------------------------------------------
		// Engine RPM Calculator
		// --------------------------------------------------------------------------------
		private double CalculateRPM(List<int> tdcList)
		{
			if (tdcList.Count < 2) return 0;

			double avgSamplesPerRev = 0;
			int count = 0;

			for (int i = 1; i < tdcList.Count; i++)
			{
				int delta = tdcList[i] - tdcList[i - 1];
				if (delta > 0)
				{
					avgSamplesPerRev += delta;
					count++;
				}
			}

			if (count == 0) return 0;

			avgSamplesPerRev /= count;

			double revPerSec = SAMPLE_PER_SECOND / avgSamplesPerRev;
			double rpm = revPerSec * 60.0;

			return rpm;
		}
		
		// --------------------------------------------------------------------------------
		// Loop system for constant feed
		// --------------------------------------------------------------------------------
		private void AcquisitionLoop()
		{
			try
			{
				while (RunDataCollection)
				{
					AcquireAndProcessOnce();
					Thread.Sleep(10);
				}
			}
			catch (Exception ex)
			{
				ShowUiMessageOnce("DAQ INIT FAILED:\n\nSwitching to simulation mode.");
			}
			finally
			{
				isAcquiring = false;
				UpdateStartButtonRunningState(false);

				if (buttonStop.InvokeRequired)
				{
					buttonStop.BeginInvoke(new Action(() =>
					{
						buttonStop.Visible = false;
					}));
				}
				else
				{
					buttonStop.Visible = false;
				}
			}
		}
		
		// --------------------------------------------------------------------------------
		// The Main Entry Function
		// --------------------------------------------------------------------------------
		public FormPV()
		{
			InitializeComponent();
			
			ComboBox cmbUnits = new ComboBox();
			cmbUnits.DropDownStyle = ComboBoxStyle.DropDownList;
			cmbUnits.Items.AddRange(new string[] { "Bar", "kPa", "PSI" });
			cmbUnits.SelectedIndex = 0;
			
			chart1.Controls.Add(cmbUnits);
			cmbUnits.Location = new Point(25, 40); 
			cmbUnits.BringToFront();

			cmbUnits.SelectedIndexChanged += (s, e) =>
			{
				switch (cmbUnits.SelectedItem.ToString())
				{
					case "Bar":
						currentUnit = PressureUnit.Bar;
						break;
					case "kPa":
						currentUnit = PressureUnit.KPa;
						break;
					case "PSI":
						currentUnit = PressureUnit.PSI;
						break;
				}

				UpdateYAxisLabel();
				
				if (listX.Count > 0)
				{
					PlotProcessedData();
				}
			};

			buttonStop.Visible = false;
			
			lblRPM = new Label();
			lblRPM.AutoSize = true;
			lblRPM.Text = "RPM: 0";
			lblRPM.Font = new Font("Segoe UI", 18, FontStyle.Bold);
			lblRPM.ForeColor = Color.Black;
			lblRPM.BackColor = Color.Transparent;
			lblRPM.Location = new Point(20, 5);

			chart1.Controls.Add(lblRPM);
			lblRPM.BringToFront();
			
			lblProbe = new Label();
			lblProbe.AutoSize = true;
			lblProbe.Text = "";
			lblProbe.ForeColor = Color.DarkGreen;
			lblProbe.BackColor = Color.White;
			lblProbe.Visible = false;

			chart1.Controls.Add(lblProbe);
			lblProbe.BringToFront();
 
			ConfigureChart();

			chart1.MouseWheel += chart_MouseWheel;
			chart1.MouseDoubleClick += chart_MouseDoubleClick;
			chart1.MouseClick += Chart_MouseClick;
			chart1.MouseEnter += (s, e) => chart1.Focus();
		}
		
		private void UpdateYAxisLabel()
		{
			string unitText;

			switch (currentUnit)
			{
				case PressureUnit.Bar: unitText = "bar"; 
					break;
				case PressureUnit.KPa: unitText = "kPa"; 
					break;
				case PressureUnit.PSI: unitText = "psi"; 
					break;
				default: unitText = "bar"; break;
			}

			chart1.ChartAreas[0].AxisY.Title = $"Pressure ({unitText})";
		}
		
		private double ConvertPressureFromBar(double pressureBar)
		{
			switch (currentUnit)
			{
				case PressureUnit.Bar: return pressureBar;
				case PressureUnit.KPa: return pressureBar * 100.0;
				case PressureUnit.PSI: return pressureBar * 14.5038;
				default: return pressureBar;
			}
		}
		
		private void chart_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			chart1.ChartAreas[0].AxisX.ScaleView.ZoomReset();
			chart1.ChartAreas[0].AxisY.ScaleView.ZoomReset();
		}

		private void ConfigureChart()
		{
			chart1.Series.Clear();

			if (!chart1.Series.IsUniqueName(series))
			{
				series = "PV_" + DateTime.Now.ToString("HHmmss");
			}

			chart1.Series.Add(series);
			chart1.Series[0].ChartType = SeriesChartType.Line;
			chart1.Series[0].BorderWidth = 2;
			
			chart1.ChartAreas[0].AxisX.LabelStyle.Format = "F2";
			chart1.ChartAreas[0].AxisY.LabelStyle.Format = "F2";

			// Stroke-colour legend
			chart1.Legends.Clear();
			var legend = new Legend("StrokeLegend");
			legend.Docking = Docking.Bottom;
			legend.Alignment = StringAlignment.Center;
			chart1.Legends.Add(legend);

			chart1.ChartAreas[0].AxisX.Title = "Volume (cm³)";
			chart1.ChartAreas[0].AxisY.Title = "Pressure (bar)";
			chart1.ChartAreas[0].AxisX.MajorGrid.LineColor = Color.LightGray;
			chart1.ChartAreas[0].AxisY.MajorGrid.LineColor = Color.LightGray;
			
			chart1.ChartAreas[0].CursorX.IsUserEnabled = true;
			chart1.ChartAreas[0].CursorX.IsUserSelectionEnabled = true;
			chart1.ChartAreas[0].AxisX.ScaleView.Zoomable = true;

			chart1.ChartAreas[0].CursorY.IsUserEnabled = true;
			chart1.ChartAreas[0].CursorY.IsUserSelectionEnabled = true;
			chart1.ChartAreas[0].AxisY.ScaleView.Zoomable = true;
			
			chart1.ChartAreas[0].CursorX.Interval = 0;
			chart1.ChartAreas[0].CursorY.Interval = 0;
			
			var area = chart1.ChartAreas[0];
			area.AxisX.ScrollBar.Enabled = true;
			area.AxisY.ScrollBar.Enabled = true;
			area.AxisX.ScaleView.Scroll(0.0);
			area.AxisY.ScaleView.Scroll(0.0);
			area.AxisX.ScaleView.Zoomable = true;
			area.AxisY.ScaleView.Zoomable = true;
			area.CursorX.IsUserEnabled = true;
			area.CursorY.IsUserEnabled = true;
			
			UpdateYAxisLabel();
		}
		
		private void Chart_MouseClick(object sender, MouseEventArgs e)
		{
			try
			{
				var result = chart1.HitTest(e.X, e.Y);

				if (result.ChartArea == null)
				{
					lblProbe.Visible = false;
					return;
				}

				var area = result.ChartArea;

				double x = area.AxisX.PixelPositionToValue(e.X);
				double y = area.AxisY.PixelPositionToValue(e.Y);

				if (double.IsNaN(x) || double.IsNaN(y) ||
					double.IsInfinity(x) || double.IsInfinity(y))
				{
					lblProbe.Visible = false;
					return;
				}

				lblProbe.Text = $"Vol: {x:F2} cm³   Pres: {y:F1} {currentUnit}";
				lblProbe.Location = new Point(e.X + 5, e.Y - 15);
				lblProbe.Visible = true;
				lblProbe.BringToFront();
			}
			catch
			{
				lblProbe.Visible = false;
			}
		}

		private void chart_MouseWheel(object sender, MouseEventArgs e)
		{
			try
			{
				var chartArea = chart1.ChartAreas[0];

				if (e.Delta < 0)
				{
					chartArea.AxisX.ScaleView.ZoomReset();
					chartArea.AxisY.ScaleView.ZoomReset();
				}
				else if (e.Delta > 0)
				{
					double xMin = chartArea.AxisX.ScaleView.ViewMinimum;
					double xMax = chartArea.AxisX.ScaleView.ViewMaximum;
					double yMin = chartArea.AxisY.ScaleView.ViewMinimum;
					double yMax = chartArea.AxisY.ScaleView.ViewMaximum;

					double posXStart = chartArea.AxisX.PixelPositionToValue(e.Location.X) - (xMax - xMin) / 4;
					double posXFinish = chartArea.AxisX.PixelPositionToValue(e.Location.X) + (xMax - xMin) / 4;

					double posYStart = chartArea.AxisY.PixelPositionToValue(e.Location.Y) - (yMax - yMin) / 4;
					double posYFinish = chartArea.AxisY.PixelPositionToValue(e.Location.Y) + (yMax - yMin) / 4;

					chartArea.AxisX.ScaleView.Zoom(posXStart, posXFinish);
					chartArea.AxisY.ScaleView.Zoom(posYStart, posYFinish);
				}
			}
			catch { }
		}
		
		private void buttonStop_Click(object sender, EventArgs e)
		{
			RunDataCollection = false;
		}

		private void button2_Click(object sender, EventArgs e)
		{
			if (isAcquiring)
			{
				MessageBox.Show("Acquisition already running.");
				return;
			}

			try
			{
				ResetRunState();
				UpdateStartButtonRunningState(true);
				RunDataCollection = true;
				isAcquiring = true;

				buttonStop.Visible = true;
				
				ThreadTask.Run(() => AcquisitionLoop());
			}
			catch (Exception ex)
			{
				isAcquiring = false;
				RunDataCollection = false;
				UpdateStartButtonRunningState(false);
				MessageBox.Show("Failed to start acquisition: " + ex.Message);
			}
		}

		private void ResetRunState()
		{
			lock (_shownMessages) { _shownMessages.Clear(); }

			listX.Clear();
			listY.Clear();
			listAngle.Clear();
			listRawAngle.Clear();
			listPressureVolts.Clear();

			TDCList.Clear();
			IndexList.Clear();
			Tracker.Clear();
			allIndex.Clear();
			allIndexAngle.Clear();

			aData = null;
			dData = null;

			if (chart1.InvokeRequired)
			{
				chart1.BeginInvoke(new Action(ConfigureChart));
			}
			else
			{
				ConfigureChart();
			}
		}

		private void UpdateStartButtonRunningState(bool running)
		{
			if (button2.InvokeRequired)
			{
				button2.BeginInvoke(new Action(() => UpdateStartButtonRunningState(running)));
				return;
			}

			if (running)
			{
				button2.BackColor = Color.White;
				button2.ForeColor = Color.Black;
				button2.Enabled = false;
			}
			else
			{
				button2.BackColor = Color.FromArgb(0, 60, 113);
				button2.ForeColor = Color.White;
				button2.Enabled = true;
			}
		}
		
		// --------------------------------------------------------------------------------
		// Load CSV with phase correction, BDC pegging, and pressure lag compensation on load
		// --------------------------------------------------------------------------------
		private void LoadAndPlotCsv(string filePath)
		{
			if (chart1.InvokeRequired)
			{
				chart1.BeginInvoke(new Action(() => LoadAndPlotCsv(filePath)));
				return;
			}

			var lines = File.ReadAllLines(filePath);

			if (lines.Length <= 1)
				throw new Exception("CSV empty");

			// Load raw data from CSV
			List<double> rawAngles = new List<double>();
			List<double> rawVolts = new List<double>();

			double loadedRpm = 0;

			for (int i = 1; i < lines.Length; i++)
			{
				var line = lines[i].Trim();

				// Extract RPM from footer comment
				if (line.StartsWith("# RPM="))
				{
					double.TryParse(line.Substring(6),
						System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture,
						out loadedRpm);
					continue;
				}

				if (line.StartsWith("#")) continue;

				var parts = line.Split(',');
				if (parts.Length < 4) continue;

				double angleDeg = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
				double pressureVolts = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);

				rawAngles.Add(angleDeg);
				rawVolts.Add(pressureVolts);
			}

			if (rawAngles.Count == 0)
				throw new Exception("No valid data rows");

			// --- Step 1: Apply phase correction (resolves TDC ambiguity) ---
			double phaseOffset = CalculatePhaseOffset(rawAngles, rawVolts);

			// --- Step 2: BDC Pegging — find voltage at intake BDC for atmospheric reference ---
			double voltageOffset = CalculateBdcPeggingOffset(rawAngles, rawVolts, phaseOffset);

			// --- Step 3: Assemble corrected data ---
			for (int i = 0; i < rawAngles.Count; i++)
			{
				double correctedAngle = (rawAngles[i] + phaseOffset) % 720.0;
				if (correctedAngle < 0) correctedAngle += 720.0;

				double pressureAngle = correctedAngle - PRESSURE_LAG_DEGREES;
				pressureAngle = pressureAngle % 720.0;
				if (pressureAngle < 0) pressureAngle += 720.0;

				double pressureVolts = rawVolts[i];
				double pressureBar = (pressureVolts - voltageOffset) * CHARGE_AMP_BAR_PER_VOLT;
				double volumeM3 = CalculateCylinderVolumeM3(pressureAngle);

				listRawAngle.Add(rawAngles[i]);
				listAngle.Add(pressureAngle);
				listX.Add(volumeM3);
				listY.Add(pressureBar);
				listPressureVolts.Add(pressureVolts);
			}

			SortListsByAngle();

			// Restore RPM display from saved value
			if (loadedRpm > 0)
			{
				_currentRpm = loadedRpm;
				lblRPM.Text = $"RPM: {loadedRpm:F0}";
			}

			PlotProcessedData();
		}

		// --------------------------------------------------------------------------------
		// Phase correction: force peak pressure to TARGET_PEAK_ANGLE.
		//
		// This single step corrects BOTH the 360° compression/exhaust TDC ambiguity AND 
		// the fine TDC detection jitter (~80° variation between runs) that comes from 
		// noise in the encoder signal.
		//
		// TARGET_PEAK_ANGLE is set so that after PRESSURE_LAG_DEGREES is subtracted at
		// plot time, the peak lands at ~360° (TDC compression). If you change 
		// PRESSURE_LAG_DEGREES, also update TARGET_PEAK_ANGLE to match:
		//	 TARGET_PEAK_ANGLE ≈ 360 + PRESSURE_LAG_DEGREES
		// --------------------------------------------------------------------------------
		private double CalculatePhaseOffset(List<double> angles, List<double> volts)
		{
			int peakIdx = 0;
			double peakVal = double.MinValue;
			for (int i = 0; i < volts.Count; i++)
			{
				if (volts[i] > peakVal) { peakVal = volts[i]; peakIdx = i; }
			}

			double peakRawAngle = angles[peakIdx];
			const double TARGET_PEAK_ANGLE = 405.0;  // = 360° + PRESSURE_LAG_DEGREES
			return TARGET_PEAK_ANGLE - peakRawAngle;
		}
		
		// --------------------------------------------------------------------------------
		// BDC Pegging: calculate voltage offset to peg intake BDC to atmospheric pressure
		// --------------------------------------------------------------------------------
		//
		// Finds the voltage reading at intake Bottom Dead Centre (angle 180° after phase
		// correction) and calculates the offset required to make that voltage correspond
		// to atmospheric pressure. This re-anchors the AC-coupled pressure signal to the
		// absolute scale.
		//
		// The offset is returned in volts and should be subtracted from each pressure
		// voltage sample before scaling to bar:
		//
		//	 pressureBar = (pressureVolts - voltageOffset) * CHARGE_AMP_BAR_PER_VOLT
		//
		private double CalculateBdcPeggingOffset(List<double> rawAngles, List<double> rawVolts, double phaseOffset)
		{
			// Find the sample closest to intake BDC (180° after phase correction)
			int bdcIdx = 0;
			double closestDelta = double.MaxValue;

			for (int i = 0; i < rawAngles.Count; i++)
			{
				double correctedAngle = (rawAngles[i] + phaseOffset) % 720.0;
				if (correctedAngle < 0) correctedAngle += 720.0;

				double delta = Math.Abs(correctedAngle - 180.0);
				if (delta < closestDelta)
				{
					closestDelta = delta;
					bdcIdx = i;
				}
			}

			// Average a small window of samples around BDC to reduce noise sensitivity
			const int windowSize = 10;
			int windowStart = Math.Max(0, bdcIdx - windowSize / 2);
			int windowEnd = Math.Min(rawVolts.Count - 1, bdcIdx + windowSize / 2);

			double bdcVoltsSum = 0;
			int bdcSamples = 0;
			for (int i = windowStart; i <= windowEnd; i++)
			{
				bdcVoltsSum += rawVolts[i];
				bdcSamples++;
			}
			double bdcVolts = bdcSamples > 0 ? bdcVoltsSum / bdcSamples : rawVolts[bdcIdx];

			// Calculate offset: we want (bdcVolts - offset) * scale = atmospheric pressure
			// Therefore: offset = bdcVolts - atmospheric / scale
			double voltageOffset = bdcVolts - (ATMOSPHERIC_PRESSURE_BAR / CHARGE_AMP_BAR_PER_VOLT);

			Debug.WriteLine($"[BDC-PEG] BDC sample idx={bdcIdx}, windowed volts={bdcVolts:F4}V, offset={voltageOffset:F4}V");

			return voltageOffset;
		}

		// =====================================================================================
		// ORIGINAL DATA ACQUISITION PIPELINE — UNTOUCHED
		// =====================================================================================
		
		private void AcquireAndProcessOnce()
		{
			try
			{
				ConfigureTasks();

				DigitalReader = new DigitalSingleChannelReader(DAQ_Task_TDC.Stream);
				AnalogReader = new AnalogSingleChannelReader(DAQ_Task_Pres.Stream);
				
				DAQ_Task_TDC.Start();

				bool tdcFound = WaitForTdcEdge();

				if (!tdcFound)
				{
					ShowUiMessageOnce("TDC not detected - skipping cycle.");
					return;
				}

				DAQ_Task_Pres.Start();

				dData = DigitalReader.ReadMultiSamplePortInt16(SAMPLES);
				aData = AnalogReader.ReadMultiSample(SAMPLES);
				
				ProcessSynchronizedBlock();
				
				if ((DateTime.Now - lastUiUpdate).TotalMilliseconds > 250)
				{
					lastUiUpdate = DateTime.Now;
					PlotProcessedData();
				}
			}
			catch (DaqException ex)
			{
				RunDataCollection = false;
				ShowUiMessageOnce("DAQ error: " + ex.Message);
			}
			catch (Exception ex)
			{
				RunDataCollection = false;
				ShowUiMessageOnce("Acquisition error: " + ex.Message);
			}
			finally
			{
				CleanupTasks();
			}
		}

		private void ConfigureTasks()
		{
			CleanupTasks();

			DAQ_Task_Pres = new DaqTask();
			DAQ_Task_TDC = new DaqTask();

			DAQ_Task_Pres.AIChannels.CreateVoltageChannel(
				"Pressure/ai0", "", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
			
			DAQ_Task_Pres.Timing.ConfigureSampleClock(
				"", SAMPLE_PER_SECOND, SampleClockActiveEdge.Rising, SampleQuantityMode.FiniteSamples, SAMPLES);

			DAQ_Task_TDC.DIChannels.CreateChannel(
				"Volume/port0/line0:7", "", ChannelLineGrouping.OneChannelForAllLines);

			DAQ_Task_TDC.Timing.ConfigureSampleClock(
				"", SAMPLE_PER_SECOND, SampleClockActiveEdge.Rising, SampleQuantityMode.FiniteSamples, SAMPLES);

			DAQ_Task_Pres.Stream.ConfigureInputBuffer(SAMPLES);
			DAQ_Task_TDC.Stream.ConfigureInputBuffer(SAMPLES);

			DAQ_Task_Pres.Control(TaskAction.Verify);
			DAQ_Task_TDC.Control(TaskAction.Verify);
		}

		// --------------------------------------------------------------------------------
		// ORIGINAL ProcessSynchronizedBlock — uses original TDC detection
		// --------------------------------------------------------------------------------
		private void ProcessSynchronizedBlock()
		{
			if (aData == null || dData == null)
			{
				ShowUiMessageOnce("No data acquired.");
				return;
			}

			if (aData.Length == 0 || dData.Length == 0)
			{
				ShowUiMessageOnce("Empty data block acquired.");
				return;
			}

			double[] angleBySample = BuildAngleArrayFromDigitalSignal(dData);

			if (angleBySample == null)
			{
				ShowUiMessageOnce("Unable to determine TDC / crank angle from digital signal.");
				return;
			}

			BuildPvData(angleBySample, aData);

			if (listX.Count == 0)
			{
				ShowUiMessageOnce("No valid P-V points were produced.");
			}
			
			double rpm = CalculateRPM(TDCList);
			_currentRpm = rpm;
			lblRPM.Text = $"RPM: {rpm:F0}";
		}

		// --------------------------------------------------------------------------------
		// ORIGINAL BuildAngleArrayFromDigitalSignal
		// --------------------------------------------------------------------------------
		private double[] BuildAngleArrayFromDigitalSignal(Int16[] digitalSamples)
		{
			List<PulseWindow> pulses = ExtractPulseWindows(digitalSamples);

			if (pulses.Count < 2)
			{
				return null;
			}

			List<int> tdcCenters = DetectTdcPulseCenters(pulses);

			if (tdcCenters.Count < 2)
			{
				return null;
			}
			
			if (tdcCenters.Count >= 3)
			{
				tdcCenters.RemoveAt(0);
			}

			TDCList.Clear();
			TDCList.AddRange(tdcCenters);

			double[] angleBySample = Enumerable.Repeat(double.NaN, digitalSamples.Length).ToArray();

			for (int t = 1; t < tdcCenters.Count; t++)
			{
				int previousTdc = tdcCenters[t - 1];
				int currentTdc = tdcCenters[t];

				int span = currentTdc - previousTdc;
				if (span <= 0)
				{
					continue;
				}

				double localAngleIncrement = 360.0 / span;

				for (int i = previousTdc; i < currentTdc && i < angleBySample.Length; i++)
				{
					angleBySample[i] = (i - previousTdc) * localAngleIncrement;
				}
			}

			return angleBySample;
		}

		// --------------------------------------------------------------------------------
		// ORIGINAL ExtractPulseWindows
		// --------------------------------------------------------------------------------
		private List<PulseWindow> ExtractPulseWindows(Int16[] digitalSamples)
		{
			List<PulseWindow> pulses = new List<PulseWindow>();

			bool inPulse = false;
			int pulseStart = -1;

			for (int i = 0; i < digitalSamples.Length; i++)
			{
				bool high = (digitalSamples[i] & 0x0001) != 0;

				if (high && !inPulse)
				{
					inPulse = true;
					pulseStart = i;
				}
				else if (!high && inPulse)
				{
					int pulseEnd = i - 1;
					int width = pulseEnd - pulseStart + 1;
					int center = pulseStart + (width / 2);

					pulses.Add(new PulseWindow
					{
						StartIndex = pulseStart,
						EndIndex = pulseEnd,
						Width = width,
						CenterIndex = center
					});

					inPulse = false;
					pulseStart = -1;
				}
			}

			if (inPulse && pulseStart >= 0)
			{
				int pulseEnd = digitalSamples.Length - 1;
				int width = pulseEnd - pulseStart + 1;
				int center = pulseStart + (width / 2);

				pulses.Add(new PulseWindow
				{
					StartIndex = pulseStart,
					EndIndex = pulseEnd,
					Width = width,
					CenterIndex = center
				});
			}

			return pulses;
		}

		// --------------------------------------------------------------------------------
		// ORIGINAL DetectTdcPulseCenters (local max width comparison)
		// --------------------------------------------------------------------------------
		private List<int> DetectTdcPulseCenters(List<PulseWindow> pulses)
		{
			List<int> tdcCenters = new List<int>();

			if (pulses.Count < 4)
			{
				return tdcCenters;
			}

			for (int i = 2; i < pulses.Count - 1; i++)
			{
				int current = pulses[i].Width;
				int prev1 = pulses[i - 1].Width;
				int prev2 = pulses[i - 2].Width;
				int next1 = pulses[i + 1].Width;

				if (current > prev1 && current > prev2 && current > next1)
				{
					tdcCenters.Add(pulses[i].CenterIndex);
				}
			}

			return tdcCenters;
		}
		
		// --------------------------------------------------------------------------------
		// ORIGINAL WaitForTdcEdge
		// --------------------------------------------------------------------------------
		private bool WaitForTdcEdge(int timeoutMs = 2000)
		{
			var sw = Stopwatch.StartNew();

			while (sw.ElapsedMilliseconds < timeoutMs)
			{
				try
				{
					var digital = DigitalReader.ReadMultiSamplePortInt16(1000);

					var pulses = ExtractPulseWindows(digital);

					if (pulses.Count < 3)
						continue;

					for (int i = 2; i < pulses.Count - 1; i++)
					{
						int current = pulses[i].Width;
						int prev1 = pulses[i - 1].Width;
						int prev2 = pulses[i - 2].Width;
						int next1 = pulses[i + 1].Width;

						if (current > prev1 && current > prev2 && current > next1)
						{
							return true;
						}
					}
				}
				catch { }
			}

			return false;
		}
	  
		// =====================================================================================
		// BUILD PV DATA — original capture + post-capture phase correction + BDC pegging
		// =====================================================================================
		//
		// Step 1: Original logic builds raw 720° angle mapping from TDC[0] to TDC[2]
		// Step 2: Phase correction resolves the compression-vs-exhaust TDC ambiguity
		// Step 3: BDC pegging — calculates voltage offset so intake BDC = atmospheric pressure
		// Step 4: Pressure lag compensation — assigns each pressure sample to an earlier angle
		//
		// The raw uncorrected angles are preserved in listRawAngle for the CSV save.
		//
		private void BuildPvData(double[] angleBySample, double[] analogSamples)
		{
			listX.Clear();
			listY.Clear();
			listAngle.Clear();
			listRawAngle.Clear();
			listPressureVolts.Clear();

			var cycle = FindFullCycleIndices(TDCList);

			if (cycle != null)
			{
				int start = cycle.Value.start;
				int end = cycle.Value.end;
				int span = end - start;

				if (span <= 0)
					return;

				// --- Step 1: Build raw angles and collect pressure data ---
				List<double> rawAngles = new List<double>();
				List<double> rawVolts = new List<double>();

				for (int i = start; i <= end && i < analogSamples.Length; i++)
				{
					double norm = (double)(i - start) / span;
					double angle720 = norm * 720.0;

					rawAngles.Add(angle720);
					rawVolts.Add(analogSamples[i]);
				}

				// --- Step 2: Apply phase correction (resolves TDC ambiguity) ---
				double phaseOffset = CalculatePhaseOffset(rawAngles, rawVolts);

				// --- Step 3: BDC Pegging — find voltage at intake BDC for atmospheric reference ---
				double voltageOffset = CalculateBdcPeggingOffset(rawAngles, rawVolts, phaseOffset);

				// --- Step 4: Assemble corrected data ---
				for (int i = 0; i < rawAngles.Count; i++)
				{
					double correctedAngle = (rawAngles[i] + phaseOffset) % 720.0;
					if (correctedAngle < 0) correctedAngle += 720.0;

					// Apply pressure lag compensation — pressure arrived "late", so it
					// actually corresponds to an earlier crank angle than recorded
					double pressureAngle = correctedAngle - PRESSURE_LAG_DEGREES;
					pressureAngle = pressureAngle % 720.0;
					if (pressureAngle < 0) pressureAngle += 720.0;

					double pressureVolts = rawVolts[i];
					// Apply BDC pegging offset so intake BDC = atmospheric pressure
					double pressureBar = (pressureVolts - voltageOffset) * CHARGE_AMP_BAR_PER_VOLT;
					double volumeM3 = CalculateCylinderVolumeM3(pressureAngle);

					listRawAngle.Add(rawAngles[i]);
					listPressureVolts.Add(pressureVolts);
					listAngle.Add(pressureAngle);
					listX.Add(volumeM3);
					listY.Add(pressureBar);
				}

				SortListsByAngle();
				return;
			}

			// Fallback: no full cycle
			int validCount = 0;

			for (int i = 0; i < analogSamples.Length && i < angleBySample.Length; i++)
			{
				double angleDeg = angleBySample[i];

				if (double.IsNaN(angleDeg))
					continue;

				double pressureAngle = angleDeg - PRESSURE_LAG_DEGREES;
				pressureAngle = pressureAngle % 720.0;
				if (pressureAngle < 0) pressureAngle += 720.0;

				double pressureVolts = analogSamples[i];
				// Fallback path uses original zero-volts baseline (no pegging possible without cycle)
				double pressureBar = (pressureVolts - CHARGE_AMP_ZERO_BAR_VOLTS) * CHARGE_AMP_BAR_PER_VOLT;
				double volumeM3 = CalculateCylinderVolumeM3(pressureAngle);

				listRawAngle.Add(angleDeg);
				listPressureVolts.Add(pressureVolts);
				listAngle.Add(pressureAngle);
				listX.Add(volumeM3);
				listY.Add(pressureBar);

				validCount++;
			}

			if (validCount == 0)
			{
				Debug.WriteLine("No valid samples to plot.");
			}
		}

		// --------------------------------------------------------------------------------
		// Sort all parallel lists by crank angle for correct plot ordering
		// --------------------------------------------------------------------------------
		private void SortListsByAngle()
		{
			if (listAngle.Count == 0) return;

			int[] indices = Enumerable.Range(0, listAngle.Count).ToArray();
			Array.Sort(indices, (a, b) => listAngle[a].CompareTo(listAngle[b]));

			List<double> sortedX = new List<double>(indices.Length);
			List<double> sortedY = new List<double>(indices.Length);
			List<double> sortedAngle = new List<double>(indices.Length);
			List<double> sortedRawAngle = new List<double>(indices.Length);
			List<double> sortedVolts = new List<double>(indices.Length);

			for (int i = 0; i < indices.Length; i++)
			{
				int idx = indices[i];
				sortedX.Add(listX[idx]);
				sortedY.Add(listY[idx]);
				sortedAngle.Add(listAngle[idx]);
				sortedRawAngle.Add(listRawAngle[idx]);
				sortedVolts.Add(listPressureVolts[idx]);
			}

			listX.Clear(); listX.AddRange(sortedX);
			listY.Clear(); listY.AddRange(sortedY);
			listAngle.Clear(); listAngle.AddRange(sortedAngle);
			listRawAngle.Clear(); listRawAngle.AddRange(sortedRawAngle);
			listPressureVolts.Clear(); listPressureVolts.AddRange(sortedVolts);
		}

		private double CalculateCylinderVolumeM3(double angleDeg) {
			
			double theta = angleDeg * (Math.PI / 180.0);
			
			double R = ROD_TO_CRANK_RATIO;
			
			double cosTheta = Math.Cos(theta);
			double sinTheta = Math.Sin(theta);
			
			double term = 1 - cosTheta + (R - Math.Sqrt(R * R - sinTheta * sinTheta));
			
			double normalized = 0.5 * term;
			
			return CLEARANCE_VOLUME_M3 + DISPLACEMENT_VOLUME_M3 * normalized;
		}
		
		// --------------------------------------------------------------------------------
		// Plot with stroke colouring
		// --------------------------------------------------------------------------------
		private void PlotProcessedData()
		{
			if (chart1.InvokeRequired)
			{
				chart1.BeginInvoke(new Action(PlotProcessedData));
				return;
			}
		
			chart1.Series.Clear();
		
			var intake = new Series("Intake") { ChartType = SeriesChartType.Line, Color = Color.LightBlue, BorderWidth = 1 };
			var compression = new Series("Compression") { ChartType = SeriesChartType.Line, Color = Color.Orange, BorderWidth = 1 };
			var power = new Series("Power") { ChartType = SeriesChartType.Line, Color = Color.Red, BorderWidth = 1 };
			var exhaust = new Series("Exhaust") { ChartType = SeriesChartType.Line, Color = Color.Green, BorderWidth = 1 };
		
			chart1.Series.Add(intake);
			chart1.Series.Add(compression);
			chart1.Series.Add(power);
			chart1.Series.Add(exhaust);

			int count = Math.Min(listAngle.Count, Math.Min(listX.Count, listY.Count));

			// Assign series to the chart legend
			intake.Legend	  = "StrokeLegend";
			compression.Legend = "StrokeLegend";
			power.Legend	   = "StrokeLegend";
			exhaust.Legend	 = "StrokeLegend";

			// Bridge-connected plot: when the stroke changes, the first point of the new
			// stroke is also added to the previous series so adjacent series share an
			// endpoint and no gap appears between them.
			Stroke? prevStroke = null;

			for (int i = 0; i < count; i++)
			{
				double volCm3 = listX[i] * 1_000_000.0;
				double pres   = ConvertPressureFromBar(listY[i]);
				double angle  = listAngle[i];
				Stroke curStroke = GetStroke(angle);

				if (prevStroke.HasValue && curStroke != prevStroke.Value)
				{
					// Add current point to the outgoing series to close its end
					switch (prevStroke.Value)
					{
						case Stroke.Intake:	  
							intake.Points.AddXY(volCm3, pres);	  
							break;
						case Stroke.Compression: 
							compression.Points.AddXY(volCm3, pres); 
							break;
						case Stroke.Power:	   
							power.Points.AddXY(volCm3, pres);	   
							break;
						case Stroke.Exhaust:	 
							exhaust.Points.AddXY(volCm3, pres);	 
							break;
					}
				}

				switch (curStroke)
				{
					case Stroke.Intake:	  
						intake.Points.AddXY(volCm3, pres);	  
						break;
					case Stroke.Compression: 
						compression.Points.AddXY(volCm3, pres); 
						break;
					case Stroke.Power:	   
						power.Points.AddXY(volCm3, pres);	   
						break;
					case Stroke.Exhaust:	 
						exhaust.Points.AddXY(volCm3, pres);	 
						break;
				}

				prevStroke = curStroke;
			}

			// Close the cycle: connect exhaust back to the start of intake
			if (count > 0)
			{
				double volCm3 = listX[0] * 1_000_000.0;
				double pres   = ConvertPressureFromBar(listY[0]);
				exhaust.Points.AddXY(volCm3, pres);
			}

			// TDC marker at compression TDC (360°)
			Series tdcSeries = new Series("TDC");
			tdcSeries.ChartType = SeriesChartType.Point;
			tdcSeries.Color = Color.Red;
			tdcSeries.MarkerSize = 8;
			tdcSeries.MarkerStyle = MarkerStyle.Circle;
			tdcSeries.IsVisibleInLegend = false;
			chart1.Series.Add(tdcSeries);
			
			// Find the single sample closest to compression TDC (360°)
			int tdcIdx = -1;
			double closestDelta = double.MaxValue;
			for (int i = 0; i < count; i++)
			{
				double delta = Math.Abs(listAngle[i] - 360.0);
				if (delta < closestDelta)
				{
					closestDelta = delta;
					tdcIdx = i;
				}
			}

			if (tdcIdx >= 0 && closestDelta < 2.0)
			{
				double volumeCm3 = listX[tdcIdx] * 1_000_000.0;
				double displayPressure = ConvertPressureFromBar(listY[tdcIdx]);
				int idx = tdcSeries.Points.AddXY(volumeCm3, displayPressure);
				tdcSeries.Points[idx].Label = "TDC";
				tdcSeries.Points[idx].LabelForeColor = Color.Red;
				tdcSeries.Points[idx].Font = new Font("Segoe UI", 9, FontStyle.Bold);
				tdcSeries.Points[idx]["LabelStyle"] = "Left";
			}
			
			// Auto-scale axes
			var area = chart1.ChartAreas[0];

			double xMin = double.MaxValue, xMax = double.MinValue;
			double yMin = double.MaxValue, yMax = double.MinValue;

			foreach (var s in chart1.Series)
			{
				foreach (var pt in s.Points)
				{
					if (pt.XValue < xMin) xMin = pt.XValue;
					if (pt.XValue > xMax) xMax = pt.XValue;
					if (pt.YValues[0] < yMin) yMin = pt.YValues[0];
					if (pt.YValues[0] > yMax) yMax = pt.YValues[0];
				}
			}

			if (xMin < xMax && yMin < yMax)
			{
				double xPadding = (xMax - xMin) * 0.1;
				double yPadding = (yMax - yMin) * 0.15;

				area.AxisX.Minimum = xMin - xPadding;
				area.AxisX.Maximum = xMax + xPadding;
				area.AxisY.Minimum = yMin - yPadding;
				area.AxisY.Maximum = yMax + yPadding;
			}

			PlotSideCharts();
		}

		// --------------------------------------------------------------------------------
		// Plot pressure and volume against crank angle on the two side charts
		// --------------------------------------------------------------------------------
		private void PlotSideCharts()
		{
			if (chartPressureTime.InvokeRequired)
			{
				chartPressureTime.BeginInvoke(new Action(PlotSideCharts));
				return;
			}

			int count = Math.Min(listAngle.Count, Math.Min(listX.Count, listY.Count));
			if (count == 0) return;

			// --- Pressure vs Crank Angle ---
			chartPressureTime.Series.Clear();
			var presSeries = new Series("Pressure")
			{
				ChartType = SeriesChartType.Line,
				Color = Color.SteelBlue,
				BorderWidth = 1
			};
			chartPressureTime.Series.Add(presSeries);

			double pMin = double.MaxValue, pMax = double.MinValue;
			for (int i = 0; i < count; i++)
			{
				double pres = ConvertPressureFromBar(listY[i]);
				presSeries.Points.AddXY(listAngle[i], pres);
				if (pres < pMin) pMin = pres;
				if (pres > pMax) pMax = pres;
			}

			string unitText;
			switch (currentUnit)
			{
				case PressureUnit.KPa: 
					unitText = "kPa"; 
					break;
				case PressureUnit.PSI: 
					unitText = "psi"; 
					break;
				default: unitText = "bar"; break;
			}
			chartPressureTime.ChartAreas[0].AxisY.Title = $"Pressure ({unitText})";
			chartPressureTime.ChartAreas[0].AxisY.LabelStyle.Format = "F2";
			chartPressureTime.ChartAreas[0].AxisX.Minimum = 0;
			chartPressureTime.ChartAreas[0].AxisX.Maximum = 720;
			if (pMin < pMax)
			{
				double pad = (pMax - pMin) * 0.1;
				chartPressureTime.ChartAreas[0].AxisY.Minimum = pMin - pad;
				chartPressureTime.ChartAreas[0].AxisY.Maximum = pMax + pad;
			}

			// --- Volume vs Crank Angle ---
			chartVolumeTime.Series.Clear();
			var volSeries = new Series("Volume")
			{
				ChartType = SeriesChartType.Line,
				Color = Color.DarkGreen,
				BorderWidth = 1
			};
			chartVolumeTime.Series.Add(volSeries);

			double vMin = double.MaxValue, vMax = double.MinValue;
			for (int i = 0; i < count; i++)
			{
				double volCm3 = listX[i] * 1_000_000.0;
				volSeries.Points.AddXY(listAngle[i], volCm3);
				if (volCm3 < vMin) vMin = volCm3;
				if (volCm3 > vMax) vMax = volCm3;
			}

			chartVolumeTime.ChartAreas[0].AxisY.LabelStyle.Format = "F2";
			chartVolumeTime.ChartAreas[0].AxisX.Minimum = 0;
			chartVolumeTime.ChartAreas[0].AxisX.Maximum = 720;
			if (vMin < vMax)
			{
				double pad = (vMax - vMin) * 0.1;
				chartVolumeTime.ChartAreas[0].AxisY.Minimum = vMin - pad;
				chartVolumeTime.ChartAreas[0].AxisY.Maximum = vMax + pad;
			}
		}
	 
		private void CleanupTasks()
		{
			if (DAQ_Task_TDC != null)
			{
				try { DAQ_Task_TDC.Stop(); } catch { }
				DAQ_Task_TDC.Dispose();
				DAQ_Task_TDC = null;
			}

			if (DAQ_Task_Pres != null)
			{
				try { DAQ_Task_Pres.Stop(); } catch { }
				DAQ_Task_Pres.Dispose();
				DAQ_Task_Pres = null;
			}

			AnalogReader = null;
			DigitalReader = null;
		}

		private void ShowUiMessage(string message)
		{
			if (this.InvokeRequired)
			{
				this.BeginInvoke(new Action(() => ShowUiMessage(message)));
				return;
			}

			MessageBox.Show(message);
		}

		// Each unique message is shown at most once per run; cleared by ResetRunState()
		private void ShowUiMessageOnce(string message)
		{
			lock (_shownMessages)
			{
				if (_shownMessages.Contains(message)) return;
				_shownMessages.Add(message);
			}
			ShowUiMessage(message);
		}
		
		private void buttonLoad_Click(object sender, EventArgs e)
		{
			using (OpenFileDialog dlg = new OpenFileDialog())
			{
				dlg.Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*";
				dlg.Title = "Select CSV File";

				if (dlg.ShowDialog() == DialogResult.OK)
				{
					try
					{
						ResetRunState();
						LoadAndPlotCsv(dlg.FileName);
					}
					catch (Exception ex)
					{
						MessageBox.Show("Load failed: " + ex.Message);
					}
				}
			}
		}

		// --------------------------------------------------------------------------------
		// Save CSV — includes raw angle, corrected angle, and TDC sample indices
		// --------------------------------------------------------------------------------
		private void button1_Click_1(object sender, EventArgs e)
		{
			string filename = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + $"_RPM-{_currentRpm:F0}.csv";

			using (SaveFileDialog saveFileDialog = new SaveFileDialog())
			{
				saveFileDialog.Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*";
				saveFileDialog.Title = "Save Chart Data";
				saveFileDialog.FileName = filename;

				if (saveFileDialog.ShowDialog() == DialogResult.OK)
				{
					try
					{
						using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName))
						{
							// Header: includes both raw and corrected angles, plus TDC info
							writer.WriteLine("Volume_m3,Pressure_bar,CrankAngle_deg,PressureVolts,RawAngle_deg");

							int rowCount = new[]
							{
								listX.Count,
								listY.Count,
								listAngle.Count,
								listPressureVolts.Count,
								listRawAngle.Count
							}.Min();

							for (int i = 0; i < rowCount; i++)
							{
								writer.WriteLine($"{listX[i]},{listY[i]},{listAngle[i]},{listPressureVolts[i]},{listRawAngle[i]}");
							}

							// Append metadata footer
							writer.WriteLine();
							writer.WriteLine($"# RPM={_currentRpm:F2}");
							writer.WriteLine("# TDC_Sample_Indices");
							for (int i = 0; i < TDCList.Count; i++)
							{
								writer.WriteLine($"# TDC[{i}]={TDCList[i]}");
							}
						}

						MessageBox.Show("Data saved successfully.", "Save", MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
					catch (Exception ex)
					{
						MessageBox.Show("Error saving data: " + ex.Message);
					}
				}
			}
		}
		
		private (int start, int end)? FindFullCycleIndices(List<int> tdcList)
		{
			if (tdcList.Count < 3)
				return null;

			int start = tdcList[0];
			int end = tdcList[2];

			if (end > start)
				return (start, end);

			return null;
		}

		private class PulseWindow
		{
			public int StartIndex { get; set; }
			public int EndIndex { get; set; }
			public int Width { get; set; }
			public int CenterIndex { get; set; }
		}
	}
}

