# DLG QA Plan Generator

> Automated DLG QA plan generation for Varian Eclipse via ESAPI 16.1 — replaces ~40 minutes of manual field creation with a single click.

[![ESAPI](https://img.shields.io/badge/ESAPI-16.1-blue)](https://www.varian.com/)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.8-purple)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Eclipse%20TPS-lightgrey)]()
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![Version](https://img.shields.io/badge/version-0.9.1-orange)](CHANGELOG.md)

![DLG Sweeping Gap Animation](docs/img/dlg-sweep.gif)

*The DLG sweeping gap moving from −70 to +70 mm across 11 control points.*

---

## Overview

**DLG QA Plan Generator** is an open-source ESAPI script that automatically creates a complete Dosimetric Leaf Gap (DLG) QA plan inside Varian Eclipse. It generates a synthetic phantom, structure set, QA course, external plan, and the full set of beams required for DLG measurement — including the open reference field, MLC transmission A/B fields, and user-selected sweeping-gap fields.

The script assigns and verifies all monitor units automatically using `CalculateDoseWithPresetValues()`, then runs a battery of geometry and sequence validations before presenting a summary report.

**One click. Fully reproducible. Verifiable.**

---

## Table of Contents

- [Why This Exists](#why-this-exists)
- [Features](#features)
- [How It Works](#how-it-works)
  - [The MLC-Outside-Jaws Technique](#the-mlc-outside-jaws-technique)
  - [Field Geometry](#field-geometry)
- [Screenshots](#screenshots)
- [Requirements](#requirements)
- [Installation](#installation)
  - [Compiling the Script](#compiling-the-script)
  - [Troubleshooting the Build](#troubleshooting-the-build)
  - [Deploying to Eclipse](#deploying-to-eclipse)
- [Usage](#usage)
- [Verification](#verification)
- [Architecture](#architecture)
- [Configuration](#configuration)
- [Regenerating Documentation Assets](#regenerating-documentation-assets)
- [Limitations](#limitations)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [Citation](#citation)
- [License](#license)
- [Disclaimer](#disclaimer)

---

## Why This Exists

Dosimetric Leaf Gap (DLG) QA is required for accurate IMRT/VMAT dose calculation, but building the QA plan manually inside Eclipse is:

- **Repetitive** — the same geometry is recreated every session.
- **Error-prone** — a single mis-typed MU or leaf position invalidates the measurement.
- **Time-consuming** — roughly 30–45 minutes of physicist time per session.

This tool eliminates the manual work entirely. What used to be a 40-minute procedure is now a 30-second operation with automated verification.

---

## Features

- ✅ **Full plan generation** — phantom, structure set, course, and external plan created programmatically.
- ✅ **Complete beam set** — OPEN reference, transmission A, transmission B, and all user-selected sweeping gaps.
- ✅ **Preset MU assignment** — MU values applied via `CalculateDoseWithPresetValues()` and re-read from Eclipse to verify assignment.
- ✅ **Automated validation** — gap geometry, CMW monotonicity, and outside-jaw reference verification run on every generated beam.
- ✅ **Professional UI** — grouped configuration form with sensible defaults and per-field MU controls.
- ✅ **Unique ID generation** — automatic collision resolution for course, plan, phantom, and beam IDs.
- ✅ **Portable build** — the included `build.bat` auto-detects your ESAPI installation across multiple Eclipse versions and install paths.
- ✅ **Multi-energy support** — 6X, 10X, 15X, 6X FFF, and 10X FFF.
- ✅ **Robust error handling** — every ESAPI capability is checked before execution, with actionable error messages.

---

## How It Works

### The MLC-Outside-Jaws Technique

Eclipse's Sliding Window (SW) validation requires **MLC motion across control points** in addition to a monotonic cumulative meterset weight (CMW) sequence. This creates a problem for reference fields like OPEN and TX, which must have **static geometry** inside the jaw-defined aperture to serve as measurement references.

The solution used here is to sweep the MLC **entirely outside the X jaws**:

- The MLC leaf pairs maintain a constant gap across all control points.
- The entire MLC pattern translates by a small amount (2 mm) in a direction **away** from the jaw-defined aperture.
- Inside the jaws, the exposed geometry is **identical** at every control point.
- Eclipse's SW validator sees leaf motion and accepts the beam.

This keeps the beams physically equivalent to static fields while satisfying the IMRT beam model requirements.

### Field Geometry
JAWS: X1 = -50 mm X2 = +50 mm
Y1 = -90 mm Y2 = +90 mm
→ 100 x 180 mm aperture

OPEN: |<───────── MLC ──────────────── MLC ─────────>|
bank0 ≤ X1 bank1 ≥ X2
Constant gap. 2 mm hidden sweep away from jaw.

TXA: [1 mm MLC strip]
center at X2 + 10 mm
Both banks > X2 (outside right jaw)

TXB: [1 mm MLC strip]
center at X1 - 10 mm
Both banks < X1 (outside left jaw)

DLG: |<── gap ──>|
Both banks sweep from -70 mm to +70 mm
11 control points, equal spacing
Gap values: 2, 4, 6, 10, 14, 16, 20 mm


---

## Screenshots

### Configuration form

![Configuration Form](docs/img/ui-config.png)

*The configuration form exposes machine, energy, dose rate, MU targets, and gap selection.*

### Generation summary

![Generation Summary](docs/img/ui-result.png)

*The summary form reports all automated geometry checks and assigned MU values.*

### Field geometry diagram

![Field Diagram](docs/img/diagram.png)

*Schematic of the generated beams: jaw aperture, OPEN reference, TX strips, and the DLG sweep.*

---

## Requirements

| Component | Version |
|---|---|
| Varian Eclipse TPS | 16.1 or later |
| ESAPI | 16.1 or later |
| .NET Framework | 4.8 |
| Windows | 10 / 11 (x64) |
| Patient write access | Required (`IsWriteable = true`) |

> ⚠️ **Validated against Eclipse 16.1.** Later versions may work but have not been tested.

---

## Installation

### Compiling the Script

1. **Clone the repository:**

   ```bash
   git clone https://github.com/lucasbritocFis/esapi-dlg-qa-generator.git
   cd esapi-dlg-qa-generator
2. **Run the build script:**

Double-click build.bat (or run it from a command prompt). The script:

Locates your C# compiler (csc.exe) automatically.

Locates your ESAPI installation automatically (see below).

Compiles src/DLGGenerator.cs into DLGGenerator.esapi.dll.

3. **Confirm the build succeeded. You should see:**
================================================================
  BUILD SUCCESSFUL
================================================================

Output: DLGGenerator.esapi.dll

If the build fails, see Troubleshooting the Build.

Troubleshooting the Build
The build.bat script attempts to locate your ESAPI installation automatically. It searches the following locations in order:

Environment variable ESAPI_ROOT — if you (or your institution) have set it.

File esapi_path.txt in the repository root — if you created it manually.

Common installation paths across Eclipse versions 15.6 through 18.0:

C:\Program Files\Varian\RTM\<version>\esapi\API

C:\Program Files (x86)\Varian\RTM\<version>\esapi\API

D:\Program Files\Varian\RTM\<version>\esapi\API

D:\Program Files (x86)\Varian\RTM\<version>\esapi\API

If auto-detection fails, you have two options:

Option 1 — Create a configuration file. In the repository root, create a file named esapi_path.txt containing the full path to your ESAPI API folder, on a single line:

text
C:\Program Files (x86)\Varian\RTM\16.1\esapi\API
Then run build.bat again.

Option 2 — Set an environment variable. From a command prompt:

bat
setx ESAPI_ROOT "C:\Program Files (x86)\Varian\RTM\16.1\esapi\API"
Restart your terminal, then run build.bat. This is the recommended approach for institutions with multiple users on the same machine.

Deploying to Eclipse
Copy the generated DLGGenerator.esapi.dll to your Eclipse scripts folder. Typical location:

text
\\<server>\VA_DATA$\ProgramData\Vision\PublishedScripts\
The exact path depends on your Eclipse configuration. Consult your local Eclipse administrator if unsure.

Restart Eclipse, or refresh the scripts list from the External Beam Planning workspace.

Confirm the script appears under External Beam Planning → Tools → Scripts.

Usage
Open a QA patient in Eclipse (any patient with write access).

Launch the script from the Eclipse Scripts menu.

Configure the plan in the form:

Machine ID — must match the Treatment Unit ID exactly as configured in Eclipse.

Energy — 6X, 10X, 15X, 6X FFF, or 10X FFF.

Dose rate — auto-suggested based on energy selection.

Course ID / Plan ID / Field prefix — naming conventions (max 16 / 16 / 10 chars).

MU values — separate targets for OPEN, TX A/B, and DLG gaps (default: 100 MU each).

Sweeping gaps — select which gap widths to generate.

Confirm the summary dialog.

Review the generated plan in Eclipse before proceeding to dose calculation.

Verification
Every generated plan undergoes automated validation before the success summary is shown:

Check	Description
Gap geometry	For each DLG beam, verifies that the central leaf-pair gap matches the requested value across all control points (tolerance ±0.01 mm).
CMW monotonicity	Verifies that meterset weights start at 0, end at 1, and increase monotonically across all control points.
Reference beams outside jaws	Confirms that OPEN/TX beams maintain their geometry outside the jaw-defined aperture at every control point.
MU assignment	After CalculateDoseWithPresetValues(), re-reads beam.Meterset.Value from Eclipse and compares against the requested values (tolerance ±0.01 MU).
If any check fails, the summary form highlights the affected beam with an [ERR] marker.

Architecture
text
src/DLGGenerator.cs
├── Script                          Entry point, orchestration
├── ConfigurationForm               User input (machine, energy, MU, gaps)
├── ResultForm                      Generation summary and check report
│
├── Beam generation
│   ├── AddReferenceSlidingWindowBeam   OPEN / TX A / TX B
│   └── AddDlgBeam                      Sweeping-gap fields
│
├── Phantom
│   └── CreateWaterBody                 Synthetic water phantom with HU=0
│
├── Monitor units
│   └── ApplyPresetMonitorUnits         Prescription + dose calc + MU verify
│
└── Validation
    ├── ValidateGap
    ├── ValidateCmw
    └── ValidateReferenceBeamOutsideJaws
Key ESAPI APIs used
Patient.AddEmptyPhantom() — synthetic phantom generation

StructureSet.AddStructure("EXTERNAL", ...) — water body

Course.AddExternalPlanSetup() — plan creation

ExternalPlanSetup.AddSlidingWindowBeam() — SW IMRT beams

Beam.GetEditableParameters() / ApplyParameters() — leaf and jaw control

ExternalPlanSetup.SetPrescription() — technical QA prescription

ExternalPlanSetup.CalculateDoseWithPresetValues() — MU application

Configuration
Constants at the top of src/DLGGenerator.cs can be edited for institutional variation:

Constant	Default	Description
DEFAULT_MU_OPEN	100	Default MU for OPEN reference
DEFAULT_MU_TX	100	Default MU for transmission A/B
DEFAULT_MU_DLG	100	Default MU for gap beams
X1, X2, Y1, Y2	-50, +50, -90, +90	Jaw positions (mm)
OPEN_MLC_PADDING_MM	10	MLC overreach beyond jaws for OPEN
TX_OVERREACH_MM	10	TX strip offset from jaw edge
TX_STRIP_WIDTH_MM	1	TX strip width
REFERENCE_HIDDEN_SWEEP_MM	2	Hidden MLC motion for SW validation
SWEEP_START, SWEEP_END	-70, +70	DLG sweep range (mm)
N_STEPS	11	Control points per beam
AVAILABLE_GAPS	2, 4, 6, 10, 14, 16, 20	Selectable gap widths (mm)
Regenerating Documentation Assets
The animated GIF and field geometry diagram are generated programmatically, so you can regenerate them if you modify the geometry or want to adjust the visuals.

Asset	Script	Output
Field geometry diagram	docs/diagram.py	docs/img/diagram.png
Animated sweep	docs/animate.py	docs/img/dlg-sweep.gif
Both scripts run in Google Colab without any local installation:

Open colab.research.google.com.

Create a new notebook.

Copy the content of docs/diagram.py into a cell and run it. Download diagram.png from the Colab Files panel.

Copy the content of docs/animate.py into a new cell and run it. Download dlg-sweep.gif.

Upload both files to docs/img/ in the repository.

The scripts use only matplotlib and Pillow, both pre-installed in Colab.

Limitations
Validated against Eclipse 16.1 only. Other versions may require adjustments.

Machine-specific. The default geometry assumes a 100 × 180 mm jaw field and 2 mm hidden sweep. Different machines may require recalibration.

No automated dose analysis. The script generates and assigns MU, but does not compare measured vs. calculated DLG.

Photon-only. Electron DLG is not applicable and not supported.

Single prescription. Uses a technical QA prescription (1 × 100 cGy @ 100%) as the mechanism for preset MU calculation.

UI in English. No localization yet.

Roadmap
□ v0.9.x — XML documentation, unit tests for pure validation functions.
□ v1.0.0 — Externalized machine configuration (machines.json), structured logging, CLI mode.
□ v1.1.0 — Multi-machine profiles, machine-specific hidden sweep tuning.
□ v1.2.0 — Automated DLG analysis from measured data (CSV import).
□ v2.0.0 — Integration with Daily QA automation suite.
See CHANGELOG.md for detailed version history.

Contributing
Contributions are welcome. This is a clinical tool, so please:

Open an issue before submitting a large PR, so we can discuss the design.

Follow the existing code style (C# conventions, ESAPI idioms).

Add validation for any new geometry or beam type.

Test in a non-clinical environment first.

Pull requests that change geometry defaults should include a justification and, ideally, validation data.

Citation
If you use this tool in a publication or clinical workflow, please cite:

text
Cavalcanti, L. B. (2025). DLG QA Plan Generator (Version 0.9.1) [Computer software].
https://github.com/lucasbritocFis/esapi-dlg-qa-generator
A formal Technical Note is in preparation.

License
This project is licensed under the MIT License. See LICENSE for details.

Disclaimer
⚠️ This software is provided as-is, without warranty of any kind.

It is intended for use by qualified medical physicists in a controlled clinical environment. All generated plans must be independently reviewed and verified before any clinical use. The author assumes no responsibility for clinical outcomes resulting from the use of this tool.

This project is not affiliated with, endorsed by, or sponsored by Varian Medical Systems. Eclipse and ESAPI are trademarks of Varian Medical Systems, Inc.

Author
Lucas de Brito Cavalcanti
Medical Physicist

GitHub: @lucasbritocFis
   
