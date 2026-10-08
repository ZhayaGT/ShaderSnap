namespace ShaderSnap.Core
{
    /// <summary>
    /// Everything the layout needs to know beyond the graph and its metrics. Passed as a struct so the
    /// engine stays free of any dependency on the preset asset.
    /// </summary>
    public struct LayoutOptions
    {
        /// <summary>How far a node may move from its longest-path column; 0 pins the classic ranking.</summary>
        public float balance;

        /// <summary>
        /// Among the columns a node may legally occupy, how strongly to prefer the one nearest the nodes
        /// that consume it. 0 spreads nodes into the lightest column; 1 hugs consumers.
        /// </summary>
        public float locality;

        public bool snapToGrid;

        /// <summary>Marks the longest dependency chain so the renderer can emphasise it.</summary>
        public bool highlightCriticalPath;

        /// <summary>Reserves a band at the bottom of the canvas for sticky notes.</summary>
        public bool reserveNotesBand;

        /// <summary>Reserves a band at the bottom of the canvas for the port type legend.</summary>
        public bool reserveLegendBand;

        /// <summary>
        /// Whether group frames are drawn. Only affects the space reserved around the graph: a frame
        /// reaches above its topmost member to make room for its title, so the caller's inset has to
        /// include that overhang or the title is drawn outside the content area.
        /// </summary>
        public bool showGroupFrames;

        /// <summary>
        /// Minimum distance the graph and the bands keep from the canvas edge. The renderer raises this
        /// when a macOS frame is drawn, so the frame border can never cross the content and the bands
        /// always sit inside the frame rather than poking through its bottom edge.
        /// </summary>
        public float canvasInset;

        public static LayoutOptions Default => new LayoutOptions
        {
            balance = 1f,
            locality = 1f
        };
    }
}
