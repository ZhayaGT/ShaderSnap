using UnityEngine;

namespace ShaderSnap.Core
{
    /// <summary>
    /// Single source of truth for node layout metrics and font sizes. The user-facing
    /// <c>textScale</c> preset value is folded in through <see cref="FromFontScale"/>.
    ///
    /// Only the vertical metrics and the font sizes follow the scale. Node width and the
    /// horizontal/vertical gaps deliberately stay fixed: growing the text while keeping the
    /// column width and the empty space constant raises the text-to-canvas ratio, which is the
    /// readability problem this type exists to solve. The canvas therefore only grows in height.
    /// </summary>
    public readonly struct LayoutMetrics
    {
        public const float GridSize = 8f;
        public const float MinFontScale = 0.5f;
        public const float MaxFontScale = 3f;
        public const float WireLaneSpacing = 10f;
        public const float WireLaneMargin = 12f;

        /// <summary>
        /// Narrowest node box the layout will use. The renderer raises this when the graph has labels
        /// that need more room; nothing ever goes below it, so short graphs keep Shader Graph's own
        /// proportions instead of collapsing to a cramped column.
        /// </summary>
        public const float MinNodeWidth = 200f;

        const float BasePortRowHeight = 22f;
        const float BaseTitleHeight = 32f;
        const float BaseHorizontalGap = 56f;
        public const float BaseVerticalGap = 28f;
        const float BasePadding = 60f;

        const float BaseTitleFontSize = 15f;
        const float BasePortLabelFontSize = 12f;
        const float BaseWatermarkFontSize = 12f;
        const float BasePortRadius = 4f;

        public const float MinVerticalSpread = 1f;
        public const float MaxVerticalSpread = 24f;

        public readonly float NodeWidth;
        public readonly float PortRowHeight;
        public readonly float TitleHeight;
        public readonly float HorizontalGap;
        public readonly float VerticalGap;
        public readonly float Padding;
        public readonly float FontScale;
        public readonly float VerticalSpread;

        /// <summary>
        /// When set, nodes that carry a value reserve one extra row for it. The flag lives here rather
        /// than in the renderer because it changes node heights, so the layout has to know.
        /// </summary>
        public readonly bool ShowNodeValues;

        LayoutMetrics(float nodeWidth, float portRowHeight, float titleHeight, float horizontalGap,
                      float verticalGap, float padding, float fontScale, float verticalSpread,
                      bool showNodeValues)
        {
            NodeWidth = nodeWidth;
            PortRowHeight = portRowHeight;
            TitleHeight = titleHeight;
            HorizontalGap = horizontalGap;
            VerticalGap = verticalGap;
            Padding = padding;
            FontScale = fontScale;
            VerticalSpread = verticalSpread;
            ShowNodeValues = showNodeValues;
        }

        public float TitleFontSize => BaseTitleFontSize * FontScale;
        public float PortLabelFontSize => BasePortLabelFontSize * FontScale;
        public float WatermarkFontSize => BaseWatermarkFontSize * FontScale;
        public float PortRadius => BasePortRadius * FontScale;
        /// <summary>
        /// Height of the category colour band under the title. Shader Graph draws it as a fixed 8px
        /// border; the upper bound keeps it from eating the title when the font scale is small.
        /// </summary>
        public float CategoryStripHeight => Mathf.Min(8f, TitleHeight * 0.35f);

        public static LayoutMetrics Default => FromFontScale(1f);

        /// <summary>
        /// Builds the metric set for a font scale.
        /// </summary>
        /// <param name="nodeWidth">
        /// Node box width. The renderer measures the graph's own labels and passes the result, so a
        /// graph with long node names gets wider boxes instead of clipped titles. Callers that have no
        /// font to measure with leave it at <see cref="MinNodeWidth"/>.
        /// </param>
        public static LayoutMetrics FromFontScale(float fontScale, float verticalSpread = 1f,
                                                  bool showNodeValues = false, float nodeWidth = MinNodeWidth)
        {
            float scale = Mathf.Clamp(fontScale, MinFontScale, MaxFontScale);
            float spread = Mathf.Clamp(verticalSpread, MinVerticalSpread, MaxVerticalSpread);
            return new LayoutMetrics(
                Mathf.Max(MinNodeWidth, nodeWidth),
                Mathf.Round(BasePortRowHeight * scale),
                Mathf.Round(BaseTitleHeight * scale),
                BaseHorizontalGap,
                Mathf.Round(BaseVerticalGap * spread),
                BasePadding,
                scale,
                spread,
                showNodeValues);
        }
    }
}
