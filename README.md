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
