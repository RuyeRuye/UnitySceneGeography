# Changelog

All notable changes to this package are documented here.

## [0.1.0] - Unreleased

Initial public release preparation.

### Added

- Double-precision geographic conversion through WGS84, ECEF and a fixed local ENU frame, with explicit horizontal and height references.
- SingleAnchor, Upright and Spatial similarity calibration, independent Fit/Check residuals, diagnostics and configurable acceptance limits.
- WGS84, GCJ-02 and BD-09 input/output within the documented supported ranges.
- Unity Reference, immutable snapshots, numeric local mappings, coordinate/Pose conversion, position/orientation bindings and reverse position/attitude reporters.
- Calibration Profiles, scene control-point Markers, custom Inspectors and a calibration workbench with working drafts, explicit point replacement/removal, Undo and separate calculate/save/apply actions.
- English-default Editor UI with an English / Simplified Chinese language setting stored per user.
- Optional synthetic Basic Sample, Input System free camera, sample-only unlit shader, and EditMode/PlayMode tests.
- English and Simplified Chinese manuals with compiled API examples.

### Limitations

- Designed for local scenes spanning a few kilometers, not a global GIS or arbitrary CRS importer.
- Root and relevant parent transforms require positive uniform scale; reflection, shear and nonuniform scale are rejected.
- Orthometric height conversion requires an external height model and is not supplied by this package.
- GCJ-02 / BD-09 conversion is approximate and rejects coordinates outside the documented numeric rectangle.
- Active calibration is in memory; initialize again after script reload or scene loading.
- Unity 2021.3 LTS is the declared minimum. The Basic Sample additionally requires Input System.
