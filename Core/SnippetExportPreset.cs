using UnityEngine;

namespace ShaderSnap.Core
{
    [CreateAssetMenu(fileName = "SnippetExportPreset", menuName = "ShaderSnap/Export Preset")]
    public class SnippetExportPreset : ScriptableObject
    {
        public enum WireColorMode { PerPortType, Single, Gradient }
        public enum BackgroundMode { SolidColor, Gradient, Transparent, BlurredEditor }

        /// <summary>
        /// How a routed wire is drawn. Every style except <see cref="Straight"/> follows the layout's
        /// node-avoiding waypoints, so only <see cref="Straight"/> can run across a node.
        /// </summary>
        public enum WireStyle
        {
            /// <summary>Axis-aligned runs with rounded corners. The tidiest, reads as a cable run.</summary>
            Orthogonal,

            /// <summary>Axis-aligned runs with corners cut at 45 degrees, like electrical conduit.</summary>
            Conduit,

            /// <summary>Smooth curve through the same waypoints. Softest, but bends are less readable.</summary>
            Bezier,

            /// <summary>One direct line from port to port, ignoring the route. Can cross nodes.</summary>
            Straight
        }

        public WireStyle wireStyle = WireStyle.Orthogonal;
        public WireColorMode wireColorMode = WireColorMode.PerPortType;
        public BackgroundMode backgroundMode = BackgroundMode.SolidColor;

        public Color backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);
        public Color backgroundGradientTop = new Color(0.18f, 0.14f, 0.28f, 1f);
        public Color backgroundGradientBottom = new Color(0.05f, 0.05f, 0.08f, 1f);
        public Color singleWireColor = new Color(0.90f, 0.90f, 0.90f, 1f);

        public float wireWidth = 2.5f;
        public float wireCornerRadius = 8f;
        public bool arrowHead = false;

        /// <summary>
        /// 0 keeps the classic longest-path ranking (every source pinned to the first column);
        /// 1 lets nodes drift toward the column that is currently lightest, which evens out the
        /// column heights at the cost of a wider canvas.
        /// </summary>
        public float layoutBalance = 1f;

        /// <summary>
        /// Among the columns a node may legally occupy, how strongly to prefer the one nearest the nodes
        /// that consume it. 0 spreads nodes into the lightest column; 1 hugs consumers, so a property
        /// node lands beside the node that reads it instead of at the far left of the canvas.
        /// </summary>
        public float nodeLocality = 1f;

        /// <summary>Draws a titled frame around each group authored in Shader Graph.</summary>
        public bool showGroups = true;

        /// <summary>Prints the authored sticky notes in a band under the graph.</summary>
        public bool showNotes = true;

        /// <summary>Emphasises the longest dependency chain and recedes everything off it.</summary>
        public bool highlightCriticalPath = false;

        /// <summary>Draws faint separators and index numbers between columns.</summary>
        public bool showColumnGuides = false;

        /// <summary>Draws a legend of the port colours the graph actually uses.</summary>
        public bool showPortLegend = false;

        /// <summary>
        /// Draws each node's stored value under its title, for example the vector a Vector 2 node holds.
        /// Off by default: it adds a row to every value-carrying node and widens the canvas.
        /// </summary>
        public bool showNodeValues = false;

        /// <summary>
        /// Multiplies the vertical gap between nodes in a column. Raising it stretches the canvas
        /// taller without touching its width, which is the only lever that moves the output away
        /// from a wide strip: the column count is fixed by the graph topology.
        /// </summary>
        public float verticalSpread = 1f;

        /// <summary>
        /// When enabled, <see cref="verticalSpread"/> is solved automatically so the canvas lands as
        /// close to <see cref="targetAspect"/> as the spread limit allows.
        ///
        /// Off by default because the stretch is pure empty space: it moves nodes apart without making
        /// anything larger. Measured on the fixtures at text scale 1.5, turning it on grew the canvas
        /// from 3414x1765 to 3414x2133 for the Terrain graph and from 3588x792 to 3588x2241 for the
        /// property-types graph, while the text stayed the same size — so the image looked smaller once
        /// fitted to a screen, which is the opposite of what the tool is for. Turn it on when the frame
        /// shape matters more than how large the graph reads.
        /// </summary>
        public bool autoAspect = false;

        /// <summary>
        /// Desired canvas width / height. 1 is square, 1.6 is a wide 16:10 frame.
        ///
        /// The width is fixed by the graph's longest path (one column per rank), so a squarer target
        /// is only reachable by widening the vertical gaps. On a graph with many ranks that turns into
        /// large empty space between nodes, so the default stops at a normal 16:10 frame rather than
        /// chasing a square. Lower it deliberately when the frame matters more than density.
        /// </summary>
        public float targetAspect = 1.6f;

        public bool showMacOsFrame = true;
        public bool showDropShadow = true;
        public bool showWatermark = true;
        public float cornerRadius = 12f;
        public float frameMargin = 24f;
        public float shadowOffset = 6f;

        public Color frameColor = new Color(0.16f, 0.16f, 0.18f, 1f);
        public Color frameBorderColor = new Color(0f, 0f, 0f, 0.65f);
        public Color blurTint = new Color(0.30f, 0.32f, 0.55f, 1f);

        public Texture2D watermarkLogo;
        public string authorName = "";

        /// <summary>
        /// Source of the date stamped into the watermark; null means the system clock.
        ///
        /// Exposed because the date makes the output change on its own: a test that compares two exports
        /// byte-for-byte would fail if they happened to straddle midnight, and the failure would look like
        /// a rendering bug. A test freezes this to a fixed instant instead.
        /// </summary>
        [System.NonSerialized] public System.Func<System.DateTime> clock;

        /// <summary>
        /// Supersampling factor applied on top of the 1x canvas. 1 already yields a 2864px-wide image
        /// for a mid-size graph, which is more than any viewer shows at once, so the default does not
        /// spend memory doubling it. Raise it when the PNG has to survive being cropped or printed.
        /// </summary>
        public int resolutionMultiplier = 1;

        /// <summary>
        /// Multiplies every font size, and the node height that has to hold the text. This is the
        /// readability control: the canvas width is fixed by the graph's column count, so the only way
        /// to make the text larger relative to the image is to make it larger outright.
        ///
        /// Measured on the 38-node Terrain fixture, viewed fit-to-window on a 1600x900 screen:
        /// 1.0 puts a node title at 7.5px and a port label at 6.0px, both too small to read; 1.5
        /// reaches 11.1px and 8.9px for a canvas only 1% taller. Past 1.5 the image height starts
        /// binding and the on-screen size stops improving, so 1.5 is where the default sits.
        /// </summary>
        public float textScale = 1.5f;

        public bool lightPreview = false;
        public int nodeCountWarningThreshold = 150;

        public void CopyFrom(SnippetExportPreset other)
        {
            if (other == null) return;
            wireColorMode = other.wireColorMode;
            wireStyle = other.wireStyle;
            backgroundMode = other.backgroundMode;
            backgroundColor = other.backgroundColor;
            backgroundGradientTop = other.backgroundGradientTop;
            backgroundGradientBottom = other.backgroundGradientBottom;
            singleWireColor = other.singleWireColor;
            wireWidth = other.wireWidth;
            wireCornerRadius = other.wireCornerRadius;
            layoutBalance = other.layoutBalance;
            nodeLocality = other.nodeLocality;
            showGroups = other.showGroups;
            showNotes = other.showNotes;
            highlightCriticalPath = other.highlightCriticalPath;
            showColumnGuides = other.showColumnGuides;
            showPortLegend = other.showPortLegend;
            showNodeValues = other.showNodeValues;
            verticalSpread = other.verticalSpread;
            autoAspect = other.autoAspect;
            targetAspect = other.targetAspect;
            arrowHead = other.arrowHead;
            showMacOsFrame = other.showMacOsFrame;
            showDropShadow = other.showDropShadow;
            showWatermark = other.showWatermark;
            cornerRadius = other.cornerRadius;
            frameMargin = other.frameMargin;
            shadowOffset = other.shadowOffset;
            frameColor = other.frameColor;
            frameBorderColor = other.frameBorderColor;
            blurTint = other.blurTint;
            watermarkLogo = other.watermarkLogo;
            authorName = other.authorName;
            clock = other.clock;
            resolutionMultiplier = other.resolutionMultiplier;
            textScale = other.textScale;
            lightPreview = other.lightPreview;
            nodeCountWarningThreshold = other.nodeCountWarningThreshold;
        }
    }
}
