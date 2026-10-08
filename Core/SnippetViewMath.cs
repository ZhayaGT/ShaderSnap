using UnityEngine;

namespace ShaderSnap.Core
{
    public static class SnippetViewMath
    {
        public const float MinZoom = 0.1f;
        public const float MaxZoom = 2f;

        public static float FitScale(Vector2 content, Vector2 viewport)
        {
            if (content.x <= 0f || content.y <= 0f) return 1f;
            if (viewport.x <= 0f || viewport.y <= 0f) return 1f;

            float scale = Mathf.Min(viewport.x / content.x, viewport.y / content.y);
            return Clamp(scale);
        }

        public static float Clamp(float zoom)
        {
            if (float.IsNaN(zoom) || zoom <= 0f) return MinZoom;
            return Mathf.Clamp(zoom, MinZoom, MaxZoom);
        }

        public static Vector2 ScaledSize(Vector2 content, float zoom)
        {
            float clamped = Clamp(zoom);
            return new Vector2(content.x * clamped, content.y * clamped);
        }
    }
}
