using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SkinSwap : MonoBehaviour
{
    [Tooltip("Texture to use as the skin atlas. Sprite rects must match the source sprites for correct mapping.")]
    public Texture2D skinTexture;

    [Tooltip("When true the component will apply the skin in LateUpdate so animations that set Sprite still play but are overridden.")]
    public bool applyDuringAnimation = true;

    SpriteRenderer _sr;

    // Cache: skinTextureInstanceId -> (sourceSpriteInstanceId -> createdSprite)
    readonly Dictionary<int, Dictionary<int, Sprite>> _cache = new Dictionary<int, Dictionary<int, Sprite>>();

    // Last applied keys to avoid redundant work
    int _lastSkinId;
    int _lastSourceId;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
    }

    void OnValidate()
    {
        // Clear cache when user changes skin in inspector so new sprites will be recreated.
        _cache.Clear();
        _lastSkinId = 0;
        _lastSourceId = 0;
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
    }

    void LateUpdate()
    {
        if (!applyDuringAnimation && !Application.isPlaying) return;
        if (skinTexture == null) return;
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();

        var src = _sr.sprite;
        if (src == null) return;

        int skinId = skinTexture.GetInstanceID();
        int srcId = src.GetInstanceID();

        if (_lastSkinId == skinId && _lastSourceId == srcId)
            return;

        _lastSkinId = skinId;
        _lastSourceId = srcId;

        Sprite skinned = GetOrCreateSkinnedSprite(skinTexture, src);
        if (skinned != null)
            _sr.sprite = skinned;
    }

    Sprite GetOrCreateSkinnedSprite(Texture2D skin, Sprite source)
    {
        if (skin == null || source == null) return null;

        int skinId = skin.GetInstanceID();
        int srcId = source.GetInstanceID();

        if (!_cache.TryGetValue(skinId, out var inner))
        {
            inner = new Dictionary<int, Sprite>();
            _cache[skinId] = inner;
        }

        if (inner.TryGetValue(srcId, out var cached) && cached != null)
            return cached;

        // Create a new Sprite that maps the source sprite rect onto the provided skin texture.
        Rect srcRect = source.rect;
        Vector2 pivot = new Vector2(source.pivot.x / srcRect.width, source.pivot.y / srcRect.height);

        // Ensure rect fits in skin texture
        if (srcRect.x + srcRect.width > skin.width || srcRect.y + srcRect.height > skin.height)
        {
            Debug.LogWarning($"SkinSwap: source sprite rect {srcRect} is outside skin texture '{skin.name}' bounds.");
            return null;
        }

        Sprite created = Sprite.Create(skin, new Rect(srcRect.x, srcRect.y, srcRect.width, srcRect.height), pivot, source.pixelsPerUnit, 0, SpriteMeshType.Tight, source.border);
        created.name = source.name + "_skin_" + skin.name;
        inner[srcId] = created;
        return created;
    }

    /// <summary>
    /// Clear generated sprites and cache (call when disposing or changing skin atlases at runtime).
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _lastSkinId = 0;
        _lastSourceId = 0;
    }
}

