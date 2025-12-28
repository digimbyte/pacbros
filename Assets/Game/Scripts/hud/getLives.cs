using UnityEngine;

public class getScore : MonoBehaviour
{
    [Tooltip("Optional explicit LevelRuntime to read lives from. If null, LevelRuntime.Active will be used.")]
    public LevelRuntime runtime;

    [Tooltip("Nova Text Mesh Pro TextBlock component to write the lives value to. Use the TextBlock component instance here.")]
    public Component novaTextBlock;

    private int _lastLives = int.MinValue;

    void Reset()
    {
        if (runtime == null)
            runtime = LevelRuntime.Active;
    }

    void OnEnable()
    {
        if (runtime == null)
            runtime = LevelRuntime.Active;
        RefreshImmediate();
    }

    void Update()
    {
        var lr = runtime != null ? runtime : LevelRuntime.Active;
        if (lr == null) return;

        int lives = lr.currentLives;
        if (lives != _lastLives)
        {
            _lastLives = lives;
            string text = lives.ToString();
            TrySetTextOnNovaBlock(novaTextBlock, text);
        }
    }

    private void RefreshImmediate()
    {
        var lr = runtime != null ? runtime : LevelRuntime.Active;
        if (lr == null) return;
        _lastLives = lr.currentLives - 1; // force update
        Update();
    }

    private bool TrySetTextOnNovaBlock(Component block, string text)
    {
        if (block == null) return false;
        try
        {
            var blkType = block.GetType();
            var bodyProp = blkType.GetProperty("Body");
            object body = bodyProp?.GetValue(block);
            if (body == null)
            {
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

            var textField = bodyType.GetField("Text") ?? bodyType.GetField("text") ?? bodyType.GetField("Value");
            if (textField != null && textField.FieldType == typeof(string))
            {
                textField.SetValue(body, text);
                return true;
            }
        }
        catch (System.Exception)
        {
            // swallow reflection errors
        }
        return false;
    }
}
