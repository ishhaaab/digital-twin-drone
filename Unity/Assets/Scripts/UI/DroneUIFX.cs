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
//   - Glowing pill badge   (armed / disarmed)
//   - Flight-mode cards    (glow when active)
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

    public static Sprite CircleSprite =>
        _circleSprite ??= Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");

    public static Sprite RoundedRectSprite =>
        _roundedRectSprite ??= Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");

    // ── Shared colour ramps ──────────────────────────────────────────────
    public static readonly Color COL_GREEN  = new Color(0.20f, 0.90f, 0.50f);
    public static readonly Color COL_YELLOW = new Color(1.00f, 0.85f, 0.20f);
    public static readonly Color COL_ORANGE = new Color(1.00f, 0.55f, 0.10f);
    public static readonly Color COL_RED    = new Color(1.00f, 0.25f, 0.25f);
    public static readonly Color COL_CYAN   = new Color(0.25f, 0.85f, 0.95f);
    public static readonly Color COL_AMBER  = new Color(1.00f, 0.70f, 0.15f);
    public static readonly Color COL_EMERALD= new Color(0.10f, 0.95f, 0.55f);

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
        if (_animator == null)
        {
            _animator = canvasRoot.GetComponent<UIFXAnimator>();
            if (_animator == null)
                _animator = canvasRoot.gameObject.AddComponent<UIFXAnimator>();
        }
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
    // 2. Glowing pill badge  (armed / disarmed)
    // ═════════════════════════════════════════════════════════════════════

    public class GlowBadgeFX : IFxWidget
    {
        public Image bg;
        public Image glow;
        public TextMeshProUGUI label;
        public Color onColor = COL_EMERALD;     // per spec: emerald when ARMED
        public Color offColor = COL_AMBER;      // per spec: amber/red when DISARMED
        public string onText = "ARMED";
        public string offText = "DISARMED";
        bool state;
        float phase;

        public void SetState(bool armed) => state = armed;

        public void Tick(float dt)
        {
            phase += dt * (state ? 3.2f : 0.8f);
            float pulse = 0.55f + 0.45f * Mathf.Sin(phase * Mathf.PI);
            Color c = state ? onColor : offColor;
            if (bg != null) bg.color = Color.Lerp(bg.color, c, dt * 8f);
            if (glow != null) glow.color = new Color(c.r, c.g, c.b, pulse * (state ? 0.55f : 0.30f));
            if (label != null)
            {
                string wanted = state ? onText : offText;
                if (label.text != wanted) label.text = wanted;
                label.color = Color.white;
            }
        }
    }

    public static GlowBadgeFX CreateGlowBadge(Transform parent, string name, float width, float height)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;

        var glowGO = new GameObject("Glow", typeof(RectTransform), typeof(Image));
        glowGO.transform.SetParent(root.transform, false);
        var glowImg = glowGO.GetComponent<Image>();
        glowImg.sprite = RoundedRectSprite;
        glowImg.type = Image.Type.Sliced;
        glowImg.color = new Color(0, 1, 0.5f, 0.3f);
        glowImg.raycastTarget = false;
        var glowRT = (RectTransform)glowGO.transform;
        glowRT.sizeDelta = new Vector2(width * 1.18f, height * 1.5f);
        Center(glowRT);

        var bgGO = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(root.transform, false);
        var bgImg = bgGO.GetComponent<Image>();
        bgImg.sprite = RoundedRectSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = COL_EMERALD;
        bgImg.raycastTarget = false;
        Stretch((RectTransform)bgGO.transform, 0f);

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(root.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = "DISARMED";
        lbl.fontSize = 20;
        lbl.fontStyle = FontStyles.Bold;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.color = Color.white;
        Stretch(lbl.rectTransform, 0f);

        return new GlowBadgeFX { bg = bgImg, glow = glowImg, label = lbl };
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. Flight-mode glow cards
    // ═════════════════════════════════════════════════════════════════════

    public class FlightModeCardsFX : IFxWidget
    {
        class Card { public Image bg, glow; public TextMeshProUGUI icon, label; public float glowAlpha; }
        readonly Dictionary<string, Card> cards = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
        string activeKey = "";

        public void AddCard(string key, Image bg, Image glow, TextMeshProUGUI icon, TextMeshProUGUI label)
            => cards[key] = new Card { bg = bg, glow = glow, icon = icon, label = label };

        public void SetActiveMode(string flightMode)
        {
            if (string.IsNullOrEmpty(flightMode)) { activeKey = ""; return; }
            activeKey = flightMode.Replace("_", "").Replace(" ", "").ToUpperInvariant();
        }

        public void Tick(float dt)
        {
            foreach (var kv in cards)
            {
                bool active = string.Equals(kv.Key.Replace("_", "").Replace(" ", ""), activeKey, StringComparison.OrdinalIgnoreCase);
                var c = kv.Value;
                float target = active ? 1f : 0f;
                c.glowAlpha = Mathf.Lerp(c.glowAlpha, target, dt * 8f);
                if (c.glow != null)
                {
                    var baseCol = c.glow.color;
                    c.glow.color = new Color(baseCol.r, baseCol.g, baseCol.b, 0.55f * c.glowAlpha);
                }
                if (c.bg != null)
                    c.bg.color = Color.Lerp(new Color(0.11f, 0.15f, 0.22f), new Color(0.16f, 0.24f, 0.34f), c.glowAlpha);
                if (c.icon != null) c.icon.color = Color.Lerp(new Color(0.5f, 0.58f, 0.68f), Color.white, c.glowAlpha);
                if (c.label != null) c.label.color = Color.Lerp(new Color(0.5f, 0.58f, 0.68f), Color.white, c.glowAlpha);
            }
        }
    }

    /// Creates one small glow-card for a flight mode inside `parent` (expects a
    /// HorizontalLayoutGroup or GridLayoutGroup already on parent). Call once per mode.
    public static void CreateFlightModeCard(FlightModeCardsFX set, Transform parent, string key,
        string glyph, string label, Color accent)
    {
        var root = new GameObject(key + "Card", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = 76;
        le.preferredHeight = 70;
        le.flexibleWidth = 1;

        var glowGO = new GameObject("Glow", typeof(RectTransform), typeof(Image));
        glowGO.transform.SetParent(root.transform, false);
        var glowImg = glowGO.GetComponent<Image>();
        glowImg.sprite = RoundedRectSprite;
        glowImg.type = Image.Type.Sliced;
        glowImg.color = new Color(accent.r, accent.g, accent.b, 0f);
        glowImg.raycastTarget = false;
        var glowRT = (RectTransform)glowGO.transform;
        Stretch(glowRT, -6f);

        var bgGO = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(root.transform, false);
        var bgImg = bgGO.GetComponent<Image>();
        bgImg.sprite = RoundedRectSprite;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(0.11f, 0.15f, 0.22f);
        bgImg.raycastTarget = false;
        Stretch((RectTransform)bgGO.transform, 0f);

        var vlg = root.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 1;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(root.transform, false);
        var iconT = iconGO.AddComponent<TextMeshProUGUI>();
        iconT.text = glyph;
        iconT.fontSize = 30;
        iconT.alignment = TextAlignmentOptions.Center;
        iconT.color = new Color(0.5f, 0.58f, 0.68f);

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(root.transform, false);
        var lblT = lblGO.AddComponent<TextMeshProUGUI>();
        lblT.text = label;
        lblT.fontSize = 13;
        lblT.fontStyle = FontStyles.Bold;
        lblT.alignment = TextAlignmentOptions.Center;
        lblT.color = new Color(0.5f, 0.58f, 0.68f);

        set.AddCard(key, bgImg, glowImg, iconT, lblT);
    }

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
        row.AddComponent<LayoutElement>().preferredHeight = 40;

        var vlg = row.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(0, 0, 2, 2);

        var headerGO = new GameObject("Header", typeof(RectTransform));
        headerGO.transform.SetParent(row.transform, false);
        headerGO.AddComponent<LayoutElement>().preferredHeight = 20;
        var hl = headerGO.AddComponent<HorizontalLayoutGroup>();
        hl.childForceExpandWidth = true;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(headerGO.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 15;
        lbl.fontStyle = FontStyles.Bold;
        lbl.color = new Color(0.6f, 0.72f, 0.85f);
        lbl.alignment = TextAlignmentOptions.MidlineLeft;

        var valGO = new GameObject("Value", typeof(RectTransform));
        valGO.transform.SetParent(headerGO.transform, false);
        var val = valGO.AddComponent<TextMeshProUGUI>();
        val.text = "0.00";
        val.fontSize = 17;
        val.fontStyle = FontStyles.Bold;
        val.color = Color.white;
        val.alignment = TextAlignmentOptions.MidlineRight;

        var trackGO = new GameObject("Track", typeof(RectTransform), typeof(Image));
        trackGO.transform.SetParent(row.transform, false);
        trackGO.AddComponent<LayoutElement>().preferredHeight = 12;
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

    /// Builds a glassmorphism heading tape: a masked strip with degree ticks that scrolls as
    /// the drone yaws, with a fixed centre pointer marking the current heading.
    public static CompassRibbonFX CreateCompassRibbon(Transform parent, float width, float height)
    {
        const float pxPerDeg = 7f;

        var root = new GameObject("CompassRibbon", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;

        // Glass background
        var glassGO = new GameObject("Glass", typeof(RectTransform), typeof(Image));
        glassGO.transform.SetParent(root.transform, false);
        var glassImg = glassGO.GetComponent<Image>();
        glassImg.sprite = RoundedRectSprite;
        glassImg.type = Image.Type.Sliced;
        glassImg.color = new Color(1f, 1f, 1f, 0.06f); // frosted-glass look: light, translucent
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
        for (int deg = -360; deg <= 720; deg += 30)
        {
            var tickGO = new GameObject($"Tick_{deg}", typeof(RectTransform));
            tickGO.transform.SetParent(contentGO.transform, false);
            var t = tickGO.AddComponent<TextMeshProUGUI>();
            int norm = ((deg % 360) + 360) % 360;
            t.text = (norm % 90 == 0) ? dirs[norm / 90] : (norm.ToString());
            t.fontSize = (norm % 90 == 0) ? 22 : 15;
            t.fontStyle = (norm % 90 == 0) ? FontStyles.Bold : FontStyles.Normal;
            t.color = (norm % 90 == 0) ? COL_CYAN : new Color(0.7f, 0.78f, 0.9f, 0.8f);
            t.alignment = TextAlignmentOptions.Center;
            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(52, height);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(deg * pxPerDeg, 0);
        }

        // Fixed centre pointer
        var ptrGO = new GameObject("Pointer", typeof(RectTransform), typeof(Image));
        ptrGO.transform.SetParent(root.transform, false);
        var ptrImg = ptrGO.GetComponent<Image>();
        ptrImg.color = COL_EMERALD;
        var ptrRT = (RectTransform)ptrGO.transform;
        ptrRT.anchorMin = new Vector2(0.5f, 0f);
        ptrRT.anchorMax = new Vector2(0.5f, 1f);
        ptrRT.sizeDelta = new Vector2(2, 0);
        ptrRT.anchoredPosition = Vector2.zero;

        return new CompassRibbonFX { tapeContent = contentRT, pixelsPerDegree = pxPerDeg };
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5. Artificial horizon
    // ═════════════════════════════════════════════════════════════════════

    public class HorizonFX : IFxWidget
    {
        public RectTransform horizonPlate; // rotates with roll, translates with pitch
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
        }
    }

    /// Builds a compact circular attitude indicator: sky/ground half-planes that rotate with
    /// roll and slide with pitch, a fixed aircraft symbol, and a circular mask/bezel.
    public static HorizonFX CreateArtificialHorizon(Transform parent, float diameter)
    {
        var root = new GameObject("ArtificialHorizon", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth = diameter;
        le.preferredHeight = diameter;

        // Bezel glow
        var glowGO = new GameObject("Glow", typeof(RectTransform), typeof(Image));
        glowGO.transform.SetParent(root.transform, false);
        var glowImg = glowGO.GetComponent<Image>();
        glowImg.sprite = CircleSprite;
        glowImg.color = new Color(0.3f, 0.65f, 1f, 0.18f);
        glowImg.raycastTarget = false;
        var glowRT = (RectTransform)glowGO.transform;
        glowRT.sizeDelta = new Vector2(diameter * 1.25f, diameter * 1.25f);
        Center(glowRT);

        // Circular mask/viewport
        var vpGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        vpGO.transform.SetParent(root.transform, false);
        vpGO.GetComponent<Image>().sprite = CircleSprite;
        vpGO.GetComponent<Image>().color = Color.white;
        vpGO.GetComponent<Mask>().showMaskGraphic = true;
        var vpRT = (RectTransform)vpGO.transform;
        vpRT.sizeDelta = new Vector2(diameter, diameter);
        Center(vpRT);

        // Plate (oversized so pitch/roll never reveals empty corners)
        var plateGO = new GameObject("Plate", typeof(RectTransform));
        plateGO.transform.SetParent(vpGO.transform, false);
        var plateRT = (RectTransform)plateGO.transform;
        plateRT.sizeDelta = new Vector2(diameter * 2f, diameter * 2f);
        plateRT.anchorMin = plateRT.anchorMax = new Vector2(0.5f, 0.5f);
        plateRT.anchoredPosition = Vector2.zero;

        var skyGO = new GameObject("Sky", typeof(RectTransform), typeof(Image));
        skyGO.transform.SetParent(plateGO.transform, false);
        skyGO.GetComponent<Image>().color = new Color(0.20f, 0.45f, 0.85f);
        var skyRT = (RectTransform)skyGO.transform;
        skyRT.anchorMin = new Vector2(0, 0.5f);
        skyRT.anchorMax = new Vector2(1, 1f);
        skyRT.offsetMin = skyRT.offsetMax = Vector2.zero;

        var groundGO = new GameObject("Ground", typeof(RectTransform), typeof(Image));
        groundGO.transform.SetParent(plateGO.transform, false);
        groundGO.GetComponent<Image>().color = new Color(0.35f, 0.24f, 0.12f);
        var groundRT = (RectTransform)groundGO.transform;
        groundRT.anchorMin = new Vector2(0, 0f);
        groundRT.anchorMax = new Vector2(1, 0.5f);
        groundRT.offsetMin = groundRT.offsetMax = Vector2.zero;

        var lineGO = new GameObject("HorizonLine", typeof(RectTransform), typeof(Image));
        lineGO.transform.SetParent(plateGO.transform, false);
        lineGO.GetComponent<Image>().color = Color.white;
        var lineRT = (RectTransform)lineGO.transform;
        lineRT.anchorMin = new Vector2(0, 0.5f);
        lineRT.anchorMax = new Vector2(1, 0.5f);
        lineRT.sizeDelta = new Vector2(0, 2);

        // Fixed aircraft symbol (drawn on root, not on the rotating plate)
        var symGO = new GameObject("AircraftSymbol", typeof(RectTransform), typeof(Image));
        symGO.transform.SetParent(root.transform, false);
        symGO.GetComponent<Image>().color = COL_YELLOW;
        var symRT = (RectTransform)symGO.transform;
        symRT.sizeDelta = new Vector2(diameter * 0.5f, 3);
        Center(symRT);

        return new HorizonFX { horizonPlate = plateRT };
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