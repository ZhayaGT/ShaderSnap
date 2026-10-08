using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ShaderSnap.Core;

namespace ShaderSnap.Editor
{
    /// <summary>
    /// Window shell and control rail. Owns the serialised state and the runtime preset instance,
    /// builds the UIElements tree, and holds the control factories every section reuses. The
    /// viewport gestures live in <c>SnippetExporterWindow.Canvas</c>, the asset and export flow in
    /// <c>SnippetExporterWindow.Assets</c>.
    /// </summary>
    public partial class SnippetExporterWindow : EditorWindow
    {
        const string UssRelativePath = "Editor/Styles/ShaderSnap.uss";

        // preset is runtime state, never an asset reference. Serialising it meant Unity destroyed the
        // instance on every domain reload and restored the field as null, while the UIElements tree
        // survived; CreateGUI then had nothing to rebuild and the canvas painted nothing at all.
        SnippetExportPreset preset;
        [SerializeField] SnippetExportPreset presetAsset;
        [SerializeField] string currentAssetPath;
        [SerializeField] float zoom = 1f;
        long lastAssetStamp;

        /// <summary>Always usable preset; created on first access and never serialised.</summary>
        SnippetExportPreset Preset
        {
            get
            {
                if (preset != null) return preset;
                preset = CreateInstance<SnippetExportPreset>();
                // HideAndDontSave includes DontUnloadUnusedAsset, which would keep this instance and its
                // inspector state alive after the domain reload that drops the field pointing at it. Hide
                // it, but let the unused-asset sweep reclaim it.
                preset.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;
                return preset;
            }
        }

        SnippetCanvasRenderer canvas;
        VisualElement canvasHost;
        ScrollView canvasArea;
        VisualElement controlBody;
        VisualElement canvasEmptyState;
        Label statusLabel;
        Label zoomLabel;
        Button exportButton;
        ObjectField presetField;

        bool panning;
        Vector2 panOrigin;
        Vector2 panScrollOrigin;

        GraphModel model;
        string shaderName = "";

        [MenuItem("Window/ShaderSnap")]
        public static void Open()
        {
            SnippetExporterWindow window = GetWindow<SnippetExporterWindow>();
            window.titleContent = new GUIContent("ShaderSnap");
            window.minSize = new Vector2(960f, 600f);
        }

        void OnEnable()
        {
            titleContent = new GUIContent("ShaderSnap");
            ShaderGraphAssetWatcher.Register(this);
        }

        void OnDisable()
        {
            ShaderGraphAssetWatcher.Unregister(this);
        }

        public void CreateGUI()
        {
            // Resolved through the package registry, not a literal path: the stylesheet has to load
            // whether the package sits in Assets/, in Packages/, or in the registry cache.
            string ussPath = ShaderSnapPaths.Asset(UssRelativePath);
            StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            if (sheet == null)
                Debug.LogError($"[ShaderSnap] Could not load the editor stylesheet at '{ussPath}'. " +
                               "The window will render unstyled.");

            rootVisualElement.Clear();
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
            rootVisualElement.style.flexDirection = FlexDirection.Row;

            // The rail scrolls so every control stays reachable when the window is short; a fixed-width
            // column that simply overflows would hide the export button with no way to reach it.
            var controlPanel = new ScrollView(ScrollViewMode.Vertical) { name = "control-panel" };
            controlPanel.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            controlPanel.verticalScrollerVisibility = ScrollerVisibility.Auto;
            controlBody = new VisualElement { name = "control-body" };
            controlPanel.Add(controlBody);
            rootVisualElement.Add(controlPanel);

            // The empty state has to overlay the viewport, so it needs a positioned ancestor that is not
            // the ScrollView's content container (which collapses to zero when there is nothing to show).
            var canvasContainer = new VisualElement { name = "canvas-container" };

            canvasArea = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { name = "canvas-area" };
            canvasArea.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            canvasArea.verticalScrollerVisibility = ScrollerVisibility.Auto;
            canvasHost = new VisualElement { name = "canvas-host" };
            canvas = new SnippetCanvasRenderer();
            canvasHost.Add(canvas);
            canvasArea.Add(canvasHost);
            canvasContainer.Add(canvasArea);

            canvasEmptyState = BuildEmptyState();
            canvasContainer.Add(canvasEmptyState);

            canvasArea.RegisterCallback<WheelEvent>(OnCanvasWheel, TrickleDown.TrickleDown);
            canvasArea.RegisterCallback<PointerDownEvent>(OnCanvasPointerDown);
            canvasArea.RegisterCallback<PointerMoveEvent>(OnCanvasPointerMove);
            canvasArea.RegisterCallback<PointerUpEvent>(OnCanvasPointerUp);
            canvasArea.RegisterCallback<PointerCaptureOutEvent>(_ => EndPan());
            rootVisualElement.Add(canvasContainer);

            BuildControls();
            ApplyZoom();

            if (!string.IsNullOrEmpty(currentAssetPath)) LoadCurrentAsset();
            else UpdateStatus();
        }

        /// <summary>Teaches the two gestures instead of leaving the user with a blank void.</summary>
        VisualElement BuildEmptyState()
        {
            var root = new VisualElement { name = "canvas-empty" };
            root.pickingMode = PickingMode.Ignore;
            root.Add(new Label("Belum ada graph") { name = "empty-title" });
            root.Add(new Label("Select a .shadergraph asset on the left to preview it.") { name = "empty-body" });
            root.Add(new Label("Gulir untuk zoom  ·  Seret untuk menggeser") { name = "empty-hint" });
            return root;
        }

        void RebuildControls()
        {
            controlBody.Clear();
            BuildControls();
        }

        void BuildControls()
        {
            AddSection("Source");

            var assetField = new ObjectField("Shader Graph Asset")
            {
                objectType = typeof(Shader),
                value = string.IsNullOrEmpty(currentAssetPath) ? null : AssetDatabase.LoadAssetAtPath<Shader>(currentAssetPath)
            };
            assetField.RegisterValueChangedCallback(evt => OnAssetSelected(evt.newValue));
            controlBody.Add(assetField);

            presetField = new ObjectField("Preset") { objectType = typeof(SnippetExportPreset), value = presetAsset };
            presetField.RegisterValueChangedCallback(evt => LoadPreset(evt.newValue as SnippetExportPreset));
            controlBody.Add(presetField);

            AddSection("View");
            controlBody.Add(BuildZoomRow());
            AddToggle("Light Preview", Preset.lightPreview, value => Preset.lightPreview = value);

            AddSection("Cables");
            AddEnumField("Style", Preset.wireStyle, value => Preset.wireStyle = (SnippetExportPreset.WireStyle)value);
            AddEnumField("Color Mode", Preset.wireColorMode, value => Preset.wireColorMode = (SnippetExportPreset.WireColorMode)value);
            AddSlider("Width", Preset.wireWidth, 1f, 6f, value => Preset.wireWidth = value);
            AddSlider("Corner Radius", Preset.wireCornerRadius, 0f, 24f, value => Preset.wireCornerRadius = value);
            AddColorField("Single Color", Preset.singleWireColor, value => Preset.singleWireColor = value);
            AddToggle("Arrow Head", Preset.arrowHead, value => Preset.arrowHead = value);

            AddSection("Node Style");
            AddSlider("Text Scale", Preset.textScale, LayoutMetrics.MinFontScale, LayoutMetrics.MaxFontScale,
                value => Preset.textScale = value);
            AddSlider("Column Balance", Preset.layoutBalance, 0f, 1f, value => Preset.layoutBalance = value);
            AddSlider("Node Locality", Preset.nodeLocality, 0f, 1f, value => Preset.nodeLocality = value);
            AddSlider("Vertical Spread", Preset.verticalSpread, LayoutMetrics.MinVerticalSpread,
                LayoutMetrics.MaxVerticalSpread, value => Preset.verticalSpread = value);
            AddToggle("Auto Aspect", Preset.autoAspect, value => Preset.autoAspect = value);
            AddSlider("Target Aspect", Preset.targetAspect, 0.5f, 3f, value => Preset.targetAspect = value);
            AddToggle("Show Node Values", Preset.showNodeValues, value => Preset.showNodeValues = value);

            AddSection("Readability");
            AddToggle("Group Frames", Preset.showGroups, value => Preset.showGroups = value);
            AddToggle("Sticky Notes", Preset.showNotes, value => Preset.showNotes = value);
            AddToggle("Highlight Critical Path", Preset.highlightCriticalPath,
                value => Preset.highlightCriticalPath = value);
            AddToggle("Column Guides", Preset.showColumnGuides, value => Preset.showColumnGuides = value);
            AddToggle("Port Legend", Preset.showPortLegend, value => Preset.showPortLegend = value);

            AddSection("Background");
            AddEnumField("Mode", Preset.backgroundMode, value => Preset.backgroundMode = (SnippetExportPreset.BackgroundMode)value);
            AddColorField("Solid Color", Preset.backgroundColor, value => Preset.backgroundColor = value);
            AddColorField("Gradient Top", Preset.backgroundGradientTop, value => Preset.backgroundGradientTop = value);
            AddColorField("Gradient Bottom", Preset.backgroundGradientBottom, value => Preset.backgroundGradientBottom = value);
            AddColorField("Blur Tint", Preset.blurTint, value => Preset.blurTint = value);

            AddSection("Frame & Watermark");
            AddToggle("macOS Window Frame", Preset.showMacOsFrame, value => Preset.showMacOsFrame = value);
            AddToggle("Drop Shadow", Preset.showDropShadow, value => Preset.showDropShadow = value);
            AddToggle("Watermark", Preset.showWatermark, value => Preset.showWatermark = value);
            AddColorField("Frame Color", Preset.frameColor, value => Preset.frameColor = value);
            AddSlider("Corner Radius", Preset.cornerRadius, 0f, 32f, value => Preset.cornerRadius = value);
            AddSlider("Frame Margin", Preset.frameMargin, 0f, 64f, value => Preset.frameMargin = value);
            AddSlider("Shadow Offset", Preset.shadowOffset, 0f, 16f, value => Preset.shadowOffset = value);
            AddTextField("Author", Preset.authorName, value => Preset.authorName = value);

            var logoField = new ObjectField("Watermark Logo") { objectType = typeof(Texture2D), value = Preset.watermarkLogo };
            logoField.RegisterValueChangedCallback(evt => { Preset.watermarkLogo = evt.newValue as Texture2D; MarkDirty(); });
            controlBody.Add(logoField);

            AddSection("Export");
            AddIntField("Resolution Multiplier", Preset.resolutionMultiplier, value =>
                Preset.resolutionMultiplier = Mathf.Clamp(value, 1, 4));
            AddIntField("Node Warning Threshold", Preset.nodeCountWarningThreshold,
                value => Preset.nodeCountWarningThreshold = Mathf.Max(1, value));

            var toolbar = new VisualElement { name = "toolbar" };
            toolbar.Add(new Button(OnRefresh) { text = "Refresh" });
            toolbar.Add(new Button(OnFitToView) { text = "Fit To View" });
            toolbar.Add(new Button(SavePreset) { text = "Save Preset" });
            controlBody.Add(toolbar);

            exportButton = new Button(OnExport) { text = "Export PNG", name = "export-button" };
            exportButton.SetEnabled(model != null && model.nodes.Count > 0);
            controlBody.Add(exportButton);

            statusLabel = new Label("Select a .shadergraph asset to begin.") { name = "status-label" };
            controlBody.Add(statusLabel);
        }

        void AddSection(string title)
        {
            var header = new VisualElement { name = "section" };
            header.Add(new Label(title) { name = "section-label" });
            controlBody.Add(header);
        }

        /// <summary>
        /// Compact zoom control. The slider it replaces was the only way to zoom and gave no feedback
        /// about where you were, so this shows the live percentage and the two gestures' shortcuts.
        /// </summary>
        VisualElement BuildZoomRow()
        {
            var row = new VisualElement { name = "zoom-row" };
            row.Add(new Button(() => StepZoom(1f / 1.25f)) { text = "−", name = "zoom-step" });
            zoomLabel = new Label("100%") { name = "zoom-value" };
            row.Add(zoomLabel);
            row.Add(new Button(() => StepZoom(1.25f)) { text = "+", name = "zoom-step" });
            row.Add(new Button(() => SetZoom(1f)) { text = "1:1", name = "zoom-fit" });
            row.Add(new Button(OnFitToView) { text = "Fit", name = "zoom-fit" });
            return row;
        }

        void AddEnumField(string label, Enum value, Action<Enum> onChanged)
        {
            var field = new EnumField(label, value);
            field.RegisterValueChangedCallback(evt => { onChanged(evt.newValue); MarkDirty(); });
            controlBody.Add(field);
        }

        void AddColorField(string label, Color value, Action<Color> onChanged)
        {
            var field = new ColorField(label) { value = value };
            field.RegisterValueChangedCallback(evt => { onChanged(evt.newValue); MarkDirty(); });
            controlBody.Add(field);
        }

        void AddSlider(string label, float value, float min, float max, Action<float> onChanged)
        {
            var field = new Slider(label, min, max) { value = value };
            field.RegisterValueChangedCallback(evt => { onChanged(evt.newValue); MarkDirty(); });
            controlBody.Add(field);
        }

        void AddToggle(string label, bool value, Action<bool> onChanged)
        {
            var field = new Toggle(label) { value = value };
            field.RegisterValueChangedCallback(evt => { onChanged(evt.newValue); MarkDirty(); });
            controlBody.Add(field);
        }

        void AddIntField(string label, int value, Action<int> onChanged)
        {
            var field = new IntegerField(label) { value = value };
            field.RegisterValueChangedCallback(evt => { onChanged(evt.newValue); MarkDirty(); });
            controlBody.Add(field);
        }

        void AddTextField(string label, string value, Action<string> onChanged)
        {
            var field = new TextField(label) { value = value };
            field.RegisterValueChangedCallback(evt => { onChanged(evt.newValue); MarkDirty(); });
            controlBody.Add(field);
        }

        void MarkDirty()
        {
            hasUnsavedChanges = true;
            if (presetAsset != null) EditorUtility.SetDirty(presetAsset);
            canvas.Refresh();
            ApplyZoom();
            UpdateStatus();
        }
    }
}
