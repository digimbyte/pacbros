using System;
using UnityEngine;

public class Score : MonoBehaviour
{
    private static int s_score = 0;

    [Header("Display")]
    [Tooltip("Assign the Nova Text Mesh Pro TextBlock component here (any Component is accepted; reflection will set its text).")]
    public Component novaTextBlock;

    [Header("Formatting")]
    [Tooltip("Number of digits to pad with leading zeros (max 6).")]
    public int padding = 6;

    [Tooltip("Maximum allowed score (will be clamped). Max allowed is 999999.")]
    public int maxScore = 999999;

    // Static copies used by the static API so instance inspector values control runtime clamping/formatting.
    private static int s_padding = 6;
    private static int s_maxScore = 999999;

    /// <summary>
    /// Read-only static accessor for the current score.
    /// </summary>
    public static int Value => s_score;

    /// <summary>
    /// Event invoked whenever the score changes (new total passed).
    /// </summary>
    public static event Action<int> OnScoreChanged;

    private void OnEnable()
    {
        OnScoreChanged += HandleScoreChanged;
        // Ensure inspector fields are validated and display is synced
        ValidateInspectorValues();
        // sync instance inspector values to static backing fields
        s_padding = Mathf.Clamp(padding, 0, 6);
        s_maxScore = Mathf.Clamp(maxScore, 0, 999999);
        UpdateDisplayImmediate();
    }

    private void OnDisable()
    {
        OnScoreChanged -= HandleScoreChanged;
    }

    private void OnValidate()
    {
        // Keep editor values in sensible ranges
        padding = Mathf.Clamp(padding, 0, 6);
        maxScore = Mathf.Clamp(maxScore, 0, 999999);
        // sync to static backing so runtime static API uses inspector limits
        s_padding = Mathf.Clamp(padding, 0, 6);
        s_maxScore = Mathf.Clamp(maxScore, 0, 999999);
        // Clamp current runtime score to new max
        if (s_score > s_maxScore)
        {
            s_score = s_maxScore;
            OnScoreChanged?.Invoke(s_score);
        }
    }

    private static void ValidateInspectorValues()
    {
        // no-op for now; instance OnValidate handles clamping
    }

    private void HandleScoreChanged(int newScore)
    {
        UpdateDisplayImmediate();
    }

    /// <summary>
    /// Add points to the global score. Amount should be positive.
    /// </summary>
    public static void AddPoints(int amount)
    {
        if (amount <= 0) return;
        // clamp to maximum (use static backing)
        s_score = Mathf.Clamp(s_score + amount, 0, s_maxScore);
        OnScoreChanged?.Invoke(s_score);
    }

    /// <summary>
    /// Remove points from the global score. Amount should be positive.
    /// Score will not go below zero.
    /// </summary>
    public static void RemovePoints(int amount)
    {
        if (amount <= 0) return;
        s_score = Mathf.Max(0, s_score - amount);
        OnScoreChanged?.Invoke(s_score);
    }

    /// <summary>
    /// Reset the score to zero. Useful for round restarts.
    /// </summary>
    public static void ResetScore()
    {
        s_score = 0;
        OnScoreChanged?.Invoke(s_score);
    }

    private void UpdateDisplayImmediate()
    {
        string formatted = FormatScore(s_score, padding);

        // Try Nova Text Mesh Pro block first (component provided)
        if (novaTextBlock != null)
        {
            if (TrySetTextOnNovaBlock(novaTextBlock, formatted))
                return;
        }
    }

    private string FormatScore(int value, int pad)
    {
        int p = Mathf.Clamp(pad, 0, 6);
        // Ensure value does not exceed configured max
        int v = Mathf.Clamp(value, 0, s_maxScore);
        return v.ToString().PadLeft(p, '0');
    }

    private bool TrySetTextOnNovaBlock(Component block, string text)
    {
        try
        {
            var blkType = block.GetType();
            var bodyProp = blkType.GetProperty("Body");
            object body = bodyProp?.GetValue(block);
            if (body == null)
            {
                // Try direct Text property on block
                var directProp = blkType.GetProperty("Text") ?? blkType.GetProperty("text");
                if (directProp != null && directProp.PropertyType == typeof(string))
                {
                    directProp.SetValue(block, text);
                    return true;
                }
                return false;
            }

            var bodyType = body.GetType();
            var textProp = bodyType.GetProperty("Text") ?? bodyType.GetProperty("text") ?? bodyType.GetProperty("Value");
            if (textProp != null && textProp.PropertyType == typeof(string))
            {
                textProp.SetValue(body, text);
                return true;
            }

            // Try to find a field
            var textField = bodyType.GetField("Text") ?? bodyType.GetField("text") ?? bodyType.GetField("Value");
            if (textField != null && textField.FieldType == typeof(string))
            {
                textField.SetValue(body, text);
                return true;
            }
        }
        catch (Exception)
        {
            // ignore reflection failures
        }
        return false;
    }
}
