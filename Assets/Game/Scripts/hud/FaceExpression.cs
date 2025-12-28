using System;
using UnityEngine;
using UnityEngine.UI;
using Nova;

public class FaceExpression : MonoBehaviour
{
    public enum HeatCategory { Cold = 0, Normal = 1, Hot = 2, Dead = 3 }
    public enum DirIndex { Up = 0, Down = 1, Left = 2, Right = 3 }

    [Header("Targets")]
    public UIBlock2D block;
    public RawImage rawImage; // fallback if Nova block doesn't expose a simple texture slot

    [Header("Sources")]
    public PlayerController playerController;

    [Header("Timing")]
    [Tooltip("How often (seconds) to update the face texture.")]
    public float updateInterval = 1f;

    [Header("Heat Mapping")]
    [Tooltip("Maximum stage index (inclusive) for Cold. Stages beyond map into subsequent categories.")]
    public int coldMaxStage = 1;
    public int normalMaxStage = 4;
    public int hotMaxStage = 7;

    [Serializable]
    public struct DirectionalFaces
    {
        public Texture2D Up;
        public Texture2D Down;
        public Texture2D Left;
        public Texture2D Right;

        public Texture2D GetForDir(DirIndex d)
        {
            switch (d)
            {
                case DirIndex.Up: return Up;
                case DirIndex.Down: return Down;
                case DirIndex.Left: return Left;
                case DirIndex.Right: return Right;
                default: return Down;
            }
        }
    }

    [Header("Face Textures")]
    [Tooltip("Assign directional textures per heat category. If a direction is empty, the Down texture will be used as fallback.")]
    public DirectionalFaces Cold;
    public DirectionalFaces Normal;
    public DirectionalFaces Fire;
    [Tooltip("Optional; if empty, Fire will be used for Dead.")]
    public DirectionalFaces Dead;

    private float _timer = 0f;
    private DirIndex _lastDir = DirIndex.Down;

    private void Reset()
    {
        // try to auto-wire common fallbacks
        if (playerController == null)
            playerController = FindObjectOfType<PlayerController>();
    }

    private void Update()
    {
        if (!gameObject.activeInHierarchy) return;

        _timer += Time.unscaledDeltaTime;
        if (_timer >= Mathf.Max(0.001f, updateInterval))
        {
            _timer = 0f;
            ApplyFace();
        }
    }

    private void ApplyFace()
    {
        int stage = Heat.GetStage();
        HeatCategory cat = MapStageToCategory(stage);

        DirIndex dir = DetermineDirection();
        _lastDir = dir;

        DirectionalFaces faces = GetFacesForCategory(cat);
        Texture2D tex = faces.GetForDir(dir) ?? faces.Down;
        if (tex == null) return;

        if (rawImage != null)
        {
            rawImage.texture = tex;
            return;
        }

        if (block != null)
        {
            TrySetTextureOnBlock(block, tex);
        }
    }

    private HeatCategory MapStageToCategory(int stage)
    {
        if (stage <= coldMaxStage) return HeatCategory.Cold;
        if (stage <= normalMaxStage) return HeatCategory.Normal;
        if (stage <= hotMaxStage) return HeatCategory.Hot;
        return HeatCategory.Dead;
    }

    private DirIndex DetermineDirection()
    {
        if (playerController == null) return _lastDir;
        Vector2 input = playerController.currentInput;
        if (input.sqrMagnitude < 0.01f) return _lastDir;

        if (Mathf.Abs(input.x) > Mathf.Abs(input.y))
        {
            return input.x < 0f ? DirIndex.Left : DirIndex.Right;
        }
        else
        {
            return input.y < 0f ? DirIndex.Down : DirIndex.Up;
        }
    }

    private void TrySetTextureOnBlock(UIBlock2D b, Texture2D tex)
    {
        try
        {
            var blkType = b.GetType();
            var bodyProp = blkType.GetProperty("Body");
            object body = bodyProp?.GetValue(b);
            if (body == null) return;

            var bodyType = body.GetType();
            var imageProp = bodyType.GetProperty("Image");
            object image = imageProp?.GetValue(body);
            if (image == null) return;

            var imgType = image.GetType();

            var texProp = imgType.GetProperty("Texture") ?? imgType.GetProperty("texture");
            if (texProp != null && texProp.PropertyType.IsAssignableFrom(typeof(Texture)))
            {
                texProp.SetValue(image, tex);
                return;
            }

            var spriteProp = imgType.GetProperty("Sprite") ?? imgType.GetProperty("sprite");
            if (spriteProp != null && spriteProp.PropertyType == typeof(Sprite))
            {
                Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                spriteProp.SetValue(image, s);
                return;
            }

            // some Nova internals might expose a nested struct/field; try to find a Texture field anywhere on image by name
            var texField = imgType.GetField("Texture") ?? imgType.GetField("texture");
            if (texField != null && texField.FieldType.IsAssignableFrom(typeof(Texture)))
            {
                texField.SetValue(image, tex);
                return;
            }
        }
        catch (Exception)
        {
            // Swallow errors — reflection best-effort only.
        }
    }

    private DirectionalFaces GetFacesForCategory(HeatCategory cat)
    {
        switch (cat)
        {
            case HeatCategory.Cold: return Cold;
            case HeatCategory.Normal: return Normal;
            case HeatCategory.Hot: return Fire;
            case HeatCategory.Dead:
                // Dead falls back to Dead if assigned, otherwise Fire
                // if Dead.Up/Down/etc are all null then Fire will be used by caller if needed
                return Dead.Up != null || Dead.Down != null || Dead.Left != null || Dead.Right != null ? Dead : Fire;
            default: return Normal;
        }
    }
}
