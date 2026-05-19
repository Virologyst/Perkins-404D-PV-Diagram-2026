# Engine Practical — PV Diagram Software

Windows Forms application (C#/.NET) that displays live pressure-volume diagrams from a Perkins 404D-22 diesel engine using National Instruments DAQ hardware.

## Tech Stack

- **Language:** C# (.NET Framework, target x86)
- **UI:** Windows Forms with `System.Windows.Forms.DataVisualization.Charting`
- **DAQ:** NationalInstruments.DAQmx (NI-DAQmx driver v18.0)
- **Hardware:** NI 9223 (analog, named "Pressure"), NI 9401 (digital, named "Volume")
- **IDE:** Visual Studio / JetBrains Rider

## Project Structure

```
Engine Practical/
├── FormPV.cs          # Main form — ALL acquisition, processing, and plotting logic
├── FormPV.Designer.cs # Auto-generated form layout (do not hand-edit)
├── Program.cs         # Entry point (standard WinForms bootstrap)
├── App.config         # Application configuration
├── Properties/        # AssemblyInfo, Resources, Settings (auto-generated)
└── Dependencies/      # NuGet packages and NI libraries (DO NOT READ)
```

`FormPV.cs` is the only file with meaningful application logic. Everything else is boilerplate or auto-generated.

## Key Constants

```
SAMPLES              = 20000        (samples per acquisition block)
SAMPLE_PER_SECOND    = 20000        (20 kHz sample rate)
CLEARANCE_VOLUME_M3  = 0.00002485   (24.85 cm³)
DISPLACEMENT_VOLUME_M3 = 0.0002771  (277.1 cm³) — NOTE: may need updating to 0.00055418
ROD_TO_CRANK_RATIO   = 4.3          — NOTE: documentation says 3.3
CHARGE_AMP_BAR_PER_VOLT = 58.82     (pressure scaling: 1/0.017 V/bar)
```

## Architecture

1. **Acquisition Loop** — background thread, calls `AcquireAndProcessOnce()` repeatedly
2. **TDC Detection** — `DetectTdcPulseCenters()` finds widest pulse via local-max comparison
3. **Angle Mapping** — linear interpolation between TDC centres, 0–720° for full 4-stroke cycle
4. **Phase Correction** — `CalculatePhaseOffset()` shifts angles so peak pressure lands at ~410° (50° ATDC)
5. **Volume Calculation** — slider-crank formula: `V = Vcl + (Vd/2)(1 - cosθ + β - √(β² - sin²θ))`
6. **Plotting** — stroke-coloured traces (Intake=blue, Compression=orange, Power=red, Exhaust=green)

## Sensor Naming

The strings `"Pressure/ai0"` and `"Volume/port0/line0:7"` in `ConfigureTasks()` must match device names in NI MAX. If hardware changes, update these strings.

## Known Issues / Watch Out

- Engine geometry constants may not match the lab documentation (β=3.3, Vd=5.5418e-4). Verify before changing.
- Pressure baseline is AC-coupled (PCB 482B06) — no absolute zero reference.
- Phase correction target (410°) assumes ~50° sensor lag. Adjust if sensor chain changes.
- The encoder has ~360 small teeth + 4 gaps at 90° + 1 wide TDC gap. The local-max TDC detection works but can occasionally misidentify a 90° gap as TDC.

## CSV Format

```
Volume_m3, Pressure_bar, CrankAngle_deg, PressureVolts, RawAngle_deg
```

TDC sample indices are appended as `# TDC[n]=index` comment lines at end of file.

## Code Style

- No particular formatter enforced. Match existing style in FormPV.cs.
- UI updates from background threads must use `InvokeRequired` / `BeginInvoke`.
- Always call `Stop()` and `Dispose()` on DAQ tasks after use.
