# Scene Geography

**English** | [简体中文](README.zh-CN.md)

Scene Geography converts latitude, longitude and height into Unity scene positions, and converts scene positions back into geographic coordinates. Calibrate a model against known corresponding points, then place POIs, set geographic orientations, or display the player's coordinates.

It is intended for local scenes spanning a few kilometers, such as streets and campuses. It is independent of the renderer: point-cloud loading, business-data import, player movement, collision and navigation remain the application's responsibility.

- Sections 1–2: installation, scene setup, calibration and snapshots.
- Sections 3–5: configuration reference.
- Sections 6–7: runtime and Editor API examples.
- Sections 8–9: coordinate conventions, samples, tests and troubleshooting.

## 1. Set up a scene

### 1.1 Install the package and try the sample

Install this folder as a UPM package using **Window > Package Manager > + > Add package from disk**, selecting its `package.json`, or place it in the project's `Packages` directory as an embedded package. The package ID is `io.github.ruyeruye.scene-geography`. For an embedded installation, Unity recommends naming the folder after this ID; the display name remains **Scene Geography**.

Unity 2021.3 LTS is the declared minimum. Editor object discovery uses a version-specific compatibility wrapper; the newer FindObjectsByType API is not required on 2021.3.

The production assemblies do not require the sample or the Input System. If your scripts use assembly definitions, reference `SceneGeography.Core` and `SceneGeography.Unity`; Editor tools additionally reference `SceneGeography.Editor`.

To try the optional demo:

1. Install **Input System** (`com.unity.inputsystem`) from Package Manager. Enable **Input System Package (New)** or **Both** under **Player > Active Input Handling** if needed; Unity may request a restart.
2. Select **Scene Geography** in Package Manager, expand **Samples**, and import **Basic Sample**.
3. Open `Scenes/GeoReference-Upright.unity` under the imported sample in `Assets/Samples/Scene Geography/<version>/Basic Sample`, then enter Play mode.

The scene contains synthetic buildings, five POIs, a free camera and an interactive panel. No additional components are needed. Source files in `Samples~/BasicSample` are deliberately excluded from normal package compilation; use the imported copy, not the scene inside the package folder.

The sample includes its own simple unlit Shader for Built-in and URP, loaded from the imported sample's `Resources/SceneGeographySample` folder so it is retained in Player builds. Keep that folder when using the demo. It does not select a URP Shader merely because URP is installed. HDRP sample rendering is not supported; the production geography APIs are render-pipeline independent.

Sample coordinates and geometry are synthetic. Real models need their own control points and Profile.

### 1.2 Connect your own model

1. **Choose a stable model root.** Place the model under a Transform such as `GeographicRoot`. This becomes the geographic Root; it does not need a geographic component.
2. **Check scaling.** The combined world transform of Root and its ancestors must contain only translation, rotation and positive uniform scale. Nonuniform scale, reflection, shear and zero scale are rejected. POI parent transforms have the same restrictions.
3. **Add a manager.** Attach `GeoReference` to an empty GameObject and assign Root. Leave Root empty to work directly in world space. Set Local Frame Id, for example `block-a-model-v1`, to identify the model's local coordinate convention.
4. **Create a calibration asset.** Use **Create > Scene Geography > Calibration Profile** in the Project window. Set the same Local Frame Id and choose a calibration mode.
5. **Enter corresponding points.** Each point needs a Root-local model position, geographic coordinate, horizontal coordinate type and height reference. Prefer well-distributed Fit points and one or two independent Check points.
6. **Calculate and inspect.** Assign Profile to Reference and click **Calculate Candidate (no object writes)**. Review scale, residuals and diagnostics.
7. **Add POIs.** Attach `GeoAnchor` to each POI and configure Reference, coordinate and offsets. Add `GeoOrientation` if it also needs a geographic rotation.
8. **Preview.** Click **Apply Candidate** on Reference. Enabled bindings update once. Check the resulting positions and rotations.
9. **Initialize at runtime.** Enable Initialize On Enable for a static scene, or call `TryInitialize` once the asynchronously loaded model, Root and configuration are ready.
10. **Connect the player.** Keep the player's own movement controller. Add `GeoPositionReporter` to display coordinates; do not add a continuously positioning Anchor to a freely moving player.

Keep POI anchors as siblings under Root where possible, with labels and visuals below each anchor. Avoid parenting one geographic anchor under another: automatic updates do not solve arbitrary transform dependency graphs.

### 1.3 Choose a calibration mode

| Mode | Required information | Typical use |
| --- | --- | --- |
| SingleAnchor | One Fit point, local Up, a local direction with a known geographic heading, and local units per real meter | Position alignment when scale and orientation are already known |
| Upright | At least two Fit points with a horizontal baseline, and local Up | Buildings or street scenes with a known vertical axis; usually the first choice |
| Spatial | At least three non-collinear Fit points | A tilted model, or no reliable local Up |

Upright and Spatial can estimate uniform scale or use a known fixed scale. Spatial accepts coplanar points, but not a fully collinear set. Upright fits heading, translation and uniform scale; it does not tilt or stretch the model to eliminate height discrepancies.

Fit points determine the solution. Check points do not affect fitting or the automatic origin; they measure accuracy elsewhere. A small Fit residual alone does not establish accuracy throughout the scene.

### 1.4 Capture and update controls in the calibration workbench

The Editor UI defaults to **English**. Use **Language > English / 简体中文** in the workbench toolbar to switch the workbench and related Inspectors. This preference is saved in the current user's EditorPrefs, not in a Profile, scene or project setting. Switching language preserves the working draft, point operations, capture selection and candidate. API names, IDs, enum identifiers and low-level error diagnostics remain unchanged; no Unity Localization or Addressables dependency is added.

Place empty GameObjects at known model positions and add GeoControlPointMarker. Assign Reference, unique Point Id, Role, Weight and explicit geographic coordinates. Open **Window > Scene Geography > Calibration Workbench**, or use the Marker Inspector's workbench button. 

- The top toolbar contains read/refresh scene, calculate, save and apply actions. The center is a two-pane workspace; the bottom is a single status line with full details on hover. There is no top-level Reference picker or duplicate asset-navigation button.
- The left hierarchy is scene → Reference → related objects, with themed cards and explicit Marker, Anchor, Orientation and Reporter badges. Each Reference shows its read-only scene reference, assigned Profile, asset path and component counts. Use “Edit calibration” to activate it. Inactive objects and disabled components are included; unassigned/invalid-reference components have a separate group in their own scene. Prefab/preview scenes are excluded.
- Components on the same object using the same Reference share one row. Components using different References appear in their respective groups, regardless of Transform parenting.
- Component-type filters use union semantics; search matches names, hierarchy paths and Point IDs. Filtering does not clear capture selection. Select/deselect visible Markers affects only visible controls in the active Reference, retaining hidden selections.
- Click an object name to inspect/edit its scene components in the object-details tab. Locate selects the object; preview frames it in Scene view without moving it. Anchor, Orientation and Reporter are not calibration inputs. Other tabs contain the configuration draft and calculation reports.
- Read scene reads all normal loaded scenes and resets filters; refresh preserves filters. Both preserve a valid current working copy and do not save/apply automatically.
- Select a target Profile and the Markers to capture. Merge by Point Id is the default: unselected points and existing mode, heading, scale and acceptance settings are preserved. Full replacement lists removed IDs and requires confirmation. Renaming an ID adds a new point; it does not silently delete the old one.
- Mode presets explicitly select SingleAnchor, Upright or Spatial without resetting other parameters. Inspect/edit the temporary serialized working copy. Use the Profile object fields to inspect or locate the original asset; the workbench does not duplicate its Inspector. External asset edits require reloading the working copy before saving it.
- **Calculate candidate** calculates without asset/transform writes. **Save to Profile** saves a validated configuration without applying it. **Apply candidate (preview only)** applies without saving. **Save and apply** does both. **Save as new Profile…** never overwrites an existing asset; binding the new asset to Reference is an explicit option.
- Input/selection/mode changes invalidate candidates. Concurrent target-asset edits block stale saves. Saving rebases the working copy, preserves the existing asset GUID and supports Undo. Undo/reload does not restore the in-memory calibration; calculate/apply again.

Closing a modified working draft (parameters or point operations) prompts Save/Discard/Cancel. Draft inputs survive assembly reload, but candidates do not. Unsaved external Profile edits still block overwriting after recovery.

Synchronization comparisons absorb only double serialization round-off, not a fixed model-unit tolerance. Descendant Markers are captured through their local transform chain so moving Root does not manufacture point changes.

Capture converts positions to Reference.Root-local coordinates; Markers need not be direct children of Root. Moving a Marker does not automatically write Profile. Marker-to-Profile sync and Profile-to-active-calibration state are displayed separately. Reference's existing calculate/apply buttons remain available.

#### Maintain renamed or unmatched control points

The Reference group also lists points present only in its configuration, with the status **Configuration only: no scene Marker found** (configuration only: no scene Marker found). Matching uses the same Reference and Point ID across all currently loaded normal scenes, including inactive objects. An absent Marker does not prove deletion: the point may have been entered numerically or its scene may be unloaded. Such rows provide read-only configuration values, not scene locate/preview actions or capture checkboxes. They follow the Marker filter and Point ID search. Activate the Reference with **Edit calibration** before changing its points.

- **Remove from configuration** (remove from configuration), shown in red, stages a removal. The row becomes **Pending removal** and offers **Undo removal**. No GameObject is deleted, and the Profile remains unchanged until saving.
- After renaming a Marker from A to B, use **Replace existing configuration point…** on B and explicitly choose A. This stages **A → B** as one replacement, automatically includes B in capture, and preserves all other configuration points. It does not temporarily retain two Fit points in the candidate. No position-based or SingleAnchor-specific automatic rename inference is performed.
- **Undo replacement** restores the previous capture selection. The destination stays selected while a replacement is pending; undo the replacement before deselecting it. If a destination is renamed again, destroyed or assigned another Reference, or the old ID reappears in the scene, the replacement becomes invalid rather than silently changing its meaning. Undo it and choose again.
- The change summary and save confirmation distinguish additions, updates, replacements and removals. Explicit replacements/removals can be combined with full-set replacement. The final candidate must still satisfy the selected mode's Fit requirements; removing the only Fit point cannot be saved.
- Parameter edits and pending point operations participate in close protection, context-switch confirmation and assembly-reload recovery. Discard clears the working operations, not edits to scene Markers. External Profile edits still block stale saves. Saving does not apply calibration unless **Save and apply** was chosen.

### 1.5 Auto-fill Reference and Marker IDs

**Auto Fill Reference & Point ID** in the Marker Inspector fills empty fields and supports multiple selection:

- An empty Reference is assigned only when the component's own scene contains exactly one GeoReference. Inactive objects and disabled References count too.
- With zero or multiple candidates, the button reports the ambiguity instead of guessing.
- An empty or whitespace-only Point Id uses the GameObject name. Existing IDs and References remain unchanged.
- Duplicate Marker IDs are reported, not automatically numbered. IDs must be unique within the same Reference.
- References are never taken from other loaded scenes. Prefab assets, Prefab Mode and preview scenes are not automatically bound to scene objects.

Anchor, Orientation, Reporter and sample Inspectors provide **Auto Assign Reference**. These buttons are Edit-mode tools with Undo support. They do not save scenes, calculate/apply calibration, or rewrite control-point IDs already stored in a Profile.

Custom Editor tools can inherit `SceneGeography.Editor.GeoReferenceConsumerEditor` to draw default fields plus the same assignment button. The inspected component must have a serialized `GeoReference` field named `reference`.

## 2. Configuration, calculation, application and snapshots

### 2.1 Profile is input; Reference holds the active calibration

A Profile records facts such as “this local model position corresponds to this geographic coordinate,” the local Up direction, and whether scale should be estimated. These are inputs to calibration, not a saved solution.

**Calculate** reads these inputs and solves translation, rotation and scale, producing a **Candidate** and report. It does not change the active coordinate system or move objects.

**Apply** commits that candidate to GeoReference. Subsequent conversions and binding updates use the applied calibration rather than solving the Profile on every call.

**Editing a Profile does not change an already active calibration.** Calculate and Apply again. Failed calculation or application preserves the previous usable calibration, so partially edited inputs do not move existing POIs.

### 2.2 Edit-mode Apply is a one-shot preview

| Action | Solves calibration? | Effect on objects |
| --- | --- | --- |
| Calculate Candidate | Yes, from the current Profile | None; creates a candidate and report |
| Apply Candidate | No; commits the prepared candidate | Updates enabled Anchors, then Orientations, then samples Reporters |
| Anchor: Apply Position Now | No; uses active calibration and current Anchor input | Updates this object's position |
| Orientation: Apply Orientation Now | No; uses active calibration and current Orientation input | Updates this object's rotation |
| Reporter: Sample Coordinates Now | No | Reads coordinates without moving anything |
| Clear Calibration | No; removes the active calibration | Does not reset or undo existing transforms |

Edit mode does not continuously rewrite transforms. After editing Anchor coordinates or an Orientation target, use the component's apply button again. After editing Profile, Calculate and Apply again.

Moving the same Root does not require recalibration, but Edit-mode bindings do not follow continuously. Calculate/Apply a new candidate for a grouped preview, or apply positions and then orientations individually. Dragging a POI to see whether it snaps back is not a reliable initialization check.

Editor application records Transform Undo and marks affected scenes dirty, without saving them. Undo restores transforms, not the in-memory calibration. After undoing a preview, Clear or Calculate/Apply again to make the intended state explicit.

### 2.3 Read calibration status and reports

The Reference Inspector's **Calibration status** distinguishes:

| Status | Meaning |
| --- | --- |
| Candidate ready to apply = Yes | A successfully calculated candidate is available; Apply is enabled |
| Active calibration = Yes | Reference has an applied calibration for conversions |
| Calibration version | Increments on successful application or Clear; not simply an application counter |
| Candidate report (not applied) | Report for the prepared candidate |
| Last calculation report | Most recent calculation that did not produce an available candidate |
| Active calibration report | Report for the calibration actually used by conversions |

The candidate belongs to the current Inspector instance and may be lost when selection changes recreate the Inspector. These fields describe available state, not a history of button clicks. Both Yes values can coexist: the old calibration is active while a new candidate awaits application. Applying consumes the candidate.

In code, inspect `IsInitialized` and `CurrentReport`. Use `TryCaptureSnapshot` to check that the current Root is also valid. `IsInitialized` does not imply that later Profile edits have been applied.

Profile assets and serialized scene inputs can be saved. Candidates and active solutions exist only in memory; initialize again after script reload or reopening a scene. Runtime initialization must follow the application's loading flow, not depend on an Editor preview.

Hover report labels, values, residual rows or diagnostics for tooltips:

| Report field | Meaning |
| --- | --- |
| Success | Mathematical solve and enabled accuracy limits passed; without limits, this is not business-accuracy acceptance |
| Fit / Check | Number of fitting points / independent check points |
| Local units / meter | Root-local units per real meter, excluding Root world scale |
| Fit RMS (meters) | Weighted root-mean-square 3D Fit error, not the worst point |
| Check max (meters) | Largest measured 3D Check error in real meters, not the configured maximum allowance |
| Per-point E / N / U | Signed residuals in real meters: entered model position minus predicted position, converted back to east/north/up |

`AccuracyThresholdNotConfigured` means none of the four Fit/Check accuracy-limit switches is enabled. Actual errors are still reported, but are not judged against application requirements. The warning does not mean an error exceeded a threshold. Changing a threshold number without enabling its Limit switch does not activate it.

### 2.4 A Snapshot fixes a coordinate mapping

A Snapshot lets a batch of operations use **one calibration and one Root transform**. It does not copy every Inspector field or freeze all POI input.

1. Capture `snapshotA` while Root is at position A.
2. Use it to convert a batch of POIs.
3. Move Root to B or apply a new calibration.
4. `snapshotA` still uses the old mapping; a newly captured `snapshotB` uses the new state.

Coordinates, offsets and orientation requests passed into each Snapshot call are still supplied by the caller. The mapping is fixed, not the input values. An old Snapshot also remains usable after Reference is cleared.

For a single conversion, call `Reference.TryGeoToWorld` or a similar method directly; it obtains the current mapping internally. For a batch, capture once and reuse it. Do not treat a retained Snapshot as an automatically updating Reference.

### 2.5 Runtime updates

By default, Reference synchronizes in LateUpdate: all positions, then all orientations, then all Reporters, followed by Changed notification. Look-at operations therefore see target positions already updated in that batch.

- **Profile changes:** initialize again, or prepare and apply a new candidate.
- **Same Root moves/rotates/scales uniformly:** the next synchronization uses its new transform without refitting.
- **Different Root or Local Frame Id:** Clear first, then change and initialize.
- **Anchor/Orientation input changes:** use `SetPlacement`, `SetRequest` or related configuration APIs. They mark pending updates; call `TryApply` for immediate writes.
- **Disabled bindings:** stop automatic writes. Explicit `TryApply` is still a deliberate one-shot operation.

### 2.6 When inputs are read

| Data | Read time |
| --- | --- |
| Profile points, mode and scale policy | When building a request and calculating/initializing; not on each conversion |
| Root world transform | When Reference obtains the current mapping or synchronizes; not permanently fixed at Calculate time |
| Anchor/Orientation fields | When applying the component; use configuration APIs to mark runtime changes pending |
| Captured Snapshot calibration and Root transform | Fixed at capture time; later conversions do not reread Reference or Transform |

Most application code can use Reference directly without manually managing Snapshots.

## 3. Calibration Profile parameters

`GeoCalibrationProfile` is an asset, not a GameObject component. The Inspector disables fields irrelevant to the chosen mode without clearing their values.

### 3.1 Parameter reference

#### Basic settings

| Field | Meaning, applicability and default |
| --- | --- |
| Schema Version | Format version, currently 1; read-only in the Inspector |
| Local Frame Id | Model-local coordinate identity; preferably nonempty and equal to Reference. A nonempty mismatched Profile ID rejects initialization |
| Mode | SingleAnchor / Upright / Spatial; default Upright |
| Points | Control points; empty by default. Too few Fit points rejects the solve |
| Local Up | Real-world Up expressed in Root-local space; default (0,1,0). SingleAnchor/Upright only |
| Local Heading Direction | Root-local direction with a known geographic heading; default (0,0,1). SingleAnchor only. Projected onto the Up-horizontal plane; must not be parallel to Up |
| Bound Heading Degrees | Geographic heading of that direction; default 0°, SingleAnchor only |
| Estimate Scale | Default true; estimates uniform scale in Upright/Spatial. SingleAnchor always uses fixed scale |
| Local Units Per Meter | Default 1, strictly positive; editable for SingleAnchor or when Estimate Scale is off |
| Options | Origin, numerical conditioning and acceptance settings |

#### Each control point

| Field | Meaning |
| --- | --- |
| Id | Unique, nonempty ID; duplicates reject the solve |
| Role | Fit participates in solving; Check only evaluates the result |
| Weight | Positive, default 1. Fit weights affect fitting; Check weights affect Check RMS. Maximum errors are unweighted |
| Local Position | Root-local model position in model units, not a world position or an ENU offset in meters |
| Coordinate | Geographic location and references for the same physical point; see section 4 |

#### Origin and numerical conditions

| Field | Meaning, default and applicability |
| --- | --- |
| Use Explicit Origin | Default false; otherwise uses the weighted ECEF center of Fit points |
| Origin Latitude Degrees / Origin Longitude Degrees | Explicit origin in WGS84 degrees; editable only with Use Explicit Origin |
| Origin Ellipsoid Height Meters | Absolute WGS84 ellipsoid height of the explicit origin; does not inherit control-point horizontal or relative-height references |
| Minimum Baseline Meters | Default 0.01 real meters; minimum geographic baseline in Upright/Spatial |
| Near Collinear Ratio | Default 0.0001; larger is stricter. Spatial only; checks for two independent directions, not for non-coplanarity |
| Minimum Scale / Maximum Scale | Default 0.000001 / 1000000; allowed local units per real meter |

#### Accuracy and distance limits

| Switch | Associated value (default) | Limit |
| --- | --- | --- |
| Limit Fit Rms | Fit Rms Meters = 1 | Weighted 3D Fit RMS |
| Limit Vertical Rms | Vertical Rms Meters = 0.5 | Weighted Fit RMS along reference Up |
| Limit Check Rms | Check Rms Meters = 1 | Weighted 3D Check RMS |
| Limit Check Maximum | Check Maximum Meters = 2 | Largest unweighted 3D Check residual |
| Limit Distance | Maximum Distance Meters = 5000 | 3D radius from the ENU origin, including height and final placement offsets |

Local Position always refers to Reference.Root, not necessarily the Marker's immediate parent. Local Units Per Meter means “how many local units equal one real meter,” not its reciprocal. World units per meter additionally include Root's world uniform scale.

SingleAnchor example: if local +Y is Up, local +X is east and one local unit equals one meter, use Local Up=(0,1,0), Local Heading Direction=(1,0,0), Bound Heading Degrees=90 and Local Units Per Meter=1.

### 3.2 Choose an origin and acceptance limits

The automatic origin is suitable for an initial setup. It uses only Fit points. Enable Use Explicit Origin to keep the same geographic reference across calibrations, entering a fixed WGS84 latitude, longitude and ellipsoid height.

All Limit switches are off by default. A threshold is checked only when its switch is enabled. Check limits require Check points. Exceeding an enabled limit rejects the new calibration rather than merely displaying a warning.

Choose thresholds from measurement quality and application requirements. Millimeter-level residuals from synthetic sample data do not imply equivalent accuracy for actual GPS measurements or point clouds.

## 4. Geographic coordinate parameters

Control points, Anchors and geographic look-at targets share these coordinate settings:

| Field | Meaning |
| --- | --- |
| Horizontal Type | Wgs84 / Gcj02 / Bd09; must be specified, Unspecified is invalid |
| Height Kind | Explicit height reference, as below |
| Latitude Degrees | Degrees; north positive, south negative |
| Longitude Degrees | Degrees; east positive, west negative |
| Height Meters | Real meters in the chosen height reference; not Unity Y |
| Ellipsoid Zero Meters | Absolute ellipsoid height of the relative zero; RelativeToEllipsoidZero only |
| Assumed Height Zero | Marks an assumed relative zero and produces a calibration diagnostic; does not improve accuracy |
| Height Model Id | Orthometric only; no height model is currently provided |

| Height Kind | Meaning of Height Meters |
| --- | --- |
| Ellipsoidal | Absolute ellipsoid height; relative-zero settings are ignored |
| RelativeToEllipsoidZero | Absolute ellipsoid height = Ellipsoid Zero Meters + Height Meters; for example 20 + 7 = 27 m |
| Orthometric | Orthometric height/elevation; currently returns MissingHeightModel. Entering a model ID alone does not enable support |

**Use the coordinate type declared by the data provider; do not infer it from numbers.** Relabeling GCJ-02 as WGS84 does not convert it.

Height also needs a known reference. For relative-only data, declare a relative ellipsoid zero and keep the same convention for control points and POIs. Elevation above sea level is not automatically ellipsoid height.

## 5. Scene component parameters

### 5.1 GeoReference: coordinate-system manager

Attach it to a manager object. That object's own transform does not define the coordinate space; Root does.

| Field | Meaning |
| --- | --- |
| Root | Reference Transform; empty means world space. A destroyed assigned Root reports an error rather than falling back. Read-only after initialization until Clear |
| Local Frame Id | Model-local identity; read-only after initialization until Clear |
| Profile | Input for Inspector calculation and enable-time initialization. Editing it invalidates the current Inspector candidate |
| Initialize On Enable | Default false; attempts initialization from Profile on each enable. Leave off for asynchronous loading |
| Synchronize Automatically | Default true; LateUpdate synchronizes positions → orientations → coordinate reporting. Otherwise call SynchronizeBindings |
| Gizmo Length Meters | Default 5 real meters; selected reference-axis visualization only |

### 5.2 GeoControlPointMarker: capture corresponding points

| Field | Meaning |
| --- | --- |
| Reference | Coordinate system used for capture; need not be initialized |
| Point Id | Unique, nonempty control-point ID |
| Role / Weight | Fit or Check, positive weight; default 1 |
| Coordinate | Geographic location corresponding to the Marker's model position |
| Transform Position | Model point in world space; capture converts it into Reference.Root-local coordinates |

Markers are authoring helpers, not runtime requirements. Calibration reads the numbers saved in Profile after capture.

### 5.3 GeoAnchor: position binding

| Field | Meaning |
| --- | --- |
| Reference | Geographic system; waits while unavailable rather than placing at a default zero |
| Coordinate | Original coordinate; use SetPlacement for runtime changes |
| East Offset Meters | Real-meter offset east; negative west, not world X |
| North Offset Meters | Real-meter offset north; negative south, not world Z |
| Up Offset Meters | Real-meter offset along reference Up; negative down, without modifying source height |
| Update Policy | OnChange (default): update when configuration, calibration, Root or parent mapping changes. Continuous: write on every Reference synchronization |
| Start Automatically | Default true. If false, first call TryApply successfully to join subsequent synchronization; this is not a one-shot-placement switch |

OnChange does not continuously correct movement by other scripts; Continuous writes on every synchronization. Do not let Anchor and a player controller compete for the same object.

`TryGetPlacement` reads and validates current input without initialization or transform writes. If mapping is unavailable, Anchor preserves the last position; inspect `HasAppliedPosition` and `LastError`.

### 5.4 GeoOrientation: rotation binding

| Field | Meaning and exclusivity |
| --- | --- |
| Reference | Geographic system |
| Kind | Heading, GeoTarget or WorldTarget; disabled when Use Transform Target is on |
| Heading Degrees | Heading only; north 0°, east 90° |
| Target Coordinate | GeoTarget only; full geographic reference |
| Target World Position | WorldTarget only; fixed position in world units |
| Use Transform Target | Default false; overrides Kind's target source |
| Transform Target | Tracked scene Transform; destruction reports an error and preserves the last rotation |
| Look Mode | Horizontal ignores height difference; Full3D includes it. Not used for a fixed Heading |
| Yaw Offset Degrees | Clockwise turn from the base orientation, degrees |
| Pitch Up Offset Degrees | Positive tilts upward, degrees |
| Roll Clockwise Offset Degrees | Clockwise roll about the forward axis, degrees |
| Model Correction | Model-local correction multiplied after geographic rotation; unit quaternion, default identity, not three Euler angles |
| Start Automatically | Default true; otherwise first apply successfully before automatic synchronization |

`SetRequest` selects a heading or geographic/world-position target. `SetTransformTarget` enables Transform tracking. Destroying a target does not silently select another source.

Prefer an anchor convention of +Z forward and +Y up, rotating the visual child for model-axis corrections when convenient.

### 5.5 GeoPositionReporter: read coordinates

| Field | Meaning |
| --- | --- |
| Reference | Geographic system |
| Target | Initially empty means self. A destroyed explicitly assigned target reports an error, not a fallback to self. Use Configure to change it at runtime |
| Output Type | Wgs84 / Gcj02 / Bd09; default Wgs84 |
| Output Height | Default Ellipsoidal; Orthometric is unsupported |
| Ellipsoid Zero Meters / Assumed Height Zero | Relative-height output settings, as for Coordinate |
| Height Model Id | Orthometric only; no model currently available |
| Sampling Interval Seconds | Default 0.2 seconds; 0 samples every synchronization. TrySample runs immediately, bypassing the interval |

Check `HasValue` before using `LastValue`. A failed sample may retain the last successful value, but it is no longer a valid current result. `PositionUpdated` fires only on successful sampling.

### 5.6 GeoOrientationReporter: read actual orientation

This is the read-only counterpart to GeoOrientation. It works on any Transform, including a player/camera without geographic bindings, and never controls its position or rotation.

| Field | Meaning |
| --- | --- |
| Reference | Supplies the active calibration and fixed geographic axes |
| Target | Initially empty means self; a destroyed explicitly assigned target fails instead of falling back |
| Model Correction | Unit quaternion removed from the right of the actual rotation before measurement; identity measures the Transform's own axes. Use the same correction as GeoOrientation when reporting its logical geographic attitude |
| Sampling Interval Seconds | Default 0.2 seconds; 0 samples each synchronization. TrySample bypasses the interval |

Configure(reference, target) measures actual axes. Configure(reference, target, correction) removes the specified model correction. TrySample returns GeoAttitude; check HasValue before LastValue, then check HasHeading/HasRoll before displaying those angles. Quaternion and pitch remain valid at vertical orientations. OrientationUpdated fires after successful sampling. Failure preserves LastValue but clears HasValue. Disabled components stop automatic sampling; explicit TrySample is still allowed. Both Reporter types run after all position and orientation bindings.

## 6. Runtime API

The runtime namespace is `SceneGeography`. Latitude/longitude use degrees; height and ENU offsets use real meters. `GeoEnuOffset` constructor order is **East, North, Up**. Heading increases clockwise from north: north 0°, east 90°, south 180°, west 270°.

### 6.1 Common entry points

| Task | API |
| --- | --- |
| Select local-space identity | TrySetRoot, TrySetLocalFrameId; Clear first if initialized |
| Initialize or recalibrate in one step | Profile.TryBuildRequest → Reference.TryInitialize |
| Review before committing | TryPrepareCalibration → inspect candidate.Report → TryApplyCalibration |
| Consistent batch conversions | TryCaptureSnapshot; reuse the snapshot for positions, rotations and unit conversions |
| Coordinate-to-position conversion | TryGeoToWorld, TryWgs84ToWorld, TryGeoToLocal, TryWgs84ToLocal |
| Placement with offsets | TryGetWorldPosition(GeoPlacement) |
| Position-to-coordinate conversion | TryWorldToWgs84, TryWorldToCoordinate, TryLocalToWgs84 |
| Axes and rotation | TryGetWorldAxes, TryGetLocalAxes, TryWorldDirectionToHeading, TryGetWorldRotation, TryGetWorldPose |
| Meter/world-unit conversion | TryMetersOffsetToWorld, TryMetersToWorldUnits, TryWorldUnitsToMeters |
| Update component input | Anchor.SetPlacement, Orientation.SetRequest / SetTransformTarget, Reporter.Configure |
| Apply immediately | Anchor.TryApply, Orientation.TryApply, Reporter.TrySample |
| Unity-independent math | GeoCalibrationEngine.TrySolve → solution.Frame |

Handle `error` whenever a Try method returns false; do not consume failed outputs. Immutable-value constructors and some configuration methods throw `ArgumentException` for invalid input.

Reference and component operations belong on Unity's main thread. Captured Snapshots no longer read Transforms and avoid repeated Root checks for batch conversion.

### 6.2 Initialize, place POIs, get a visit pose and read coordinates

The following is a complete example class. Supply valid Reference, Transform and coordinate values, with matching Reference/Profile Local Frame Ids.

```csharp
using UnityEngine;
using SceneGeography;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoRuntimeExamples
    {
        public static bool Initialize(
            GeoReference reference,
            GeoCalibrationProfile profile,
            out GeoCalibrationReport report,
            out GeoError error)
        {
            report = null;
            if (reference == null || profile == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Reference/Profile required.");
                return false;
            }

            if (!profile.TryBuildRequest(out var request, out error))
            {
                return false;
            }

            return reference.TryInitialize(request, out report, out error);
        }

        public static bool PlacePoi(GeoReference reference, GeoCoordinate coordinate, Transform target, out GeoError error)
        {
            // Use one snapshot for position and rotation: 1 m east, 2 m up, heading 105 degrees.
            if (!reference.TryCaptureSnapshot(out var snapshot, out error))
            {
                return false;
            }

            var placement = new GeoPlacement(coordinate, new GeoEnuOffset(1, 0, 2));
            var orientation = GeoOrientationRequest.ForHeading(GeoHeading.FromCardinal(GeoCardinalDirection.East, 15));
            if (!snapshot.TryGetWorldPose(placement, orientation, out var pose, out error))
            {
                return false;
            }

            target.SetPositionAndRotation(pose.position, pose.rotation);
            return true;
        }

        public static bool GetVisitPose(GeoReference reference, GeoCoordinate poi, out Pose pose, out GeoError error)
        {
            // Keep the visit pose separate from the visual anchor: 22 m south and up, looking at the POI.
            var placement = new GeoPlacement(poi, new GeoEnuOffset(0, -22, 22));
            var look = GeoOrientationRequest.ForGeoTarget(poi, GeoLookMode.Full3D);
            return reference.TryGetWorldPose(placement, look, out pose, out error);
        }

        public static bool ConfigureAndApply(
            GeoReference reference,
            GeoAnchor anchor,
            GeoOrientation orientation,
            GeoCoordinate poi,
            out GeoError error)
        {
            anchor.Configure(reference, new GeoPlacement(poi, new GeoEnuOffset(0, 0, 2)));
            orientation.Configure(
                reference,
                GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(90)),
                Quaternion.identity);
            // Configure updates input only. Apply position before orientation for an immediate update.
            return anchor.TryApply(out error) && orientation.TryApply(out error);
        }

        public static bool ReadPlayer(GeoReference reference, Transform player, out GeoCoordinate coordinate, out GeoError error)
        {
            // Specify output references explicitly; RelativeToEllipsoidZero(20) uses a 20 m ellipsoid-height zero.
            return reference.TryWorldToCoordinate(player.position, GeoCoordinateReference.Wgs84Ellipsoidal, out coordinate, out error);
        }
    }
}
```

`PlacePoi` writes the Transform directly and is appropriate only when no other controller owns it. For player teleportation, pass the visit pose through the movement controller's teleport API so its internal state stays synchronized. The sample's `GeoDemoCamera.Teleport` also updates its pitch cache.

### 6.3 Create requests without a Profile

```csharp
using System.Collections.Generic;
using SceneGeography;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoCalibrationExamples
    {
        public static GeoCalibrationRequest Single(
            GeoControlPoint anchor,
            GeoVector3d localUp,
            GeoVector3d knownEast,
            double localUnitsPerMeter)
        {
            var options = GeoCalibrationOptions.CreateDefault();
            // knownEast is geographic east in Root-local space, so its bound heading is 90 degrees.
            return GeoCalibrationRequest.SingleAnchor(
                anchor,
                new GeoSingleAnchorSettings(localUp, knownEast, GeoHeading.FromDegrees(90), localUnitsPerMeter, options));
        }

        public static GeoCalibrationRequest Upright(IReadOnlyList<GeoControlPoint> controls, GeoVector3d localUp)
        {
            var options = GeoCalibrationOptions.CreateDefault();
            options.MaximumFitRmsMeters = 0.5;
            // Example acceptance threshold; choose it from measurement quality and application requirements.
            return GeoCalibrationRequest.Upright(controls, new GeoUprightSettings(localUp, GeoScalePolicy.Estimate, options));
        }

        public static GeoCalibrationRequest Spatial(IReadOnlyList<GeoControlPoint> controls)
        {
            var options = GeoCalibrationOptions.CreateDefault();
            return GeoCalibrationRequest.Spatial(controls, new GeoSpatialSettings(GeoScalePolicy.Estimate, options));
        }

        public static bool SolveWithoutUnity(
            GeoCalibrationRequest request,
            Wgs84Coordinate coordinate,
            out GeoVector3d localPosition,
            out GeoError error)
        {
            localPosition = default;
            if (!GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out var solution, out _, out error))
            {
                return false;
            }

            return solution.Frame.TryWgs84ToLocal(coordinate, out localPosition, out error);
        }
    }
}
```

Construct control points with `new GeoControlPoint(id, rootLocalPosition, coordinate, weight, role)`. Use `GeoScalePolicy.Fixed(localUnitsPerMeter)` for fixed scale and `options.ReferenceOrigin` for an explicit origin.

Settings and requests copy options and point collections. Editing the original list after request construction does not change that request; build a new one.

### 6.4 Numeric conversion without scene bindings

These APIs take values and return values. They do not require Anchor, Orientation, a target Transform or writes to the scene. Three layers are available:

| Need | Entry point |
| --- | --- |
| Convert WGS84 / GCJ-02 / BD-09 and height references | GeoCoordinateConverter.TryConvert; no calibration needed |
| Convert geographic coordinates/attitudes into model-local values | GeoLocalMapping, constructed from solution.Frame; no GameObject needed |
| Use an active scene calibration | Reference.TryCaptureLocalMapping or the Reference numeric methods |
| Convert world values using a fixed Root transform | Reference.TryCaptureSnapshot and its numeric methods |

Local always means **Reference.Root-local**, not an arbitrary object's parent-local space. GeoLocalPose stores a double-precision position in model units and a Unity Quaternion value. GeoAttitude stores rotation relative to a fixed geographic basis: +X east, +Y up, +Z north. This is not the conventional ENU component order and is not a world Euler rotation.

- TryGeoToLocalPose / TryLocalPoseToGeo convert complete numeric poses in both directions. TryLocalToCoordinate returns a chosen horizontal/height reference.
- TryGetLocalPose accepts GeoPlacement so ENU offsets can be included. Reverse conversion reports the final position; it cannot recover an unknown source offset.
- TryGeoToWorldPose / TryWorldPoseToGeo provide world equivalents.
- TryLocalRotationToAttitude / TryWorldRotationToAttitude measure actual numeric rotations. They do not infer the original Heading/LookAt request or automatically undo model-axis corrections.
- GeoAttitude.FromDegrees uses heading → pitch-up → clockwise-roll, matching the existing rotation-offset convention. GeographicRotation is authoritative; decomposed PitchUpDegrees is in [-90,90], heading in [0,360), and roll in [-180,180).
- A vertical forward direction still has a valid Quaternion and pitch, but heading/roll are not uniquely separable. Check HasHeading and HasRoll; do not treat their unavailable values as zero measurements.
- GCJ/BD selection changes position representation, not the attitude basis. Magnetic north, arbitrary projected-grid north and per-point moving tangent frames are not provided.

LocalMapping is immutable and independent of Root's world transform. The captured mapping remains usable after Root becomes invalid/destroyed, Reference is recalibrated, or Clear is called. New Reference queries require an active calibration. World queries additionally require a valid current Root. The standalone mapper and captured snapshots use value-only Unity math types; Core coordinate conversion and frame solving remain UnityEngine-independent.

```csharp
using UnityEngine;
using SceneGeography;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoNumericExamples
    {
        public static bool ConvertCoordinate(
            GeoCoordinate input,
            GeoCoordinateReference outputReference,
            out GeoCoordinate output,
            out GeoError error)
        {
            // Coordinate-standard conversion needs no calibration or scene.
            return GeoCoordinateConverter.CreateDefault().TryConvert(input, outputReference, out output, out error);
        }

        public static bool CreateMapping(
            GeoCalibrationRequest request,
            out GeoLocalMapping mapping,
            out GeoError error)
        {
            mapping = null;
            var converter = GeoCoordinateConverter.CreateDefault();
            if (!GeoCalibrationEngine.TrySolve(request, converter, out var solution, out _, out error))
            {
                return false;
            }

            // No GeoReference, GameObject or Transform is required.
            mapping = new GeoLocalMapping(solution.Frame, converter);
            return true;
        }

        public static bool LocalPoseRoundTrip(
            GeoLocalMapping mapping,
            GeoCoordinate coordinate,
            GeoCoordinateReference outputReference,
            out GeoLocalPose localPose,
            out GeoGeographicPose geographicPose,
            out GeoError error)
        {
            geographicPose = default;
            var attitude = GeoAttitude.FromDegrees(90, 10, -5);
            return mapping.TryGeoToLocalPose(coordinate, attitude, out localPose, out error) &&
                mapping.TryLocalPoseToGeo(localPose, outputReference, out geographicPose, out error);
        }

        public static bool FromActiveReference(
            GeoReference reference,
            GeoCoordinate coordinate,
            out GeoLocalPose localPose,
            out GeoError error)
        {
            // Uses the active calibration, but never reads or writes a target Transform.
            return reference.TryGeoToLocalPose(coordinate, GeoAttitude.FromDegrees(90), out localPose, out error);
        }

        public static bool MeasureWorldRotation(
            GeoReference reference,
            Quaternion actualRotation,
            Quaternion modelCorrection,
            out GeoAttitude attitude,
            out GeoError error)
        {
            attitude = default;
            if (!GeoAttitude.TryFromRotation(modelCorrection, out _, out error))
            {
                return false;
            }

            var geographicWorldRotation = actualRotation * Quaternion.Inverse(modelCorrection);
            return reference.TryWorldRotationToAttitude(geographicWorldRotation, out attitude, out error);
        }
    }
}
```

## 7. Editor API

Put Editor code in an Editor-only assembly referencing `SceneGeography.Editor` and the runtime assemblies, or in the project's predefined Editor assembly.

- Validate a Profile without applying: `TryBuildRequest`, then `GeoCalibrationEngine.TrySolve`.
- Calculate and apply separately: `Reference.TryPrepareCalibration`, inspect the report, then `GeoCalibrationEditorUtility.TryApplyCalibration`.
- Apply a preview in one step: `GeoCalibrationEditorUtility.TryInitializeFromProfile`.

These tools record binding-transform Undo, preview enabled positions/orientations/Reporters in that order, and mark affected scenes dirty. They do not save scenes or modify Profile assets.

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SceneGeography;
using SceneGeography.Editor;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoEditorExamples
    {
        public static bool Preview(
            GeoReference reference,
            GeoCalibrationProfile profile,
            out GeoCalibrationReport report,
            out GeoError error)
        {
            return GeoCalibrationEditorUtility.TryInitializeFromProfile(reference, profile, out report, out error);
        }

        public static bool Capture(
            IReadOnlyList<GeoControlPointMarker> markers,
            GeoCalibrationMode mode,
            out GeoCalibrationProfile profile,
            out GeoError error)
        {
            profile = null;
            error = default;
            if (markers == null || markers.Count == 0 || markers[0] == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Markers required.");
                return false;
            }

            var reference = markers[0].reference;
            var points = new List<GeoControlPointSettings>();
            foreach (var marker in markers)
            {
                if (marker == null || marker.reference != reference)
                {
                    error = new GeoError(GeoErrorCode.InvalidConfiguration, "Use one Reference.");
                    return false;
                }

                if (!marker.TryCapture(out var point, out error))
                {
                    return false;
                }

                points.Add(GeoControlPointSettings.From(point));
            }

            profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            profile.localFrameId = reference.LocalFrameId;
            profile.mode = mode;
            profile.points = points.ToArray();
            // Capture numbers only; configure Up, direction, scale and limits, then solve and review.
            return true;
        }

        public static string SaveNewProfile(GeoCalibrationProfile profile, string assetPath)
        {
            // For example Assets/Calibration/BlockA.asset; the parent folder must already exist.
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(profile, uniquePath);
            AssetDatabase.SaveAssets();
            return uniquePath;
        }
    }
}
```

`Capture` returns an unsaved asset; `SaveNewProfile` saves it. The destination's parent folder must already exist. Review mode, directions, scale and acceptance limits after capture.

A true result means calibration was committed, not that every binding succeeded. A POI can still fail because of invalid input, a parent transform or a missing look target; inspect its own `LastError`.

## 8. Coordinate types, ranges and accuracy

### 8.1 Conversion pipeline

Input is converted to WGS84 latitude/longitude and ellipsoid height, then to Earth-centered Earth-fixed (ECEF) coordinates, then to East/North/Up (ENU) around a fixed origin. Calibration maps ENU into Root-local and finally Unity world space.

Internally, vectors use `(East, Up, North)` to match Unity's usual axis layout. `GeoEnuOffset` still takes East, North, Up. East/north/up axes remain fixed at the reference origin rather than recomputing true north and zenith for every POI. ECEF differences retain Earth curvature; this is not simply latitude/longitude differences multiplied by constants.

### 8.2 GCJ-02 / BD-09 support

Forward conversion means WGS84 → GCJ-02 → BD-09. The formulas are public approximations adapted from MIT-licensed `wandergis/coordtransform`, not an official complete encryption algorithm or a surveying-accuracy guarantee. See [third-party license](ThirdParty/coordtransform-LICENSE.txt).

GCJ-02 offsets vary by location. BD-09 adds another transformation. Inverse conversion iteratively finds an input producing the requested output; the 0.01 m convergence tolerance is a numerical stopping condition, not an accuracy claim.

GCJ/BD conversion is supported only strictly inside:

- `73.66 < longitude < 135.05` degrees east.
- `3.86 < latitude < 53.55` degrees north.

This spans about 61.4° longitude and 49.7° latitude: roughly 5,500 km north–south and 5,900 km east–west at 30°N. It covers most of China's land area and surrounding regions, but **is not an administrative boundary and cannot determine which coordinate type a dataset uses**.

The rectangle limits this converter, not WGS84 or ENU in general:

| Input/output | Outside this rectangle |
| --- | --- |
| Explicit WGS84 | Local-scene calibration remains available |
| Explicit GCJ-02 / BD-09 | Returns UnsupportedCoordinateRegion; both input and output conversion check the range |
| Overseas or boundary data | Confirm the actual reference with the provider, or convert reliably to WGS84 upstream; do not just relabel it |

A small scene in Beijing or Shanghai is well inside the region. Near the boundary, check both input and converted coordinates when importing data.

### 8.3 EPSG, WKT, PRJ and UTM

| Term | Meaning | Example |
| --- | --- | --- |
| EPSG | Registry identifiers for coordinate reference systems and related definitions | EPSG:4326 identifies the common 2D WGS84 geographic CRS |
| WKT | Well-Known Text; here, a text description of a coordinate reference system | Datum, axes, units and projection parameters |
| PRJ | Common filename extension for a CRS definition, usually WKT | A .prj file accompanying a Shapefile |
| UTM | Universal Transverse Mercator zoned projection system | Easting/Northing in meters, with zone and hemisphere |

These are not four interchangeable GPS formats. The package does not parse arbitrary CRS definitions or accept UTM meter coordinates directly. Convert such data upstream into explicit WGS84 geographic coordinates with a known height reference. EPSG:4326 alone does not specify the required height convention.

### 8.4 Operating limits

- One Reference serves one local scene, not global roaming. Origins do not shift automatically; reference latitudes beyond ±89.9° are rejected.
- No geoid/height model is supplied, so orthometric elevation cannot currently be converted directly into ellipsoid height.
- Double-precision math cannot recover precision already lost in Unity float Transforms.
- The system does not snap to surfaces, extract Gaussian geometry, perform collision checks or navigate. POI height comes from input or application correction.
- Moving Root updates geographic mapping, not a renderer's independent world-space caches. Keep rendering Root stable unless the renderer explicitly supports synchronized changes.
- Binding updates order component categories, not arbitrary parenting or mutually tracking dependencies.

## 9. Samples, tests and troubleshooting

### 9.1 Sample controls

- WASD: move; Q/E: descend/ascend; right mouse: rotate; Shift: faster movement.
- **Visit POI** moves near the POI and looks at it rather than placing the camera at its center.
- **Apply alternate calibration** commits another calibration. **Try invalid calibration** demonstrates preserving the old solution on failure.
- **Rotate Root / Toggle Root scale** demonstrates mapping updates. **Invalid nonuniform Root / restore** demonstrates invalid-transform rejection and recovery.

Sample POI visuals have an additional 16-real-meter upward offset; yellow bars indicate direction. They demonstrate behavior rather than provide a production POI manager.

| Component | Field | Meaning |
| --- | --- | --- |
| GeoReferenceDemo | Reference, Scene Root, Profile, Viewer | Geographic manager, synthetic-model root, calibration and free camera |
| GeoReferenceDemo | Create Proxy Geometry | Default true; creates ground/buildings. Turning it off still creates synthetic POIs; it does not adopt application POIs |
| GeoReferenceDemo | Show Controls | Default true; shows the IMGUI demo panel |
| GeoDemoCamera | Reference | Supplies Up and units per meter; no movement before initialization |
| GeoDemoCamera | Speed Meters Per Second | Default 15 real m/s, without collision or gravity |
| GeoDemoCamera | Mouse Degrees Per Pixel | Default 0.15, right-mouse rotation sensitivity |

### 9.2 Assembly layout and tests

| Location | Assembly / purpose |
| --- | --- |
| Runtime/Core | SceneGeography.Core: double-precision mathematics, no UnityEngine reference |
| Runtime/Unity | SceneGeography.Unity: scene mapping and components; depends on Core |
| Editor | SceneGeography.Editor: authoring and preview; depends on runtime only |
| Tests/Shared | SceneGeography.Tests.Shared: independent synthetic fixtures for package tests |
| Tests/EditMode, Tests/PlayMode | Package tests; no sample dependency |
| Samples~/BasicSample | Optional demo, its Editor assembly and its own EditMode/PlayMode tests |

Install Unity Test Framework (`com.unity.test-framework`) to run tests. Embedded-package tests are available to Test Runner. For an installed non-embedded package, add `"io.github.ruyeruye.scene-geography"` to the project's top-level `testables` array in `Packages/manifest.json` (merge it with existing entries; this is not a dependency entry).

Run `SceneGeography.Tests.EditMode` and `SceneGeography.Tests.PlayMode` in **Window > General > Test Runner**. These tests do not require Basic Sample or Input System. After importing Basic Sample and installing Input System, its extra tests are available as `SceneGeography.Samples.Tests.EditMode` and `SceneGeography.Samples.Tests.PlayMode`.

With Unity CLI connected to this project:

~~~powershell
unity command test run --project-path . --format json -- --mode editor --filter SceneGeography.Tests.EditMode --filter_type assembly
unity command test run --project-path . --format json -- --mode playmode --filter SceneGeography.Tests.PlayMode --filter_type assembly --async_tests true
unity command test status --project-path . --format json
~~~

Read the final status after an asynchronous run. Do not start tests while manually playing or with unsaved scene edits.

The four C# examples above also live in [Tests/EditMode/Documentation](Tests/EditMode/Documentation) and compile with the tests. Automated checks compare code blocks in both language editions against those sources. README tests locate the package through UPM's resolved path, so they do not depend on its on-disk folder name.

### 9.3 Troubleshooting

| Symptom/error | Check first |
| --- | --- |
| CoordinateTypeUnspecified / HeightReferenceUnspecified | Specify horizontal type and height reference; default external coordinates are invalid |
| LocalFrameIdMismatch | Profile and Reference identify different local coordinate spaces |
| MissingHeightModel | Orthometric needs an unavailable height model; a model ID alone cannot fix it |
| UnsupportedCoordinateRegion | GCJ/BD input or output is outside the supported rectangle |
| DegenerateBaseline / NearCollinearPoints | Duplicate points, a short horizontal baseline or near-collinear Spatial controls |
| InvalidRootTransform / InvalidTargetParentTransform | Nonuniform scale, reflection, shear or zero scale anywhere in the relevant ancestor chain |
| RootDestroyed | Clear, then explicitly choose a new Root or world mode |
| ResidualLimitExceeded | Coordinate source, height convention, model positions, Up or thresholds; inspect per-point residuals |
| StaleCalibrationCandidate | Recalculate an outdated candidate, version or space identity |
| MissingLookTarget / DegenerateLookDirection | Missing/destroyed target, coincident positions or direction nearly parallel to Up |
| OutOfConfiguredRange | Final 3D ENU position exceeds the range, including offsets |
| POIs snap back / camera jumps back after teleport | Competing transform owners; remove continuous Anchor from the player and synchronize controller state on teleport |
| Editor preview works, Play does not | Solutions are not serialized; initialize at runtime after loading prerequisites |
| Sample reports missing Unity.InputSystem | Install Input System before importing/compiling Basic Sample |
| Embedded-package folder-name warning | Name its folder after package ID; this is separate from C# compilation errors |

## 10. License

Scene Geography is provided under the [MIT License](LICENSE.md). See the [changelog](CHANGELOG.md) for version notes. Adapted coordinate-conversion formulas retain their separate [coordtransform MIT notice](ThirdParty/coordtransform-LICENSE.txt).
