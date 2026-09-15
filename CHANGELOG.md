
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
## [0.9.2] - 2026-09-15

### Added
- Leaf-speed check before plan creation (sweeping-gap and OPEN/TX fields), reporting the minimum MU.
- Consistency check of the geometry constants before plan creation.
- Warning to close the patient without saving when an error occurs after generation has started.

### Changed
- Generation summary now includes OPEN/TX results in the overall status.
- Direction of the hidden OPEN/TX sweep is based on the jaw center (correct for asymmetric jaws).
- Portable build script: paths relative to the script, version read from APP_VERSION, output in bin\.

## [0.9.1] - 2025-09-15

### Changed
- Added null-safety check on `CalculationResult` before accessing `.Success`.
- Expanded inline documentation for `TECHNIQUE_ID` and `REFERENCE_HIDDEN_SWEEP_MM` constants.
- Removed dead code (`Border` field in `ConfigurationForm`, unused `AddNumericBox` method).

### Fixed
- Stale file-header comment still referenced v0.8.

No change to clinical behavior; this is a defensive patch.

## [0.9.0] - 2025-09-15

### Added
- Initial public release.
- Automated generation of DLG QA plan (phantom, course, plan, beams).
- Support for OPEN reference, MLC transmission A/B, and sweeping gaps (2–20 mm).
- Preset MU assignment via `CalculateDoseWithPresetValues()` with post-calculation verification.
- Automated geometry checks: gap validation, CMW monotonicity, outside-jaw reference verification.
- Professional WinForms configuration and summary UI.
- Unique ID generation for course, plan, phantom, and beams.

### Known Limitations
- Validated against Eclipse 16.1 only.
- Machine-specific geometry defaults (100 x 180 mm jaws, 2 mm hidden sweep).
