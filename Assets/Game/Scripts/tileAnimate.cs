using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class tileAnimate : MonoBehaviour
{
    [Header("Atlas")]
    [Tooltip("Number of columns in the texture atlas.")]
    public int cols = 1;
    [Tooltip("Number of rows in the texture atlas.")]
    public int rows = 1;
    [Tooltip("Starting frame index (0 based).")]
    public int startFrame = 0;

    [Header("Playback")]
    [Tooltip("Frames per second.")]
    public float fps = 10f;
    [Tooltip("If true, animation will loop. If false, it will stop on the last frame.")]
    public bool loop = true;

    [Header("Tint")]
    [Tooltip("Tint color applied to the material's color property (if present).")]
    public Color tint = Color.white;

    Renderer _renderer;
    Material _materialInstance;

    int _totalFrames => Mathf.Max(1, cols * rows);
    int _currentFrame;
    float _accum;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer == null)
        {
            enabled = false;
            return;
        }

        // Create a unique material instance so changing tint/offset doesn't affect other objects.
        _materialInstance = _renderer.material;

        // Clamp values
        cols = Mathf.Max(1, cols);
        rows = Mathf.Max(1, rows);
        fps = Mathf.Max(0.001f, fps);

        // Configure scale so a single cell is visible.
        Vector2 scale = new Vector2(1f / cols, 1f / rows);
        _materialInstance.mainTextureScale = scale;

        // Initialize frame and tint.
        _currentFrame = Mathf.Clamp(startFrame, 0, _totalFrames - 1);
        ApplyFrame(_currentFrame);
        ApplyTint();
    }

    void OnValidate()
    {
        // Keep inspector changes visible in edit mode.
        if (_materialInstance != null)
        {
            cols = Mathf.Max(1, cols);
            rows = Mathf.Max(1, rows);
            _materialInstance.mainTextureScale = new Vector2(1f / cols, 1f / rows);
            ApplyTint();
            ApplyFrame(_currentFrame);
        }
    }

    void Update()
    {
        if (_materialInstance == null) return;

        // Advance time and frames according to fps.
        _accum += Time.deltaTime;
        float secPerFrame = 1f / fps;
        while (_accum >= secPerFrame)
        {
            _accum -= secPerFrame;
            AdvanceFrame();
            if (!loop && _currentFrame == _totalFrames - 1)
            {
                // stop advancing if not looping and reached last frame
                _accum = 0f;
                break;
            }
        }
    }

    void AdvanceFrame()
    {
        _currentFrame++;
        if (_currentFrame >= _totalFrames)
        {
            if (loop) _currentFrame = 0;
            else _currentFrame = _totalFrames - 1;
        }
        ApplyFrame(_currentFrame);
    }

    void ApplyFrame(int frame)
    {
        if (_materialInstance == null) return;
        frame = Mathf.Clamp(frame, 0, _totalFrames - 1);
        int col = frame % cols;
        int row = frame / cols; // 0 = top row? Unity's UV origin is lower-left so invert row

        // Convert to texture offset. We want row 0 to be top, so flip Y.
        float x = (float)col / cols;
        float y = 1f - (float)(row + 1) / rows;
        _materialInstance.mainTextureOffset = new Vector2(x, y);
    }

    void ApplyTint()
    {
        if (_materialInstance == null) return;
        if (_materialInstance.HasProperty("_Color"))
            _materialInstance.color = tint;
    }
}
