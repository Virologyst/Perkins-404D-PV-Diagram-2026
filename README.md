# Perkins 404D-22 — PV Diagram Software

A Windows Forms application developed at **Queensland University of Technology** that acquires live cylinder pressure and crank angle data from a Perkins 404D-22 diesel engine and displays real-time pressure-volume (PV) diagrams.

---

## Requirements

- Windows 10/11 (x86 build target)
- [NI-DAQmx driver v18.0](https://www.ni.com/en/support/downloads/drivers/download.ni-daq-mx.html) or later
- NI chassis with:
  - **NI 9223** analog input module — named `Pressure` in NI MAX
  - **NI 9401** digital I/O module — named `Volume` in NI MAX
- .NET Framework 4.x

---

## Hardware Setup

1. Open **NI MAX** and confirm both modules appear with the correct names (`Pressure`, `Volume`).
2. Connect the cylinder pressure sensor to `Pressure/ai0`.
3. Connect the crank angle encoder to the **bottom port** of the NI 9401 module (`Volume/port0/line0:7`).
4. Ensure the engine is connected and the NI chassis is powered before launching the application.

---

## Getting Started

1. Build the solution in Visual Studio (x86 configuration).
2. Run `Engine Practical.exe` — the main PV Diagram window opens.
3. Click **Start** to begin live acquisition. The three charts will update in real time:
   - **P-V Diagram** — pressure vs. cylinder volume, colour-coded by stroke
   - **Pressure vs. Crank Angle**
   - **Volume vs. Crank Angle**
4. Click **Stop** to halt acquisition cleanly before closing.

---

## Saving Data

To save the currently displayed engine cycle as a CSV file:

1. Click **Save Data** in the bottom-left controls.
2. In the dialog that opens, navigate to the desired output directory.
3. The default filename is auto-generated in the format `YYYY-MM-DD HH-MM-SS RPM-XXXX.csv`, reflecting the current date, time, and engine RPM.
4. Click **Save** to write the file.

### CSV File Format

Each row represents one sample point:

| Column | Description |
|---|---|
| `Volume_m3` | Instantaneous cylinder volume in cubic metres |
| `Pressure_bar` | Calibrated cylinder pressure in bar (absolute) |
| `CrankAngle_deg` | Phase-corrected crank angle in degrees (0–720°) |
| `PressureVolts` | Raw voltage from the NI 9223 analog input |
| `RawAngle_deg` | Crank angle before phase correction (for reprocessing) |

TDC pulse sample indices are appended as `# TDC[n]=index` comment lines at the end of the file, which are ignored by standard CSV parsers.

---

## Loading Previously Saved Data

1. Click **Load** in the bottom-left controls.
2. Navigate to a previously saved `.csv` file and select it.
3. Click **Open**.

The application reads the file, applies the same phase correction and BDC pegging algorithms used during live acquisition, and displays the data across all three charts. The RPM value is read from the filename if present.

---

## Chart Interaction

All three charts support the following interactions:

| Action | Result |
|---|---|
| Scroll wheel forward | Zoom in, centred on cursor |
| Scroll wheel backward | Zoom out / reset view |
| Double-click chart area | Reset zoom to default |
| Left-click a point | Display volume and pressure values at that location |
| Scrollbars (when zoomed) | Pan the visible region on both axes |

---

## Troubleshooting

| Symptom | Likely Cause / Resolution |
|---|---|
| **"DAQ INIT FAILED"** on startup | NI-DAQmx driver not installed, NI chassis not connected, or device names in NI MAX do not match `Pressure` and `Volume`. |
| **"TDC not detected"** | Engine not running, crank angle sensor not connected, or CAS connected to the wrong port. Confirm the sensor is wired to the **bottom port** of the NI 9401 module. |
| RPM displays as 0 during acquisition | TDC pulses are not being detected. Check the encoder cable and confirm the engine is running above idle. |
| P-V loop appears inverted or peak pressure is at the wrong volume | Phase correction may need adjustment. Verify the engine is firing on all cylinders and the pressure sensor is securely seated. |
| Intake/exhaust strokes show pressure significantly below atmospheric | BDC pegging reference may be incorrect. Verify the `ATMOSPHERIC_PRESSURE_BAR` constant matches local atmospheric conditions. |
| Application becomes unresponsive | Click **Stop** and wait several seconds for the acquisition thread to complete. If the issue persists, close and reopen the application. |

---

## Closing the Application

Click the standard Windows close button (**X**) in the top-right corner. If acquisition is running, the application will stop the acquisition thread and release the NI hardware before closing. It is good practice to click **Stop** before closing to ensure a clean shutdown of the DAQ tasks.

---

## Project Structure

```
Engine Practical/
├── FormPV.cs               # Main form — acquisition, processing, and plotting logic
├── FormPV.Designer.cs      # Auto-generated form layout
├── Program.cs              # Entry point
├── App.config              # Application configuration
├── Engine Practical.csproj
├── Engine Practical.sln
└── Properties/
```

---

*Developed at Queensland University of Technology — Engine Laboratory Practical, 2026.*
