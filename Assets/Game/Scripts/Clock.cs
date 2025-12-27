using UnityEngine;
using TMPro;
using Nova.TMP;

public class Clock : MonoBehaviour
{
    [Header("Time Tracking")]
    [Tooltip("Current whole seconds elapsed (int)")]
    public int Seconds;

    [Tooltip("Thousandths of a second (0..999)")]
    public int Thousandths;

    [Tooltip("Use unscaled time (ignores Time.timeScale). If false and scaled time is zero, falls back to unscaled time.")]
    public bool useUnscaledTime = true;

    // internal accumulator in fractional milliseconds
    private double msAccumulator = 0.0;

    [Header("TMP Scale Animation")]
    [Tooltip("TextMeshPro TextBlock components to animate the font size of. If empty, will attempt to find one on this GameObject.")]
    public TextMeshProTextBlock[] textComponents;

    [Tooltip("Animation curve mapping ms% (0..1) to multiplier percentage (0..1). Evaluate at Thousandths/1000.")]
    public AnimationCurve scaleCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("Primary base multiplier (example default 4)")]
    public float baseMultiplier = 4f;

    [Tooltip("Secondary multiplier (example default 10). Final delta = baseMultiplier * extraMultiplier * curveValue")]
    public float extraMultiplier = 10f;

    // store original font sizes so we can apply deltas relative to them
    private float[] originalFontSizes;

    private void Start()
    {
        if (textComponents == null || textComponents.Length == 0)
        {
            var t = GetComponent<TextMeshProTextBlock>();
            if (t != null)
                textComponents = new TextMeshProTextBlock[] { t };
        }

        if (textComponents != null && textComponents.Length > 0)
        {
            originalFontSizes = new float[textComponents.Length];
            for (int i = 0; i < textComponents.Length; i++)
            {
                var comp = textComponents[i];
                originalFontSizes[i] = comp != null ? comp.fontSize : 0f;
            }
        }
        else
        {
            originalFontSizes = new float[0];
        }

        // don't forcibly reset Seconds/Thousandths so inspector-start values are preserved; only clear accumulator
        msAccumulator = 0.0;
    }

    private void Update()
    {
        // advance ms accumulator
        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        // If using scaled time and timeScale is zero, fall back to unscaled so the clock still advances.
        if (!useUnscaledTime && Mathf.Approximately(delta, 0f))
        {
            delta = Time.unscaledDeltaTime;
        }
        // convert to milliseconds
        msAccumulator += delta * 1000.0;

        if (msAccumulator >= 1.0)
        {
            int addMs = (int)msAccumulator;
            msAccumulator -= addMs;

            Thousandths += addMs;
            if (Thousandths >= 1000)
            {
                int carry = Thousandths / 1000;
                Seconds += carry;
                Thousandths -= carry * 1000;
            }
        }

        // update TMP scale animation based on current ms percentage
        float msPercent = Mathf.Clamp01(Thousandths / 1000f);
        float curveVal = scaleCurve != null ? scaleCurve.Evaluate(msPercent) : msPercent;
        float deltaSize = baseMultiplier * extraMultiplier * curveVal;

        if (textComponents != null)
        {
            for (int i = 0; i < textComponents.Length; i++)
            {
                var comp = textComponents[i];
                if (comp == null) continue;
                float baseSize = (i < originalFontSizes.Length) ? originalFontSizes[i] : comp.fontSize;
                comp.fontSize = baseSize + deltaSize;
            }
        }

        // Update text values: index 0 = seconds, index 1 = milliseconds (zero-padded to 3 digits)
        if (textComponents != null && textComponents.Length >= 2)
        {
            var s0 = textComponents[0];
            var s1 = textComponents[1];
            if (s0 != null) s0.text = Seconds.ToString();
            if (s1 != null) s1.text = "." + Thousandths.ToString("D3");
        }
    }
}
