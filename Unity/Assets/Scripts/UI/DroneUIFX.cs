using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// ═════════════════════════════════════════════════════════════════════════
// DroneUIFX
//
// A small, self-contained widget/animation library used by DroneDashboardUI
// and DroneUIUpdater to build the "modern HUD" look:
//   - Radial ring gauges   (battery, motors, latency)
//   - Gradient bars        (vibration X/Y/Z)
//   - Compass tape ribbon  (heading)
//   - Artificial horizon   (pitch/roll)
//   - Signal / satellite bars (GPS)
//   - Glassmorphic button hover glow
//
// DESIGN NOTES
// ------------
// * No external art assets are required. Circles and rounded rectangles are
//   built from Unity's built-in editor sprites (UI/Skin/Knob.psd and
//   UI/Skin/UISprite.psd), which ship with every Unity install. This keeps
//   the "everything built at runtime from code" philosophy of the original
//   DroneDashboardUI script intact — nothing to import, nothing to lose in
//   version control, nothing to re-wire if you regenerate the canvas.
// * "Glow" is faked (no Bloom/post-processing dependency) by stacking a
//   soft, larger, low-alpha copy of a shape behind/around the real shape and
//   pulsing its alpha/scale. This works in any render pipeline (Built-in,
//   URP, HDRP) with zero setup. If you DO have URP/HDRP Bloom configured,
//   you can push these colours to HDR (e.g. color * 1.5f) for a stronger effect —
//   see the `hdrBoost` knobs below.
// * All animation is centralised in UIFXAnimator, one component added to the
//   canvas root. Each widget implements IFxWidget.Tick(dt) so values glide
//   smoothly between telemetry packets instead of snapping — this alone is a
//   big part of what makes a dashboard feel "alive" rather than "spreadsheet".
// * DroneUIUpdater still owns *when* values change (i.e. on new telemetry).
//   These widgets own *how* the change looks (colour ramps, easing, glow).
// ═════════════════════════════════════════════════════════════════════════

public static class DroneUIFX
{
    // ── Built-in sprites (no import needed) ─────────────────────────────
    static Sprite _circleSprite;
    static Sprite _roundedRectSprite;

    /// Unity's builtin UI sprites (UI/Skin/*.psd) exist in the editor but are
    /// NOT packed into standalone player builds (verified — player log shows
    /// "The resource UI/Skin/UISprite.psd could not be loaded"). We fall back to
    /// procedurally-generated textures so the UI renders identically in both.
    public static Sprite CircleSprite =>
        _circleSprite ??= LoadSprite("UI/Skin/Knob.psd") ?? GenerateCircleSprite();

    public static Sprite RoundedRectSprite =>
        _roundedRectSprite ??= GenerateRoundedRectSprite();

    static Sprite LoadSprite(string path)
    {
        try { return Resources.GetBuiltinResource<Sprite>(path); }
        catch { return null; }
    }

    /// Sharply radiused sliced rectangle used for all GCS surfaces.
    static Sprite GenerateRoundedRectSprite()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.name = "ProcRoundedRect";
        float r = 4f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                // distance from nearest rounded corner → alpha
                bool top    = y >= S - r;
                bool bottom = y <= r - 1;
                bool left   = x <= r - 1;
                bool right  = x >= S - r;

                float a = 1f;
                // corner rounding: inside if within radius of the corner centre
                if ((top && left) || (top && right) || (bottom && left) || (bottom && right))
                {
                    float cx = left ? r - 1 : S - r;
                    float cy = top ? S - r : r - 1;
                    float dx = x - cx, dy = y - cy;
                    a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var spr = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f,
            0u, SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
        spr.name = "ProcRoundedRect";
        return spr;
    }

    /// 64x64 circle with soft edge — substitutes UI/Skin/Knob.psd.
    static Sprite GenerateCircleSprite()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.name = "ProcCircle";
        float c = (S - 1) / 2f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(c + 0.5f - d); // hard edge inside radius
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        var spr = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f,
            0u, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
        spr.name = "ProcCircle";
        return spr;
    }

    public enum IconType
    {
        Drone, Connection, Mode, Lock, Signal, Gps, Heading, Clock,
        Camera, Map, Settings, Attitude, Position, Speed, Controls,
        Battery, Telemetry, System, Target, Layers, Lighting, Fullscreen,
        Altitude
    }

    static readonly Dictionary<IconType, Sprite> iconSprites = new Dictionary<IconType, Sprite>();

    public static Image CreateIcon(Transform parent, IconType type, float size, Color color)
    {
        var go = new GameObject(type + "Icon", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = GetIconSprite(type);
        image.color = color;
        image.preserveAspect = true;
        image.raycastTarget = false;
        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = size;
        layout.preferredHeight = size;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
        return image;
    }

    static Sprite GetIconSprite(IconType type)
    {
        if (iconSprites.TryGetValue(type, out Sprite sprite)) return sprite;

        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "GCS Icon " + type,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var clear = new Color32[size * size];
        texture.SetPixels32(clear);
        Color ink = Color.white;

        void Line(float x0, float y0, float x1, float y1, float width = 4.5f)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1) - width));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + width));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, y1) - width));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(y0, y1) + width));
            Vector2 a = new Vector2(x0, y0);
            Vector2 b = new Vector2(x1, y1);
            Vector2 ab = b - a;
            float lengthSq = Mathf.Max(ab.sqrMagnitude, 0.001f);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
                float distance = Vector2.Distance(p, a + ab * t);
                float alpha = Mathf.Clamp01(width * 0.5f + 0.7f - distance);
                if (alpha > 0f) texture.SetPixel(x, y, new Color(ink.r, ink.g, ink.b, alpha));
            }
        }

        void Circle(float cx, float cy, float radius, float width = 4.5f)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radius - width));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(cx + radius + width));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radius - width));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(cy + radius + width));
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float distance = Mathf.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - radius);
                float alpha = Mathf.Clamp01(width * 0.5f + 0.7f - distance);
                if (alpha > 0f) texture.SetPixel(x, y, new Color(ink.r, ink.g, ink.b, alpha));
            }
        }

        void Rect(float x0, float y0, float x1, float y1, float width = 4.5f)
        {
            Line(x0, y0, x1, y0, width); Line(x1, y0, x1, y1, width);
            Line(x1, y1, x0, y1, width); Line(x0, y1, x0, y0, width);
        }

        switch (type)
        {
            case IconType.Drone:
                Circle(32, 32, 6); Line(27, 27, 17, 17); Line(37, 27, 47, 17);
                Line(27, 37, 17, 47); Line(37, 37, 47, 47);
                Circle(14, 14, 6); Circle(50, 14, 6); Circle(14, 50, 6); Circle(50, 50, 6); break;
            case IconType.Connection:
            case IconType.Signal:
                Line(15, 21, 15, 27); Line(25, 21, 25, 35); Line(35, 21, 35, 43); Line(45, 21, 45, 51); break;
            case IconType.Mode:
                Line(14, 20, 50, 20); Line(14, 32, 50, 32); Line(14, 44, 50, 44);
                Circle(24, 20, 4); Circle(40, 32, 4); Circle(29, 44, 4); break;
            case IconType.Lock:
                Rect(17, 15, 47, 38); Circle(32, 38, 12); Line(20, 38, 20, 44); Line(44, 38, 44, 44); break;
            case IconType.Gps:
            case IconType.Position:
                Circle(32, 36, 13); Circle(32, 36, 4); Line(21, 27, 32, 12); Line(43, 27, 32, 12); break;
            case IconType.Heading:
                Circle(32, 32, 20); Line(32, 48, 25, 23); Line(32, 48, 39, 23); break;
            case IconType.Clock:
                Circle(32, 32, 20); Line(32, 32, 32, 45); Line(32, 32, 43, 26); break;
            case IconType.Camera:
                Rect(12, 18, 52, 44); Rect(22, 44, 34, 49); Circle(32, 31, 9); break;
            case IconType.Map:
                Line(12, 15, 12, 48); Line(12, 48, 27, 42); Line(27, 42, 40, 48); Line(40, 48, 52, 42);
                Line(52, 42, 52, 15); Line(52, 15, 40, 21); Line(40, 21, 27, 15); Line(27, 15, 12, 21);
                Line(27, 15, 27, 42); Line(40, 21, 40, 48); break;
            case IconType.Settings:
                Circle(32, 32, 9); Circle(32, 32, 20); Line(32, 7, 32, 14); Line(32, 50, 32, 57);
                Line(7, 32, 14, 32); Line(50, 32, 57, 32); Line(14, 14, 19, 19); Line(45, 45, 50, 50);
                Line(14, 50, 19, 45); Line(45, 19, 50, 14); break;
            case IconType.Attitude:
                Circle(32, 32, 21); Line(12, 32, 52, 32); Line(21, 39, 43, 39); Line(26, 25, 38, 25); break;
            case IconType.Speed:
                Circle(32, 29, 20); Line(32, 29, 44, 42); Line(17, 13, 47, 13); break;
            case IconType.Controls:
                Line(12, 20, 52, 20); Line(12, 32, 52, 32); Line(12, 44, 52, 44);
                Circle(22, 20, 5); Circle(42, 32, 5); Circle(30, 44, 5); break;
            case IconType.Battery:
                Rect(10, 19, 50, 45); Rect(50, 27, 55, 37); Line(17, 25, 17, 39); Line(24, 25, 24, 39); Line(31, 25, 31, 39); break;
            case IconType.Telemetry:
                Line(8, 30, 17, 30); Line(17, 30, 23, 45); Line(23, 45, 31, 16); Line(31, 16, 39, 39); Line(39, 39, 46, 30); Line(46, 30, 56, 30); break;
            case IconType.System:
                Rect(18, 18, 46, 46); Rect(25, 25, 39, 39); for (int i = 20; i <= 44; i += 8) { Line(i, 11, i, 18); Line(i, 46, i, 53); Line(11, i, 18, i); Line(46, i, 53, i); } break;
            case IconType.Target:
                Circle(32, 32, 18); Circle(32, 32, 5); Line(32, 7, 32, 17); Line(32, 47, 32, 57); Line(7, 32, 17, 32); Line(47, 32, 57, 32); break;
            case IconType.Layers:
                Line(10, 38, 32, 51); Line(32, 51, 54, 38); Line(54, 38, 32, 25); Line(32, 25, 10, 38);
                Line(10, 28, 32, 15); Line(32, 15, 54, 28); break;
            case IconType.Lighting:
                Circle(32, 32, 10); Line(32, 7, 32, 16); Line(32, 48, 32, 57); Line(7, 32, 16, 32); Line(48, 32, 57, 32);
                Line(14, 14, 20, 20); Line(44, 44, 50, 50); Line(14, 50, 20, 44); Line(44, 20, 50, 14); break;
            case IconType.Fullscreen:
                Line(12, 26, 12, 12); Line(12, 12, 26, 12); Line(38, 12, 52, 12); Line(52, 12, 52, 26);
                Line(12, 38, 12, 52); Line(12, 52, 26, 52); Line(38, 52, 52, 52); Line(52, 52, 52, 38); break;
            case IconType.Altitude:
                Line(32, 10, 32, 54); Line(32, 54, 23, 43); Line(32, 54, 41, 43); Line(16, 15, 48, 15); break;
        }

        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
            0u, SpriteMeshType.FullRect, Vector4.zero);
        sprite.name = "GCS Icon " + type;
        iconSprites[type] = sprite;
        return sprite;
    }

    // ── Shared colour ramps ──────────────────────────────────────────────
    public static readonly Color COL_GREEN  = new Color(0.20f, 0.90f, 0.50f);
    public static readonly Color COL_YELLOW = new Color(1.00f, 0.85f, 0.20f);
    public static readonly Color COL_ORANGE = new Color(1.00f, 0.55f, 0.10f);
    public static readonly Color COL_RED    = new Color(1.00f, 0.25f, 0.25f);
    public static readonly Color COL_CYAN   = new Color(0.25f, 0.85f, 0.95f);
    public static readonly Color COL_AMBER  = new Color(1.00f, 0.70f, 0.15f);
    public static readonly Color COL_EMERALD= new Color(0.10f, 0.95f, 0.55f);

    // ── Aerospace GCS palette (professional, desaturated, high-contrast) ─
    // Backgrounds: very dark graphite/navy, not pure black, to reduce eye strain
    // Cards: slightly lighter slate with subtle border. Accents: muted teal/amber/rose
    // All text: high-contrast white (0.92) vs secondary slate (0.60)
    public static readonly Color AERO_BG          = new Color(0.027f, 0.063f, 0.094f, 1f); // #071018
    public static readonly Color AERO_PANEL       = new Color(0.039f, 0.090f, 0.125f, 1f); // #0A1720
    public static readonly Color AERO_CARD        = new Color(0.047f, 0.106f, 0.145f, 1f); // #0C1B25
    public static readonly Color AERO_CARD2       = new Color(0.055f, 0.125f, 0.169f, 1f);
    public static readonly Color AERO_BORDER      = new Color(0.090f, 0.208f, 0.275f, 1f); // #173546
    public static readonly Color AERO_DIVIDER     = new Color(0.106f, 0.235f, 0.298f, 0.68f);
    public static readonly Color AERO_TEXT        = new Color(0.902f, 0.933f, 0.957f, 1f); // #E6EEF4
    public static readonly Color AERO_TEXT_SEC    = new Color(0.565f, 0.643f, 0.690f, 1f);
    public static readonly Color AERO_TEXT_DIM    = new Color(0.365f, 0.467f, 0.525f, 1f);
    public static readonly Color AERO_ACCENT      = new Color(0.125f, 0.847f, 0.941f, 1f); // #20D8F0
    public static readonly Color AERO_ACCENT_DIM  = new Color(0.125f, 0.847f, 0.941f, 0.12f);
    public static readonly Color AERO_AMBER       = new Color(0.953f, 0.714f, 0.184f, 1f); // #F3B62F
    public static readonly Color AERO_RED         = new Color(0.941f, 0.310f, 0.310f, 1f); // #F04F4F
    public static readonly Color AERO_RED_BG      = new Color(0.941f, 0.310f, 0.310f, 0.12f);
    public static readonly Color AERO_GREEN       = new Color(0.208f, 0.875f, 0.541f, 1f); // #35DF8A
    // Monospace number colour: slightly cooler white
    public static readonly Color AERO_NUM         = new Color(0.902f, 0.933f, 0.957f, 1f);

    /// 3-stop gradient: 0 = red, 0.5 = yellow, 1 = green. Used for "fraction remaining" style
    /// gauges (battery, latency-quality) where high = good.
    public static Color RampGoodHigh(float t01)
    {
        t01 = Mathf.Clamp01(t01);
        return t01 < 0.5f
            ? Color.Lerp(COL_RED, COL_YELLOW, t01 * 2f)
            : Color.Lerp(COL_YELLOW, COL_GREEN, (t01 - 0.5f) * 2f);
    }

    /// 3-stop gradient: 0 = cyan (calm), 0.5 = amber, 1 = red (stressed). Used for anything
    /// where high = bad (vibration, motor stress, latency in ms).
    public static Color RampBadHigh(float t01)
    {
        t01 = Mathf.Clamp01(t01);
        return t01 < 0.5f
            ? Color.Lerp(COL_CYAN, COL_ORANGE, t01 * 2f)
            : Color.Lerp(COL_ORANGE, COL_RED, (t01 - 0.5f) * 2f);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Animation driver
    // ═════════════════════════════════════════════════════════════════════

    public interface IFxWidget
    {
        void Tick(float dt);
    }

    /// One of these lives on the Canvas root. Every FX widget registers itself here
    /// so smoothing/pulsing keeps running every frame regardless of telemetry rate.
    public class UIFXAnimator : MonoBehaviour
    {
        public readonly List<IFxWidget> widgets = new List<IFxWidget>();

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < widgets.Count; i++)
                widgets[i]?.Tick(dt);
        }
    }

    static UIFXAnimator _animator;
    public static UIFXAnimator GetAnimator(Transform canvasRoot)
    {
        // Unity's == operator on a UnityEngine.Object returns true when the
        // object has been destroyed, so a cached animator from a previous
        // scene reloads cleanly instead of leaving widgets pointing at garbage.
        if (_animator != null)
            return _animator;

        _animator = canvasRoot.GetComponent<UIFXAnimator>();
        if (_animator == null)
            _animator = canvasRoot.gameObject.AddComponent<UIFXAnimator>();
        return _animator;
    }

    // ═════════════════════════════════════════════════════════════════════
    // 1 & 4 & 9. Radial ring gauge  (battery / motor / latency)
    // ═════════════════════════════════════════════════════════════════════

    public class RadialGaugeFX : IFxWidget
    {
        public Image fill;          // the coloured arc
        public Image glow;          // soft halo behind the ring
        public TextMeshProUGUI valueText;
        public Func<float, Color> colorRamp = RampGoodHigh;
        public float smoothSpeed = 6f;
        public bool pulseWhenHot = false;   // pulse the glow when value is near 1 (e.g. motor overload)
        public float pulseThreshold = 0.85f;

        float displayValue;
        float targetValue;
        float pulsePhase;

        public void SetTarget01(float v) => targetValue = Mathf.Clamp01(v);

        public void Tick(float dt)
        {
            displayValue = Mathf.MoveTowards(displayValue, targetValue, dt * smoothSpeed * 0.5f + Mathf.Abs(targetValue - displayValue) * dt * smoothSpeed);
            if (fill != null)
            {
                fill.fillAmount = displayValue;
                var c = colorRamp(displayValue);
                fill.color = c;

                if (glow != null)
                {
                    float baseAlpha = Mathf.Lerp(0.10f, 0.45f, displayValue);
                    if (pulseWhenHot && displayValue >= pulseThreshold)
                    {
                        pulsePhase += dt * 6f;
                        baseAlpha = 0.35f + 0.35f * (0.5f + 0.5f * Mathf.Sin(pulsePhase));
                    }
                    glow.color = new Color(c.r, c.g, c.b, baseAlpha);
                }
            }
        }
    }

    /// Builds a ring gauge: outer glow halo + track + radial fill + centre "hole" punch +
    /// optional icon glyph + value text, all centred in a square of the given diameter.
    /// `holeColor` should match whatever panel/background the ring sits on, so the punched
    /// centre reads as a true ring rather than a solid pie.
    public static RadialGaugeFX CreateRadialRing(
        Transform parent, string name, float diameter, Color trackColor, Color holeColor,
        string iconGlyph, float iconFontSize, string initialValueText, float valueFontSize,
        bool depleteClockwise = true)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rootRT = (RectTransform)root.transform;
        rootRT.sizeDelta = new Vector2(diameter, diameter);

        // Soft glow halo (bigger, blurred-looking via low alpha + slight oversize)
        var glowGO = new GameObject("Glow", typeof(RectTransform), typeof(Image));
        glowGO.transform.SetParent(root.transform, false);
        var glowImg = glowGO.GetComponent<Image>();
        glowImg.sprite = CircleSprite;
        glowImg.color = new Color(1, 1, 1, 0.15f);
        glowImg.raycastTarget = false;
        var glowRT = (RectTransform)glowGO.transform;
        glowRT.sizeDelta = new Vector2(diameter * 1.35f, diameter * 1.35f);
        Center(glowRT);

        // Track (dim full ring background)
        var trackGO = new GameObject("Track", typeof(RectTransform), typeof(Image));
        trackGO.transform.SetParent(root.transform, false);
        var trackImg = trackGO.GetComponent<Image>();
        trackImg.sprite = CircleSprite;
        trackImg.color = trackColor;
        trackImg.raycastTarget = false;
        var trackRT = (RectTransform)trackGO.transform;
        trackRT.sizeDelta = new Vector2(diameter, diameter);
        Center(trackRT);

        // Fill (the actual radial progress arc)
        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(root.transform, false);
        var fillImg = fillGO.GetComponent<Image>();
        fillImg.sprite = CircleSprite;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Radial360;
        fillImg.fillOrigin = (int)Image.Origin360.Top;
        // NOTE: fillClockwise = false makes the arc's trailing edge recede in the clockwise
        // direction as fillAmount drops (i.e. it visually "drains" clockwise). Flip this if
        // you prefer the opposite convention.
        fillImg.fillClockwise = !depleteClockwise;
        fillImg.fillAmount = 1f;
        fillImg.color = COL_GREEN;
        fillImg.raycastTarget = false;
        var fillRT = (RectTransform)fillGO.transform;
        fillRT.sizeDelta = new Vector2(diameter, diameter);
        Center(fillRT);

        // Punch (small solid circle matching the background, creates the "ring" hole)
        var holeGO = new GameObject("Hole", typeof(RectTransform), typeof(Image));
        holeGO.transform.SetParent(root.transform, false);
        var holeImg = holeGO.GetComponent<Image>();
        holeImg.sprite = CircleSprite;
        holeImg.color = holeColor;
        holeImg.raycastTarget = false;
        var holeRT = (RectTransform)holeGO.transform;
        holeRT.sizeDelta = new Vector2(diameter * 0.68f, diameter * 0.68f);
        Center(holeRT);

        // Icon glyph (kept as a text glyph so no external icon-font/sprite asset is needed —
        // swap for an Image+sprite later if you add a proper icon set)
        TextMeshProUGUI valueTMP;
        if (!string.IsNullOrEmpty(iconGlyph))
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform));
            iconGO.transform.SetParent(root.transform, false);
            var iconT = iconGO.AddComponent<TextMeshProUGUI>();
            iconT.text = iconGlyph;
            iconT.fontSize = iconFontSize;
            iconT.color = Color.white;
            iconT.alignment = TextAlignmentOptions.Center;
            var iconRT = iconT.rectTransform;
            iconRT.sizeDelta = new Vector2(diameter * 0.6f, diameter * 0.32f);
            iconRT.anchorMin = new Vector2(0.5f, 0.62f);
            iconRT.anchorMax = new Vector2(0.5f, 0.62f);
            iconRT.anchoredPosition = Vector2.zero;

            var valGO = new GameObject("Value", typeof(RectTransform));
            valGO.transform.SetParent(root.transform, false);
            valueTMP = valGO.AddComponent<TextMeshProUGUI>();
            valueTMP.text = initialValueText;
            valueTMP.fontSize = valueFontSize;
            valueTMP.fontStyle = FontStyles.Bold;
            valueTMP.color = Color.white;
            valueTMP.alignment = TextAlignmentOptions.Center;
            valueTMP.enableAutoSizing = true;
            valueTMP.fontSizeMin = Mathf.Max(8f, valueFontSize * 0.55f);
            valueTMP.fontSizeMax = valueFontSize;
            var valRT = valueTMP.rectTransform;
            valRT.sizeDelta = new Vector2(diameter * 0.6f, diameter * 0.34f);
            valRT.anchorMin = new Vector2(0.5f, 0.34f);
            valRT.anchorMax = new Vector2(0.5f, 0.34f);
            valRT.anchoredPosition = Vector2.zero;
        }
        else
        {
            var valGO = new GameObject("Value", typeof(RectTransform));
            valGO.transform.SetParent(root.transform, false);
            valueTMP = valGO.AddComponent<TextMeshProUGUI>();
            valueTMP.text = initialValueText;
            valueTMP.fontSize = valueFontSize;
            valueTMP.fontStyle = FontStyles.Bold;
            valueTMP.color = Color.white;
            valueTMP.alignment = TextAlignmentOptions.Center;
            valueTMP.enableAutoSizing = true;
            valueTMP.fontSizeMin = Mathf.Max(8f, valueFontSize * 0.55f);
            valueTMP.fontSizeMax = valueFontSize;
            var valRT = valueTMP.rectTransform;
            Stretch(valRT, diameter * 0.15f);
        }

        return new RadialGaugeFX { fill = fillImg, glow = glowImg, valueText = valueTMP };
    }

    // ═════════════════════════════════════════════════════════════════════
    // ═════════════════════════════════════════════════════════════════════
    // 8. Vibration gradient bars
    // ═════════════════════════════════════════════════════════════════════

    public class GradientBarFX : IFxWidget
    {
        public Image fill;
        public TextMeshProUGUI valueText;
        public float maxAbsValue = 80f; // full-scale value for the bar (m/s^2)
        float displayFrac, targetFrac;

        public void SetValue(float raw)
        {
            targetFrac = Mathf.Clamp01(Mathf.Abs(raw) / maxAbsValue);
            if (valueText != null) valueText.text = $"{raw:F2}";
        }

        public void Tick(float dt)
        {
            displayFrac = Mathf.Lerp(displayFrac, targetFrac, dt * 10f);
            if (fill != null)
            {
                fill.fillAmount = displayFrac;
                fill.color = RampBadHigh(displayFrac);
            }
        }
    }

    public static GradientBarFX CreateGradientBar(Transform parent, string label)
    {
        var row = new GameObject(label + "Bar", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        row.AddComponent<LayoutElement>().preferredHeight = 34;

        var vlg = row.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(0, 0, 2, 2);

        var headerGO = new GameObject("Header", typeof(RectTransform));
        headerGO.transform.SetParent(row.transform, false);
        headerGO.AddComponent<LayoutElement>().preferredHeight = 18;
        var hl = headerGO.AddComponent<HorizontalLayoutGroup>();
        hl.childForceExpandWidth = true;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(headerGO.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 9;
        lbl.fontStyle = FontStyles.Bold;
        lbl.color = new Color(0.6f, 0.72f, 0.85f);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;

        var valGO = new GameObject("Value", typeof(RectTransform));
        valGO.transform.SetParent(headerGO.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = "0.00";
        val.fontSize = 11;
        val.fontStyle = FontStyles.Bold;
        val.color = Color.white;
        val.alignment = TextAlignmentOptions.MidlineRight;

        var trackGO = new GameObject("Track", typeof(RectTransform), typeof(Image));
        trackGO.transform.SetParent(row.transform, false);
        trackGO.AddComponent<LayoutElement>().preferredHeight = 4;
        var trackImg = trackGO.GetComponent<Image>();
        trackImg.sprite = RoundedRectSprite;
        trackImg.type = Image.Type.Sliced;
        trackImg.color = new Color(1, 1, 1, 0.08f);

        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(trackGO.transform, false);
        var fillImg = fillGO.GetComponent<Image>();
        fillImg.sprite = RoundedRectSprite;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 0f;
        fillImg.color = COL_CYAN;
        Stretch((RectTransform)fillGO.transform, 0f);

        return new GradientBarFX { fill = fillImg, valueText = val };
    }

    // ═════════════════════════════════════════════════════════════════════
    // 8b. Flat horizontal gauge (battery %, generic fraction)
    // ═════════════════════════════════════════════════════════════════════

    public class HorizontalBarFX : IFxWidget
    {
        public Image fill;
        public Func<float, Color> colorRamp = RampGoodHigh;
        public float smoothSpeed = 8f;
        float displayFrac, targetFrac;

        public void SetValue01(float v) => targetFrac = Mathf.Clamp01(v);

        public float Target => targetFrac;

        public void Tick(float dt)
        {
            if (fill == null) return;
            displayFrac = Mathf.Lerp(displayFrac, targetFrac, dt * smoothSpeed);
            if (Mathf.Abs(displayFrac - targetFrac) < 0.002f) displayFrac = targetFrac;
            fill.fillAmount = displayFrac;
            fill.color = colorRamp(displayFrac);
        }
    }

    /// Flat horizontal bar (rounded track + filled progress) for text-first readouts
    /// such as the battery percentage.
    public static HorizontalBarFX CreateHorizontalBar(Transform parent, string name, float height)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.AddComponent<LayoutElement>().preferredHeight = height;

        var trackGO = new GameObject("Track", typeof(RectTransform), typeof(Image));
        trackGO.transform.SetParent(root.transform, false);
        var trackImg = trackGO.GetComponent<Image>();
        trackImg.sprite = RoundedRectSprite;
        trackImg.type = Image.Type.Sliced;
        trackImg.color = new Color(1f, 1f, 1f, 0.07f);
        trackImg.raycastTarget = false;
        Stretch((RectTransform)trackGO.transform, 0f);

        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(trackGO.transform, false);
        var fillImg = fillGO.GetComponent<Image>();
        fillImg.sprite = RoundedRectSprite;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 0f;
        fillImg.color = AERO_GREEN;
        fillImg.raycastTarget = false;
        Stretch((RectTransform)fillGO.transform, 0f);

        return new HorizontalBarFX { fill = fillImg };
    }

    // ═════════════════════════════════════════════════════════════════════
    // 6. Compass heading ribbon
    // ═════════════════════════════════════════════════════════════════════

    public class CompassRibbonFX : IFxWidget
    {
        public RectTransform tapeContent;
        public float pixelsPerDegree;
        float displayHeading, targetHeading;

        public void SetHeading(float degrees) => targetHeading = ((degrees % 360f) + 360f) % 360f;

        public void Tick(float dt)
        {
            // shortest-path smoothing across the 0/360 wrap
            float delta = Mathf.DeltaAngle(displayHeading, targetHeading);
            displayHeading += delta * Mathf.Clamp01(dt * 8f);
            displayHeading = ((displayHeading % 360f) + 360f) % 360f;
            if (tapeContent != null)
                tapeContent.anchoredPosition = new Vector2(-displayHeading * pixelsPerDegree, 0f);
        }
    }

    /// Builds a compact aircraft-style heading tape with a fixed centre pointer.
    public static CompassRibbonFX CreateCompassRibbon(Transform parent, float width, float height)
    {
        const float pxPerDeg = 7f;

        var root = new GameObject("CompassRibbon", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;

        // Low-opacity backing keeps the tape legible without covering the scene.
        var glassGO = new GameObject("Glass", typeof(RectTransform), typeof(Image));
        glassGO.transform.SetParent(root.transform, false);
        var glassImg = glassGO.GetComponent<Image>();
        glassImg.sprite = RoundedRectSprite;
        glassImg.type = Image.Type.Sliced;
        glassImg.color = new Color(AERO_BG.r, AERO_BG.g, AERO_BG.b, 0.58f);
        Stretch((RectTransform)glassGO.transform, 0f);

        // Mask/viewport so ticks don't spill outside the pill
        var vpGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        vpGO.transform.SetParent(root.transform, false);
        vpGO.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        vpGO.GetComponent<Mask>().showMaskGraphic = false;
        Stretch((RectTransform)vpGO.transform, 4f);

        // Tape content: repeats 0..360 twice so scrolling never shows an empty edge
        var contentGO = new GameObject("Tape", typeof(RectTransform));
        contentGO.transform.SetParent(vpGO.transform, false);
        var contentRT = (RectTransform)contentGO.transform;
        contentRT.anchorMin = new Vector2(0.5f, 0.5f);
        contentRT.anchorMax = new Vector2(0.5f, 0.5f);
        contentRT.pivot = new Vector2(0.5f, 0.5f);
        contentRT.sizeDelta = new Vector2(360f * pxPerDeg, height);

        string[] dirs = { "N", "E", "S", "W" };
        for (int deg = -360; deg <= 720; deg += 10)
        {
            int norm = ((deg % 360) + 360) % 360;
            bool cardinal = norm % 90 == 0;
            bool labelled = norm % 30 == 0;

            var markGO = new GameObject($"Mark_{deg}", typeof(RectTransform), typeof(Image));
            markGO.transform.SetParent(contentGO.transform, false);
            var mark = markGO.GetComponent<Image>();
            mark.color = cardinal ? AERO_ACCENT : new Color(AERO_TEXT.r, AERO_TEXT.g, AERO_TEXT.b, labelled ? 0.68f : 0.36f);
            mark.raycastTarget = false;
            var markRT = (RectTransform)markGO.transform;
            markRT.anchorMin = markRT.anchorMax = new Vector2(0.5f, 0f);
            markRT.sizeDelta = new Vector2(1f, cardinal ? 10f : labelled ? 7f : 4f);
            markRT.anchoredPosition = new Vector2(deg * pxPerDeg, 1f);

            if (!labelled) continue;
            var tickGO = new GameObject($"Tick_{deg}", typeof(RectTransform));
            tickGO.transform.SetParent(contentGO.transform, false);
            var t = tickGO.AddComponent<TextMeshProUGUI>();
            t.text = (norm % 90 == 0) ? dirs[norm / 90] : (norm.ToString());
            t.fontSize = cardinal ? 13 : 9;
            t.fontStyle = cardinal ? FontStyles.Bold : FontStyles.Normal;
            t.color = cardinal ? AERO_ACCENT : new Color(AERO_TEXT.r, AERO_TEXT.g, AERO_TEXT.b, 0.72f);
            t.alignment = TextAlignmentOptions.Center;
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(42, height - 7f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(deg * pxPerDeg, 5f);
        }

        // Fixed centre pointer
        var ptrGO = new GameObject("Pointer", typeof(RectTransform), typeof(Image));
        ptrGO.transform.SetParent(root.transform, false);
        var ptrImg = ptrGO.GetComponent<Image>();
        ptrImg.color = AERO_ACCENT;
        ptrImg.raycastTarget = false;
        var ptrRT = (RectTransform)ptrGO.transform;
        ptrRT.anchorMin = ptrRT.anchorMax = new Vector2(0.5f, 0f);
        ptrRT.pivot = new Vector2(0.5f, 0f);
        ptrRT.sizeDelta = new Vector2(2, 12);
        ptrRT.anchoredPosition = new Vector2(0, 1);

        return new CompassRibbonFX { tapeContent = contentRT, pixelsPerDegree = pxPerDeg };
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5. Artificial horizon
    // ═════════════════════════════════════════════════════════════════════

    public class HorizonFX : IFxWidget
    {
        public RectTransform horizonPlate; // rotates with roll, translates with pitch
        public RectTransform rollPointer;   // fixed bank-angle needle against the bezel scale
        public float pixelsPerDegreePitch = 4f;
        public float pitchClampDeg = 30f;
        float displayPitch, displayRoll, targetPitch, targetRoll;

        public void SetAttitude(float pitchDeg, float rollDeg)
        {
            targetPitch = Mathf.Clamp(pitchDeg, -pitchClampDeg, pitchClampDeg);
            targetRoll = rollDeg;
        }

        public void Tick(float dt)
        {
            displayPitch = Mathf.Lerp(displayPitch, targetPitch, dt * 8f);
            displayRoll = Mathf.LerpAngle(displayRoll, targetRoll, dt * 8f);
            if (horizonPlate != null)
            {
                horizonPlate.localRotation = Quaternion.Euler(0, 0, -displayRoll);
                horizonPlate.anchoredPosition = new Vector2(0, -displayPitch * pixelsPerDegreePitch);
            }
            // Bank needle points up when wings level and swings against the fixed scale
            if (rollPointer != null)
                rollPointer.localRotation = Quaternion.Euler(0, 0, -displayRoll);
        }
    }

    /// Builds a compact circular attitude indicator: sky/ground half-planes that rotate with
    /// roll and slide with pitch, a pitch ladder, a fixed bank scale with swinging needle, a
    /// fixed aircraft symbol, and a circular mask/bezel.
    public static HorizonFX CreateArtificialHorizon(Transform parent, float diameter)
    {
        var root = new GameObject("ArtificialHorizon", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = diameter;
        le.preferredHeight = diameter;

        // Fixed bezel disc (behind everything, gives the instrument a rim)
        var bezelGO = new GameObject("Bezel", typeof(RectTransform), typeof(Image));
        bezelGO.transform.SetParent(root.transform, false);
        var bezelImg = bezelGO.GetComponent<Image>();
        bezelImg.sprite = CircleSprite;
        bezelImg.color = AERO_CARD2;
        bezelImg.raycastTarget = false;
        var bezelRT = (RectTransform)bezelGO.transform;
        bezelRT.sizeDelta = new Vector2(diameter, diameter);
        Center(bezelRT);

        // Circular mask/viewport
        var vpGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        vpGO.transform.SetParent(root.transform, false);
        vpGO.GetComponent<Image>().sprite = CircleSprite;
        vpGO.GetComponent<Image>().color = Color.white;
        vpGO.GetComponent<Mask>().showMaskGraphic = true;
        var vpRT = (RectTransform)vpGO.transform;
        vpRT.sizeDelta = new Vector2(diameter - 10f, diameter - 10f);
        Center(vpRT);

        // Plate (oversized so pitch/roll never reveals empty corners)
        var plateGO = new GameObject("Plate", typeof(RectTransform));
        plateGO.transform.SetParent(vpGO.transform, false);
        var plateRT = (RectTransform)plateGO.transform;
        plateRT.sizeDelta = new Vector2(diameter * 2.2f, diameter * 2.2f);
        plateRT.anchorMin = plateRT.anchorMax = new Vector2(0.5f, 0.5f);
        plateRT.anchoredPosition = Vector2.zero;

        // Instrument colors remain deliberately muted and functional.
        var skyGO = new GameObject("Sky", typeof(RectTransform), typeof(Image));
        skyGO.transform.SetParent(plateGO.transform, false);
        skyGO.GetComponent<Image>().color = new Color(0.10f, 0.30f, 0.46f);
        var skyRT = (RectTransform)skyGO.transform;
        skyRT.anchorMin = new Vector2(0, 0.5f);
        skyRT.anchorMax = new Vector2(1, 1f);
        skyRT.offsetMin = skyRT.offsetMax = Vector2.zero;

        var groundGO = new GameObject("Ground", typeof(RectTransform), typeof(Image));
        groundGO.transform.SetParent(plateGO.transform, false);
        groundGO.GetComponent<Image>().color = new Color(0.24f, 0.17f, 0.11f);
        var groundRT = (RectTransform)groundGO.transform;
        groundRT.anchorMin = new Vector2(0, 0f);
        groundRT.anchorMax = new Vector2(1, 0.5f);
        groundRT.offsetMin = groundRT.offsetMax = Vector2.zero;

        Color ladderCol = new Color(0.92f, 0.93f, 0.95f, 0.55f);
        int pxPerDeg = 4;

        // Pitch ladder: fixed lines at ±5..±30°, horizontal in plate space so they
        // roll with the plate and slide with pitch like a real attitude indicator.
        for (int deg = -30; deg <= 30; deg += 5)
        {
            if (deg == 0) continue; // horizon line is drawn separately
            var lGO = new GameObject($"Ladder{deg:+00;-00}", typeof(RectTransform), typeof(Image));
            lGO.transform.SetParent(plateGO.transform, false);
            var lImg = lGO.GetComponent<Image>();
            lImg.color = ladderCol;

            // Major lines (every 10°) are wider, minors thinner
            bool major = (deg % 10) == 0;
            float w = major ? diameter * 0.34f : diameter * 0.20f;
            Color c = major ? new Color(0.92f, 0.93f, 0.95f, 0.85f) : ladderCol;
            lImg.color = c;

            var lRT = (RectTransform)lGO.transform;
            lRT.anchorMin = lRT.anchorMax = new Vector2(0.5f, 0.5f);
            lRT.sizeDelta = new Vector2(w, 1.2f);
            lRT.anchoredPosition = new Vector2(0, deg * pxPerDeg);
            if (major)
            {
                var numberGO = new GameObject("Value", typeof(RectTransform));
                numberGO.transform.SetParent(lGO.transform, false);
                var number = numberGO.AddComponent<TextMeshProUGUI>();
                number.text = Mathf.Abs(deg).ToString();
                number.fontSize = 7;
                number.color = c;
                number.alignment = TextAlignmentOptions.MidlineRight;
                number.raycastTarget = false;
                var numberRT = number.rectTransform;
                numberRT.anchorMin = numberRT.anchorMax = new Vector2(0, 0.5f);
                numberRT.pivot = new Vector2(1, 0.5f);
                numberRT.anchoredPosition = new Vector2(-4, 0);
                numberRT.sizeDelta = new Vector2(18, 10);
            }
        }

        var lineGO = new GameObject("HorizonLine", typeof(RectTransform), typeof(Image));
        lineGO.transform.SetParent(plateGO.transform, false);
        var lineImg = lineGO.GetComponent<Image>();
        lineImg.color = new Color(1f, 1f, 1f, 0.95f);
        var lineRT = (RectTransform)lineGO.transform;
        lineRT.anchorMin = new Vector2(0, 0.5f);
        lineRT.anchorMax = new Vector2(1, 0.5f);
        lineRT.sizeDelta = new Vector2(0, 2);
        lineImg.raycastTarget = false;

        // Fixed bank scale: ticks on the bezel at 0, ±15, ±30
        Color scaleCol = new Color(0.75f, 0.80f, 0.85f, 0.9f);
        MakeScaleTick(root.transform, 0f,  diameter / 2f, 6f, scaleCol);
        MakeScaleTick(root.transform, 15f, diameter / 2f, 4f, scaleCol);
        MakeScaleTick(root.transform, -15f, diameter / 2f, 4f, scaleCol);
        MakeScaleTick(root.transform, 30f, diameter / 2f, 3f, scaleCol);
        MakeScaleTick(root.transform, -30f, diameter / 2f, 3f, scaleCol);

        // Swinging bank needle (rotates against the fixed scale)
        var ptrGO = new GameObject("RollPointer", typeof(RectTransform), typeof(Image));
        ptrGO.transform.SetParent(root.transform, false);
        var ptrImg = ptrGO.GetComponent<Image>();
        ptrImg.color = AERO_ACCENT;
        ptrImg.raycastTarget = false;
        var ptrRT = (RectTransform)ptrGO.transform;
        ptrRT.sizeDelta = new Vector2(2.5f, diameter * 0.20f);
        ptrRT.anchorMin = ptrRT.anchorMax = new Vector2(0.5f, 0.5f);
        ptrRT.anchoredPosition = new Vector2(0, diameter * 0.36f);

        // Fixed aircraft symbol — wing bars + centre dot (drawn on root, not the plate)
        MakeAircraftSymbol(root.transform, diameter);

        return new HorizonFX { horizonPlate = plateRT, rollPointer = ptrRT };
    }

    static void MakeScaleTick(Transform parent, float angleDeg, float radius, float len, Color col)
    {
        var tGO = new GameObject($"ScaleTick{angleDeg}", typeof(RectTransform), typeof(Image));
        tGO.transform.SetParent(parent, false);
        var tImg = tGO.GetComponent<Image>();
        tImg.color = col;
        tImg.raycastTarget = false;
        var tRT = (RectTransform)tGO.transform;
        tRT.anchorMin = tRT.anchorMax = new Vector2(0.5f, 0.5f);
        tRT.sizeDelta = new Vector2(1.4f, len);
        tRT.localRotation = Quaternion.Euler(0, 0, -angleDeg);
        tRT.anchoredPosition = new Vector2(0, radius - 4f - len * 0.5f);
    }

    static void MakeAircraftSymbol(Transform parent, float diameter)
    {
        Color wingCol = new Color(0.98f, 0.99f, 1f, 0.95f);
        // left wing
        var lw = new GameObject("WingL", typeof(RectTransform), typeof(Image));
        lw.transform.SetParent(parent, false);
        lw.GetComponent<Image>().color = wingCol;
        lw.GetComponent<Image>().raycastTarget = false;
        var lwRT = (RectTransform)lw.transform;
        lwRT.sizeDelta = new Vector2(diameter * 0.26f, 2f);
        lwRT.anchorMin = lwRT.anchorMax = new Vector2(0.5f, 0.5f);
        lwRT.anchoredPosition = new Vector2(-diameter * 0.155f, 0);
        lwRT.localRotation = Quaternion.Euler(0, 0, 6f);
        // right wing
        var rw = new GameObject("WingR", typeof(RectTransform), typeof(Image));
        rw.transform.SetParent(parent, false);
        rw.GetComponent<Image>().color = wingCol;
        rw.GetComponent<Image>().raycastTarget = false;
        var rwRT = (RectTransform)rw.transform;
        rwRT.sizeDelta = new Vector2(diameter * 0.26f, 2f);
        rwRT.anchorMin = rwRT.anchorMax = new Vector2(0.5f, 0.5f);
        rwRT.anchoredPosition = new Vector2(diameter * 0.155f, 0);
        rwRT.localRotation = Quaternion.Euler(0, 0, -6f);
        // centre reference dot
        var dot = new GameObject("WingDot", typeof(RectTransform), typeof(Image));
        dot.transform.SetParent(parent, false);
        var dImg = dot.GetComponent<Image>();
        dImg.color = new Color(0.98f, 0.99f, 1f, 0.95f);
        dImg.sprite = CircleSprite;
        dImg.raycastTarget = false;
        var dRT = (RectTransform)dot.transform;
        dRT.sizeDelta = new Vector2(4f, 4f);
        dRT.anchorMin = dRT.anchorMax = new Vector2(0.5f, 0.5f);
        dRT.anchoredPosition = new Vector2(0, -2f);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7. GPS signal / satellite indicator
    // ═════════════════════════════════════════════════════════════════════

    public class SignalBarsFX : IFxWidget
    {
        public Image[] bars;         // short → tall, left → right
        public Color activeColor = COL_CYAN;
        public Color inactiveColor = new Color(1, 1, 1, 0.12f);
        int targetLevel; // 0..bars.Length

        /// quality01: 0 = no signal, 1 = excellent. Internally quantised to bar count.
        public void SetQuality01(float quality01)
        {
            if (bars == null || bars.Length == 0) return;
            targetLevel = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(quality01) * bars.Length), 0, bars.Length);
        }

        public void Tick(float dt)
        {
            if (bars == null) return;
            for (int i = 0; i < bars.Length; i++)
            {
                Color target = i < targetLevel ? activeColor : inactiveColor;
                bars[i].color = Color.Lerp(bars[i].color, target, dt * 10f);
            }
        }
    }

    public static SignalBarsFX CreateSignalBars(Transform parent, int barCount = 5, float maxHeight = 20f)
    {
        var root = new GameObject("SignalBars", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredHeight = maxHeight;
        le.preferredWidth = barCount * 12f;

        var hl = root.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 3;
        hl.childAlignment = TextAnchor.LowerLeft;
        hl.childForceExpandHeight = false;
        hl.childForceExpandWidth = false;

        var bars = new Image[barCount];
        for (int i = 0; i < barCount; i++)
        {
            var barGO = new GameObject($"Bar{i}", typeof(RectTransform), typeof(Image));
            barGO.transform.SetParent(root.transform, false);
            float h = maxHeight * ((i + 1f) / barCount);
            var img = barGO.GetComponent<Image>();
            img.sprite = RoundedRectSprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(1, 1, 1, 0.12f);
            var barLE = barGO.AddComponent<LayoutElement>();
            barLE.preferredWidth = 8;
            barLE.preferredHeight = h;
            bars[i] = img;
        }

        return new SignalBarsFX { bars = bars };
    }

    // ═════════════════════════════════════════════════════════════════════
    // 10. Glassmorphic command buttons
    // ═════════════════════════════════════════════════════════════════════

    /// Restyles an existing Button/Image pair (as produced by DroneDashboardUI.MakeButton) into
    /// a glassmorphic pill with a neon border and a hover/press glow, without touching its
    /// onClick listeners.
    public static void ApplyGlassStyle(GameObject buttonGO, Color accent)
    {
        var img = buttonGO.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = RoundedRectSprite;
            img.type = Image.Type.Sliced;
            // translucent glass fill instead of a flat colour block
            img.color = new Color(accent.r, accent.g, accent.b, 0.22f);
        }

        // Neon border (a second, slightly larger, outline-only rounded rect underneath is
        // approximated here with a thin sliced-image frame drawn via a border colour overlay)
        var borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
        borderGO.transform.SetParent(buttonGO.transform, false);
        borderGO.transform.SetAsFirstSibling();
        var borderImg = borderGO.GetComponent<Image>();
        borderImg.sprite = RoundedRectSprite;
        borderImg.type = Image.Type.Sliced;
        borderImg.color = new Color(accent.r, accent.g, accent.b, 0.9f);
        borderImg.raycastTarget = false;
        var borderRT = (RectTransform)borderGO.transform;
        Stretch(borderRT, 0f);
        // Inner cutout that sits above the border, leaving a ~2px ring visible — cheap "stroke"
        var innerGO = new GameObject("InnerFill", typeof(RectTransform), typeof(Image));
        innerGO.transform.SetParent(buttonGO.transform, false);
        innerGO.transform.SetSiblingIndex(1);
        var innerImg = innerGO.GetComponent<Image>();
        innerImg.sprite = RoundedRectSprite;
        innerImg.type = Image.Type.Sliced;
        innerImg.color = new Color(0.05f, 0.07f, 0.11f, 0.75f);
        innerImg.raycastTarget = false;
        Stretch((RectTransform)innerGO.transform, 2f);

        var hover = buttonGO.AddComponent<GlassButtonHoverFX>();
        hover.accent = accent;
        hover.borderImage = borderImg;
        hover.fillImage = img;
    }

    /// Per-button hover/press glow. Lightweight coroutine-based tween — no external animation
    /// package required.
    public class GlassButtonHoverFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Color accent = Color.white;
        public Image borderImage;
        public Image fillImage;
        Coroutine running;

        public void OnPointerEnter(PointerEventData e) => Retarget(0.55f, 1.4f);
        public void OnPointerExit(PointerEventData e) => Retarget(0.22f, 1f);
        public void OnPointerDown(PointerEventData e) => Retarget(0.85f, 0.85f);
        public void OnPointerUp(PointerEventData e) => Retarget(0.55f, 1.4f);

        void Retarget(float fillAlpha, float borderMul)
        {
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(TweenTo(fillAlpha, borderMul));
        }

        IEnumerator TweenTo(float fillAlpha, float borderMul)
        {
            float t = 0f;
            Color fromFill = fillImage.color;
            Color toFill = new Color(accent.r, accent.g, accent.b, fillAlpha);
            Color fromBorder = borderImage.color;
            Color toBorder = new Color(
                Mathf.Min(accent.r * borderMul, 1f),
                Mathf.Min(accent.g * borderMul, 1f),
                Mathf.Min(accent.b * borderMul, 1f), 1f);
            while (t < 1f)
            {
                t += Time.deltaTime * 8f;
                fillImage.color = Color.Lerp(fromFill, toFill, t);
                borderImage.color = Color.Lerp(fromBorder, toBorder, t);
                yield return null;
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Aerospace helpers — flat, bordered, non-neon GCS style
    // ═════════════════════════════════════════════════════════════════════

    public class AeroStatusPill : IFxWidget
    {
        public Image bg, dot;
        public TextMeshProUGUI label;
        Color targetBg, targetDot;
        string targetText;
        public void SetState(Color bgCol, Color dotCol, string text)
        {
            targetBg = bgCol; targetDot = dotCol; targetText = text;
        }
        public void Tick(float dt)
        {
            if (bg != null) bg.color = Color.Lerp(bg.color, targetBg, dt * 10f);
            if (dot != null) dot.color = Color.Lerp(dot.color, targetDot, dt * 10f);
            if (label != null)
            {
                if (label.text != targetText) label.text = targetText;
                label.color = Color.Lerp(label.color, targetDot, dt * 10f);
            }
        }
    }

    public static AeroStatusPill CreateAeroStatusPill(Transform parent, string name, float width, float height)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;
        le.flexibleWidth = 0;

        var bgGO = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(root.transform, false);
        var bgImg = bgGO.GetComponent<Image>();
        bgImg.sprite = RoundedRectSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = AERO_CARD;
        bgImg.raycastTarget = false;
        var bgLE = bgGO.AddComponent<LayoutElement>(); bgLE.ignoreLayout = true;
        Stretch((RectTransform)bgGO.transform, 0f);
        // border via outline image behind bg
        var borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
        borderGO.transform.SetParent(root.transform, false);
        borderGO.transform.SetAsFirstSibling();
        var borderImg = borderGO.GetComponent<Image>();
        borderImg.sprite = RoundedRectSprite;
        borderImg.type = Image.Type.Sliced;
        borderImg.color = AERO_BORDER;
        borderImg.raycastTarget = false;
        borderGO.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch((RectTransform)borderGO.transform, 0f);
        var innerGO = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        innerGO.transform.SetParent(root.transform, false);
        innerGO.transform.SetSiblingIndex(1);
        var innerImg = innerGO.GetComponent<Image>();
        innerImg.sprite = RoundedRectSprite;
        innerImg.type = Image.Type.Sliced;
        innerImg.color = AERO_CARD;
        innerImg.raycastTarget = false;
        innerGO.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch((RectTransform)innerGO.transform, 1f);

        var hl = root.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(10, 10, 0, 0);
        hl.spacing = 8;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childForceExpandWidth = false;

        var dotGO = new GameObject("Dot", typeof(RectTransform), typeof(Image));
        dotGO.transform.SetParent(root.transform, false);
        var dotImg = dotGO.GetComponent<Image>();
        dotImg.sprite = CircleSprite;
        dotImg.color = AERO_GREEN;
        var dotLE = dotGO.AddComponent<LayoutElement>();
        dotLE.preferredWidth = 10; dotLE.preferredHeight = 10;
        dotLE.flexibleWidth = 0;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(root.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = "--";
        lbl.fontSize = 12;
        lbl.fontStyle = FontStyles.Bold;
        lbl.color = AERO_TEXT;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;

        return new AeroStatusPill { bg = innerImg, dot = dotImg, label = lbl };
    }

    public static AeroButtonHoverFX ApplyAeroButtonStyle(GameObject buttonGO, Color accent, bool isDestructive = false)
    {
        var img = buttonGO.GetComponent<Image>();
        var button = buttonGO.GetComponent<Button>();
        if (button != null) button.transition = Selectable.Transition.None;
        if (img != null)
        {
            img.sprite = RoundedRectSprite;
            img.type = Image.Type.Sliced;
            img.color = isDestructive ? Color.Lerp(AERO_CARD, AERO_RED, 0.09f) : AERO_CARD2;
        }
        // flat border
        var borderGO = new GameObject("AeroBorder", typeof(RectTransform), typeof(Image));
        borderGO.transform.SetParent(buttonGO.transform, false);
        borderGO.transform.SetAsFirstSibling();
        var borderImg = borderGO.GetComponent<Image>();
        borderImg.sprite = RoundedRectSprite;
        borderImg.type = Image.Type.Sliced;
        borderImg.color = isDestructive ? new Color(AERO_RED.r, AERO_RED.g, AERO_RED.b, 0.58f) : AERO_BORDER;
        borderImg.raycastTarget = false;
        Stretch((RectTransform)borderGO.transform, 0f);
        var innerGO = new GameObject("AeroInner", typeof(RectTransform), typeof(Image));
        innerGO.transform.SetParent(buttonGO.transform, false);
        innerGO.transform.SetSiblingIndex(1);
        var innerImg = innerGO.GetComponent<Image>();
        innerImg.sprite = RoundedRectSprite;
        innerImg.type = Image.Type.Sliced;
        innerImg.color = img.color;
        innerImg.raycastTarget = false;
        Stretch((RectTransform)innerGO.transform, 1f);

        // subtle hover — no neon, just lighten/darken
        var hover = buttonGO.GetComponent<GlassButtonHoverFX>();
        if (hover != null) UnityEngine.Object.Destroy(hover);
        var aeroHover = buttonGO.AddComponent<AeroButtonHoverFX>();
        aeroHover.baseColor = img.color;
        aeroHover.borderImage = borderImg;
        aeroHover.fillImage = innerImg;
        aeroHover.accent = accent;
        aeroHover.isDestructive = isDestructive;
        aeroHover.label = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
        if (aeroHover.label != null) aeroHover.normalTextColor = aeroHover.label.color;
        aeroHover.SetSelected(false);
        return aeroHover;
    }

    public class AeroButtonHoverFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Color baseColor, accent;
        public Image borderImage, fillImage;
        public TextMeshProUGUI label;
        public Color normalTextColor;
        public Graphic foreground;
        public Color normalForegroundColor;
        public bool isDestructive;
        bool selected;
        bool hovered;
        bool pressed;
        Color targetFill;
        Color targetBorder;

        public void SetSelected(bool value)
        {
            selected = value;
            RefreshTargets();
            if (label) label.color = selected ? AERO_TEXT : normalTextColor;
            if (foreground) foreground.color = selected ? AERO_TEXT : normalForegroundColor;
        }

        void RefreshTargets()
        {
            // Keep the inner fill opaque. A translucent fill reveals the full-size
            // border sprite underneath and turns the entire button cyan.
            Color restingFill = selected ? Color.Lerp(AERO_CARD2, accent, 0.12f) : baseColor;
            Color restingBorder = selected
                ? new Color(accent.r, accent.g, accent.b, 0.92f)
                : isDestructive ? new Color(AERO_RED.r, AERO_RED.g, AERO_RED.b, 0.58f) : AERO_BORDER;
            float blend = pressed ? 0.28f : hovered ? 0.16f : 0f;
            targetFill = Color.Lerp(restingFill, accent, blend);
            targetBorder = hovered || pressed ? Color.Lerp(restingBorder, accent, 0.55f) : restingBorder;
        }

        void Update()
        {
            float t = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
            if (fillImage) fillImage.color = Color.Lerp(fillImage.color, targetFill, t);
            if (borderImage) borderImage.color = Color.Lerp(borderImage.color, targetBorder, t);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            hovered = true;
            RefreshTargets();
        }
        public void OnPointerExit(PointerEventData e)
        {
            hovered = false;
            pressed = false;
            RefreshTargets();
        }
        public void OnPointerDown(PointerEventData e)
        {
            pressed = true;
            RefreshTargets();
        }
        public void OnPointerUp(PointerEventData e)
        {
            pressed = false;
            RefreshTargets();
        }
    }

    // Graph card factory — returns (TelemetryGraph, valueText) pair
    public static TelemetryGraph CreateGraphCard(Transform parent, string title, string unit, Color accent, out TextMeshProUGUI valueTextOut, out TextMeshProUGUI titleTextOut)
    {
        var card = new GameObject(title + "GraphCard", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(parent, false);
        var cardImg = card.GetComponent<Image>();
        cardImg.sprite = RoundedRectSprite;
        cardImg.type = Image.Type.Sliced;
        cardImg.color = AERO_CARD;
        var cardLE = card.AddComponent<LayoutElement>();
        cardLE.flexibleWidth = 1;
        cardLE.flexibleHeight = 1;
        // border — ignored by VerticalLayoutGroup
        var borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
        borderGO.transform.SetParent(card.transform, false);
        borderGO.transform.SetAsFirstSibling();
        var borderImg = borderGO.GetComponent<Image>();
        borderImg.sprite = RoundedRectSprite;
        borderImg.type = Image.Type.Sliced;
        borderImg.color = AERO_BORDER;
        borderImg.raycastTarget = false;
        borderGO.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch((RectTransform)borderGO.transform, 0f);
        var innerGO = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        innerGO.transform.SetParent(card.transform, false);
        innerGO.transform.SetSiblingIndex(1);
        var innerImg = innerGO.GetComponent<Image>();
        innerImg.sprite = RoundedRectSprite;
        innerImg.type = Image.Type.Sliced;
        innerImg.color = AERO_CARD;
        innerImg.raycastTarget = false;
        innerGO.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch((RectTransform)innerGO.transform, 1f);

        var vl = card.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(8, 8, 7, 6);
        vl.spacing = 4;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        vl.childControlHeight = true;

        // Header row: title left, value right
        var headerGO = new GameObject("Header", typeof(RectTransform));
        headerGO.transform.SetParent(card.transform, false);
        headerGO.AddComponent<LayoutElement>().preferredHeight = 24;
        var hl = headerGO.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = false;
        hl.childAlignment = TextAnchor.MiddleLeft;

        IconType graphIcon = title.Contains("ALTITUDE") ? IconType.Altitude
            : title.Contains("SPEED") ? IconType.Speed
            : title.Contains("BATTERY") ? IconType.Battery
            : IconType.Telemetry;
        CreateIcon(headerGO.transform, graphIcon, 16f, AERO_ACCENT);

        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(headerGO.transform, false);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = title;
        titleTMP.fontSize = 11;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.color = AERO_TEXT_SEC;
        titleTMP.alignment = TextAlignmentOptions.MidlineLeft;
        // letter spacing for aerospace label
        titleTMP.characterSpacing = 5f;
        titleGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        var valueGO = new GameObject("Value", typeof(RectTransform));
        valueGO.transform.SetParent(headerGO.transform, false);
        var valueTMP = valueGO.AddComponent<TextMeshProUGUI>();
        valueTMP.text = "-- " + unit;
        valueTMP.fontSize = 14.5f;
        valueTMP.fontStyle = FontStyles.Bold;
        valueTMP.color = AERO_NUM;
        valueTMP.alignment = TextAlignmentOptions.MidlineRight;
        valueTextOut = valueTMP;
        titleTextOut = titleTMP;

        // Graph area
        var graphGO = new GameObject("Graph", typeof(RectTransform), typeof(Image));
        graphGO.transform.SetParent(card.transform, false);
        graphGO.AddComponent<LayoutElement>().flexibleHeight = 1;
        var graphBg = graphGO.GetComponent<Image>();
        graphBg.color = new Color(AERO_BG.r, AERO_BG.g, AERO_BG.b, 0.55f);
        graphBg.raycastTarget = false;

        var innerGraphGO = new GameObject("GraphPlot", typeof(RectTransform), typeof(CanvasRenderer));
        innerGraphGO.transform.SetParent(graphGO.transform, false);
        var rt = innerGraphGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(5, 4); rt.offsetMax = new Vector2(-5, -4);
        var graph = innerGraphGO.AddComponent<TelemetryGraph>();
        graph.lineColor = accent;
        graph.fillColor = new Color(accent.r, accent.g, accent.b, 0.11f);
        graph.gridColor = new Color(0.18f, 0.54f, 0.64f, 0.16f);
        graph.thickness = 2.1f;
        graph.raycastTarget = false;

        var axisGO = new GameObject("TimeAxis", typeof(RectTransform));
        axisGO.transform.SetParent(card.transform, false);
        axisGO.AddComponent<LayoutElement>().preferredHeight = 10;
        string[] labels = { "-60s", "-45s", "-30s", "-15s", "0s" };
        for (int i = 0; i < labels.Length; i++)
        {
            var labelGO = new GameObject(labels[i], typeof(RectTransform));
            labelGO.transform.SetParent(axisGO.transform, false);
            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text = labels[i];
            label.fontSize = 8.5f;
            label.color = AERO_TEXT_DIM;
            label.alignment = i == 0 ? TextAlignmentOptions.MidlineLeft
                : i == labels.Length - 1 ? TextAlignmentOptions.MidlineRight
                : TextAlignmentOptions.Center;
            label.raycastTarget = false;
            var labelRT = label.rectTransform;
            float anchor = i / (float)(labels.Length - 1);
            labelRT.anchorMin = labelRT.anchorMax = new Vector2(anchor, 0.5f);
            labelRT.pivot = new Vector2(anchor, 0.5f);
            labelRT.anchoredPosition = Vector2.zero;
            labelRT.sizeDelta = new Vector2(34, 10);
        }

        return graph;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Small shared layout helpers
    // ═════════════════════════════════════════════════════════════════════

    static void Center(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }
}
