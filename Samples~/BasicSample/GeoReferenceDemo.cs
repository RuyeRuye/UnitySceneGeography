using System;
using System.Collections.Generic;
using UnityEngine;

namespace SceneGeography.Samples
{
    /// <summary>合成验证场景的初始化与交互控制；会生成合成 POI，不应用于真实业务数据管理。</summary>
    public sealed class GeoReferenceDemo : MonoBehaviour
    {
        /// <summary>测试用地理 Reference；不是生产 POI 管理器。</summary>
        [Tooltip("Geographic Reference for this sample; not a production POI manager.")]
        public GeoReference reference;

        /// <summary>合成模型与 POI 根节点，需满足正向统一缩放限制。</summary>
        [Tooltip("Root of the synthetic model and POIs; requires positive uniform scale.")]
        public Transform sceneRoot;

        /// <summary>合成验证配置，不可用于真实点云的测量标定。</summary>
        [Tooltip("Synthetic validation Profile; not surveyed calibration data for a real point cloud.")]
        public GeoCalibrationProfile profile;

        /// <summary>用于 POI 访问跳转的测试自由相机。</summary>
        [Tooltip("Sample free camera used to visit POIs.")]
        public GeoDemoCamera viewer;

        /// <summary>是否生成代理地面与建筑；关闭后仍生成合成 POI。</summary>
        [Tooltip("Generate proxy ground and buildings; synthetic POIs are still created when disabled.")]
        public bool createProxyGeometry = true;

        /// <summary>是否显示合成场景验证面板。</summary>
        [Tooltip("Show the synthetic-scene validation controls.")]
        public bool showControls = true;
        public bool Initialized { get; private set; }
        public string Status { get; private set; }

        public readonly List<Transform> PoiTransforms = new List<Transform>();
        readonly List<GeoCoordinate> poiCoordinates = new List<GeoCoordinate>();
        readonly List<Material> materials = new List<Material>();
        Shader sampleShader;
        Vector2 scroll;
        bool shifted;
        Vector3 normalScale;
        void Start()
        {
            if (reference == null || sceneRoot == null || profile == null)
            {
                Status = "Assign Reference, Root and Profile.";
                return;
            }

            // A Resources asset retains this sample-only shader in Player builds.
            // No render-pipeline package or Shader.Find fallback is required.
            sampleShader = Resources.Load<Shader>("SceneGeographySample/Unlit");
            if (sampleShader == null || !sampleShader.isSupported)
            {
                Status = "Sample shader is missing or unsupported. Import the complete Basic Sample (Built-in or URP).";
                return;
            }

            normalScale = sceneRoot.localScale;
            if (!reference.IsInitialized)
            {
                if (!reference.TrySetRoot(sceneRoot, out var error) ||
                    !reference.TrySetLocalFrameId(profile.localFrameId, out error))
                {
                    Status = error.ToString();
                    return;
                }

                if (!profile.TryBuildRequest(out var request, out error) ||
                    !reference.TryInitialize(request, out _, out error))
                {
                    Status = error.ToString();
                    return;
                }
            }

            if (createProxyGeometry)
            {
                CreateGeometry();
            }

            CreatePois();
            Initialized = true;
            Status = "Ready. Synthetic controls, mixed WGS84/GCJ02/BD09 input.";
            if (viewer != null && reference.TryCaptureSnapshot(out var s, out _) &&
                s.TryGetWorldPose(
                new GeoPlacement(GeoDemoData.GeoAt(new GeoVector3d(0, 0, -140)), new GeoEnuOffset(0, 0, 65)),
                GeoOrientationRequest.ForGeoTarget(GeoDemoData.Origin.ToCoordinate(), GeoLookMode.Full3D),
                out var pose,
                out _))
            {
                viewer.Teleport(pose);
            }
        }

        Material Material(Color color)
        {
            var material = new Material(sampleShader);
            material.color = color;
            materials.Add(material);
            return material;
        }

        GameObject Shape(string name, PrimitiveType primitive, Vector3 local, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            go.transform.SetParent(sceneRoot, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Material(color);
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            return go;
        }

        void CreateGeometry()
        {
            var mode = profile.mode;
            var rotation = GeoDemoData.ModelRotation(mode);
            var ground = Shape(
                "Synthetic block ground",
                PrimitiveType.Cube,
                GeoDemoData.ModelPosition(new GeoVector3d(0, -1, 0), mode),
                new Vector3(220, 1, 200) * 1.25f,
                new Color(.12f, .17f, .21f));
            ground.transform.localRotation = rotation;
            for (int i = 0; i < 5; i++)
            {
                var q = GeoDemoData.EnuPoints[i];
                var box = Shape(
                    "Proxy building " + (i + 1),
                    PrimitiveType.Cube,
                    GeoDemoData.ModelPosition(q + new GeoVector3d(0, 5, 0), mode),
                    new Vector3(12, 10, 12) * 1.25f,
                    new Color(.25f + .05f * i, .38f, .45f));
                box.transform.localRotation = rotation;
            }
        }

        void CreatePois()
        {
            for (int i = 0; i < GeoDemoData.EnuPoints.Length; i++)
            {
                var coordinate = GeoDemoData.GeoAt(GeoDemoData.EnuPoints[i], (GeoCoordinateType)(1 + i % 3));
                poiCoordinates.Add(coordinate);
                var go = Shape(
                    "POI " + (i + 1),
                    PrimitiveType.Sphere,
                    Vector3.zero,
                    Vector3.one * 2,
                    new Color(.9f, .45f + .07f * i, .1f));
                var anchor = go.AddComponent<GeoAnchor>();
                anchor.Configure(reference, new GeoPlacement(coordinate, new GeoEnuOffset(0, 0, 16)));
                var arrow = Shape(
                    "Heading indicator " + (i + 1),
                    PrimitiveType.Cube,
                    Vector3.zero,
                    new Vector3(.6f, .6f, 5),
                    Color.yellow);
                arrow.transform.SetParent(go.transform, false);
                arrow.transform.localPosition = Vector3.forward * 2;
                var orientation = go.AddComponent<GeoOrientation>();
                orientation.Configure(
                    reference,
                    GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(i * 60)),
                    Quaternion.identity);
                PoiTransforms.Add(go.transform);
            }

            reference.SynchronizeBindings();
        }

        /// <summary>提交有效或故意错误的样例标定；失败不改变已生效解与交替偏移状态。</summary>
        public bool Reinitialize(bool invalid)
        {
            var copy = Instantiate(profile);
            try
            {
                bool nextShifted = !shifted;
                foreach (var p in copy.points)
                {
                    if (invalid)
                    {
                        p.coordinate = GeoCoordinateSettings.From(GeoDemoData.Origin.ToCoordinate());
                    }
                    else
                    {
                        p.localPosition.x += nextShifted ? 8 : 0;
                    }
                }

                if (!copy.TryBuildRequest(out var request, out var error) ||
                    !reference.TryInitialize(request, out _, out error))
                {
                    Status = "Rejected; previous calibration kept: " + error;
                    return false;
                }

                shifted = nextShifted;
                Status = "Calibration applied. Version " + reference.CalibrationVersion;
                return true;
            }
            finally
            {
                Destroy(copy);
            }
        }

        /// <summary>跳到合成 POI 的访问位置并朝向目标，不是将相机直接放到标记中心。</summary>
        public void VisitPoi(int index)
        {
            if (index < 0 || index >= poiCoordinates.Count || viewer == null)
            {
                return;
            }

            var placement = new GeoPlacement(poiCoordinates[index], new GeoEnuOffset(0, -22, 22));
            var look = GeoOrientationRequest.ForGeoTarget(poiCoordinates[index], GeoLookMode.Full3D);
            if (reference.TryGetWorldPose(placement, look, out var pose, out var error))
            {
                viewer.Teleport(pose);
                Status = "Visited POI " + (index + 1);
            }
            else
            {
                Status = error.ToString();
            }
        }

        void OnGUI()
        {
            if (!showControls)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(12, 12, 380, Mathf.Max(100, Screen.height - 24)), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("GeoReference / " + (profile == null ? "No profile" : profile.mode.ToString()));
            GUILayout.Label("SYNTHETIC VALIDATION SCENE - not surveyed data");
            GUILayout.Label("WASD move / Q E vertical / RMB look / Shift fast");
            GUILayout.Label(Status ?? "Waiting for Start");
            if (reference != null)
            {
                GUILayout.Label("Calibration version: " + reference.CalibrationVersion);
                var report = reference.CurrentReport;
                if (report != null && report.FitMetrics.HasValue)
                {
                    GUILayout.Label("Local units/m: " + report.LocalUnitsPerMeter.Value.ToString("F6"));
                    GUILayout.Label("Fit RMS m: " + report.FitMetrics.Value.SpatialRmsMeters.ToString("F6"));
                    if (report.CheckMetrics.HasValue)
                    {
                        GUILayout.Label("Check max m: " + report.CheckMetrics.Value.MaximumResidualMeters.ToString("F6"));
                    }
                }

                if (viewer != null && reference.TryWorldToWgs84(viewer.transform.position, out var gps, out var error))
                {
                    GUILayout.Label($"Viewer WGS84: {gps.LatitudeDegrees:F7}, {gps.LongitudeDegrees:F7}, h={gps.EllipsoidHeightMeters:F2}");
                }
                else if (!reference.IsWorldMappingValid)
                {
                    GUILayout.Label(reference.LastWorldMappingError.ToString());
                }
            }

            if (Initialized)
            {
                if (GUILayout.Button("Apply alternate calibration (+8 local X)"))
                {
                    Reinitialize(false);
                }

                if (GUILayout.Button("Try invalid calibration (must retain old)"))
                {
                    Reinitialize(true);
                }

                if (GUILayout.Button("Rotate Root 20 degrees"))
                {
                    sceneRoot.Rotate(Vector3.up, 20, Space.Self);
                    reference.SynchronizeBindings();
                }

                if (GUILayout.Button("Toggle Root scale x1 / x2"))
                {
                    sceneRoot.localScale = (sceneRoot.localScale - normalScale).sqrMagnitude < .001f ? normalScale * 2 : normalScale;
                    reference.SynchronizeBindings();
                }

                if (GUILayout.Button("Invalid nonuniform Root / restore"))
                {
                    sceneRoot.localScale = Mathf.Abs(sceneRoot.localScale.x - sceneRoot.localScale.y) < .001f
                        ? Vector3.Scale(normalScale, new Vector3(1, 2, 1))
                        : normalScale;
                    reference.SynchronizeBindings();
                }

                for (int i = 0; i < poiCoordinates.Count; i++)
                {
                    if (GUILayout.Button("Visit POI " + (i + 1) + " (" + poiCoordinates[i].Reference.HorizontalType + ")"))
                    {
                        VisitPoi(i);
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void OnDestroy()
        {
            foreach (var material in materials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }
        }
    }
}
