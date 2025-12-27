using System;
using System.Reflection;
using UnityEngine;

[AddComponentMenu("Animation/Universal Property Animator")]
public class UniversalPropertyAnimator : MonoBehaviour
{
    public enum TargetKind
    {
        ComponentMember,    // any field/property on a Component by name
        MaterialProperty,   // shader property on a Renderer material
        TransformPosition,  // position of a Transform
        TransformRotation   // rotation of a Transform
    }

    public enum ValueType
    {
        Float,
        Vector3,
        Quaternion,
        Color
    }

    public enum InterpMode
    {
        Lerp,
        Slerp // only meaningful for Quaternion, ignored otherwise
    }

    public enum PlayMode
    {
        Once,
        Loop,
        PingPong
    }

    [Header("Target")]
    public TargetKind targetKind = TargetKind.ComponentMember;

    [Tooltip("Component that owns the member to animate, or the Transform to move/rotate.")]
    public Component targetComponent;

    [Tooltip("Field or property name on the component for ComponentMember mode.")]
    public string memberName;

    [Tooltip("Renderer whose material property will be animated.")]
    public Renderer targetRenderer;

    [Tooltip("Index of material on the renderer.")]
    public int materialIndex = 0;

    [Tooltip("Shader property name (e.g. _Color, _Intensity).")]
    public string shaderProperty;

    [Tooltip("How we treat the value being animated.")]
    public ValueType valueType = ValueType.Float;

    [Tooltip("Lerp or Slerp (only affects Quaternions).")]
    public InterpMode interpMode = InterpMode.Lerp;

    [Header("Transform Options")]
    [Tooltip("Use local space for Transform position/rotation targets.")]
    public bool useLocalSpace = true;

    [Header("Timing")]
    [Min(0.0001f)]
    public float duration = 1f;

    public PlayMode playMode = PlayMode.Loop;

    [Tooltip("Automatically play when enabled.")]
    public bool playOnEnable = true;

    [Tooltip("Use unscaled time (ignores Time.timeScale).")]
    public bool useUnscaledTime = false;

    [Header("Validation")]
    [Tooltip("Run HealthCheck automatically in Awake to validate bindings and log detailed errors.")]
    public bool runHealthCheckOnAwake = false;

    [Header("Curve")]
    [Tooltip("Remaps normalized time (0-1) to an eased value.")]
    public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("From / To Values")]
    public float fromFloat;
    public float toFloat;

    public Vector3 fromVector;
    public Vector3 toVector;

    public Quaternion fromQuaternion = Quaternion.identity;
    public Quaternion toQuaternion = Quaternion.identity;

    public Color fromColor = Color.white;
    public Color toColor = Color.black;

    [Header("Noise")]
    [Tooltip("Enable procedural noise on top of the base interpolation.")]
    public bool useNoise = false;

    [Tooltip("Max multiplicative deviation from base value, e.g. 0.1 = ±10%.")]
    [Range(0f, 1f)]
    public float noisePercent = 0.1f;

    [Tooltip("How fast the noise changes over time.")]
    public float noiseSpeed = 1f;

    [Tooltip("Seed for noise so multiple instances can be decorrelated.")]
    public int noiseSeed = 0;

    [Tooltip("For Vector3/Color: use separate noise per component.")]
    public bool noisePerComponent = true;

    // Internal state
    float _elapsed;
    bool _playing;

    // Reflection cache
    FieldInfo _fieldInfo;
    PropertyInfo _propertyInfo;
    // Support for dotted member paths (e.g. "visuals.Image.Adjustment.CenterUV.y")
    MemberInfo[] _memberInfos;
    string _componentSelector; // e.g. "x","y","z","w","r","g","b","a"
    // Resolved member type for the final token (if available)
    Type _resolvedMemberType;
    // Exposed for editor/debugging
    public string resolvedMemberTypeName;

    // Shader cache
    MaterialPropertyBlock _mpb;
    int _shaderId;
    bool _shaderIdValid;

    void Awake()
    {
        CacheComponentMember();
        CacheShaderProperty();

        if (runHealthCheckOnAwake)
        {
            HealthCheck();
        }
    }

    void OnEnable()
    {
        if (playOnEnable)
        {
            Play();
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Keep caches fresh when values change in the Inspector
        CacheComponentMember();
        CacheShaderProperty();
    }
#endif

    /// <summary>Start (or restart) the animation from t=0.</summary>
    public void Play()
    {
        _elapsed = 0f;
        _playing = true;
    }

    /// <summary>Stop animation at current value.</summary>
    public void Stop()
    {
        _playing = false;
    }

    /// <summary>Force apply the 'from' value immediately.</summary>
    public void ApplyFrom()
    {
        ApplyValueAt(0f);
    }

    /// <summary>Force apply the 'to' value immediately.</summary>
    public void ApplyTo()
    {
        ApplyValueAt(1f);
    }

    void Update()
    {
        if (!_playing || duration <= 0f)
            return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        _elapsed += dt;

        float rawT = _elapsed / duration;
        float loopedT;

        switch (playMode)
        {
            case PlayMode.Once:
                loopedT = Mathf.Clamp01(rawT);
                if (_elapsed >= duration)
                {
                    _playing = false;
                }
                break;

            case PlayMode.Loop:
                loopedT = Mathf.Repeat(rawT, 1f);
                break;

            case PlayMode.PingPong:
                loopedT = Mathf.PingPong(rawT, 1f);
                break;

            default:
                loopedT = Mathf.Clamp01(rawT);
                break;
        }

        float easedT = curve != null ? curve.Evaluate(loopedT) : loopedT;
        ApplyValueAt(easedT);
    }

    void ApplyValueAt(float t)
    {
        float timeForNoise = _elapsed * noiseSpeed;

        switch (valueType)
        {
            case ValueType.Float:
                float fBase = Mathf.Lerp(fromFloat, toFloat, t);
                float fFinal = useNoise ? AddNoiseFloat(fBase, timeForNoise) : fBase;
                ApplyFloat(fFinal);
                break;

            case ValueType.Vector3:
                Vector3 vBase = Vector3.Lerp(fromVector, toVector, t);
                Vector3 vFinal = useNoise ? AddNoiseVector(vBase, timeForNoise) : vBase;
                ApplyVector(vFinal);
                break;

            case ValueType.Quaternion:
                Quaternion qBase = interpMode == InterpMode.Slerp
                    ? Quaternion.Slerp(fromQuaternion, toQuaternion, t)
                    : Quaternion.Lerp(fromQuaternion, toQuaternion, t);

                Quaternion qFinal = useNoise ? AddNoiseQuaternion(qBase, timeForNoise) : qBase;
                ApplyQuaternion(qFinal);
                break;

            case ValueType.Color:
                Color cBase = Color.Lerp(fromColor, toColor, t);
                Color cFinal = useNoise ? AddNoiseColor(cBase, timeForNoise) : cBase;
                ApplyColor(cFinal);
                break;
        }
    }

    /// <summary>
    /// Validates current configuration and logs detailed errors with available options.
    /// </summary>
    [ContextMenu("Health Check / Validate Binding")]
    public void HealthCheck()
    {
        switch (targetKind)
        {
            case TargetKind.ComponentMember:
                HealthCheckComponentMember();
                break;
            case TargetKind.MaterialProperty:
                HealthCheckMaterialProperty();
                break;
            case TargetKind.TransformPosition:
            case TargetKind.TransformRotation:
                HealthCheckTransformTarget();
                break;
        }
    }

    #region Noise

    float AddNoiseFloat(float value, float time)
    {
        if (noisePercent <= 0f || noiseSpeed <= 0f) return value;

        float n = Perlin01(noiseSeed, time) * 2f - 1f; // [-1,1]
        float factor = 1f + n * noisePercent;
        return value * factor;
    }

    Vector3 AddNoiseVector(Vector3 v, float time)
    {
        if (noisePercent <= 0f || noiseSpeed <= 0f) return v;

        if (noisePerComponent)
        {
            float nx = Perlin01(noiseSeed + 17, time) * 2f - 1f;
            float ny = Perlin01(noiseSeed + 31, time) * 2f - 1f;
            float nz = Perlin01(noiseSeed + 47, time) * 2f - 1f;

            return new Vector3(
                v.x * (1f + nx * noisePercent),
                v.y * (1f + ny * noisePercent),
                v.z * (1f + nz * noisePercent)
            );
        }
        else
        {
            float n = Perlin01(noiseSeed, time) * 2f - 1f;
            float factor = 1f + n * noisePercent;
            return v * factor;
        }
    }

    Quaternion AddNoiseQuaternion(Quaternion q, float time)
    {
        if (noisePercent <= 0f || noiseSpeed <= 0f) return q;

        float n = Perlin01(noiseSeed, time) * 2f - 1f;
        float angleDeg = n * noisePercent * 10f; // 0–10 degrees-ish; tweak if desired

        Vector3 axis = new Vector3(
            Perlin01(noiseSeed + 61, time) * 2f - 1f,
            Perlin01(noiseSeed + 73, time) * 2f - 1f,
            Perlin01(noiseSeed + 89, time) * 2f - 1f
        ).normalized;

        if (axis.sqrMagnitude < 1e-4f)
            axis = Vector3.up;

        Quaternion noiseRot = Quaternion.AngleAxis(angleDeg, axis);
        return noiseRot * q;
    }

    Color AddNoiseColor(Color c, float time)
    {
        if (noisePercent <= 0f || noiseSpeed <= 0f) return c;

        if (noisePerComponent)
        {
            float nr = Perlin01(noiseSeed + 5, time) * 2f - 1f;
            float ng = Perlin01(noiseSeed + 11, time) * 2f - 1f;
            float nb = Perlin01(noiseSeed + 23, time) * 2f - 1f;
            float na = Perlin01(noiseSeed + 29, time) * 2f - 1f;

            return new Color(
                c.r * (1f + nr * noisePercent),
                c.g * (1f + ng * noisePercent),
                c.b * (1f + nb * noisePercent),
                c.a * (1f + na * noisePercent)
            );
        }
        else
        {
            float n = Perlin01(noiseSeed, time) * 2f - 1f;
            float factor = 1f + n * noisePercent;
            return c * factor;
        }
    }

    float Perlin01(float xSeed, float t)
    {
        return Mathf.PerlinNoise(xSeed + 0.1234f, t);
    }

    float Perlin01(int intSeed, float t)
    {
        return Mathf.PerlinNoise(intSeed + 0.1234f, t);
    }

    #endregion

    #region Apply to Targets

    void ApplyFloat(float value)
    {
        switch (targetKind)
        {
            case TargetKind.ComponentMember:
                SetMemberValue(value);
                break;
            case TargetKind.MaterialProperty:
                SetShaderFloat(value);
                break;
            case TargetKind.TransformPosition:
                break;
            case TargetKind.TransformRotation:
                break;
        }
    }

    void ApplyVector(Vector3 value)
    {
        switch (targetKind)
        {
            case TargetKind.ComponentMember:
                SetMemberValue(value);
                break;

            case TargetKind.MaterialProperty:
                SetShaderVector(value);
                break;

            case TargetKind.TransformPosition:
                if (targetComponent is Transform trP)
                {
                    if (useLocalSpace)
                        trP.localPosition = value;
                    else
                        trP.position = value;
                }
                break;

            case TargetKind.TransformRotation:
                if (targetComponent is Transform trR)
                {
                    if (useLocalSpace)
                        trR.localEulerAngles = value;
                    else
                        trR.eulerAngles = value;
                }
                break;
        }
    }

    void ApplyQuaternion(Quaternion value)
    {
        switch (targetKind)
        {
            case TargetKind.ComponentMember:
                SetMemberValue(value);
                break;

            case TargetKind.MaterialProperty:
                SetShaderVector(new Vector4(value.x, value.y, value.z, value.w));
                break;

            case TargetKind.TransformPosition:
                break;

            case TargetKind.TransformRotation:
                if (targetComponent is Transform tr)
                {
                    if (useLocalSpace)
                        tr.localRotation = value;
                    else
                        tr.rotation = value;
                }
                break;
        }
    }

    void ApplyColor(Color value)
    {
        switch (targetKind)
        {
            case TargetKind.ComponentMember:
                SetMemberValue(value);
                break;

            case TargetKind.MaterialProperty:
                SetShaderColor(value);
                break;

            case TargetKind.TransformPosition:
            case TargetKind.TransformRotation:
                break;
        }
    }

    #endregion

    #region Validation Helpers

    bool HealthCheckComponentMember()
    {
        if (targetComponent == null)
        {
            var all = GetComponents<Component>();
            string available = "(none)";
            if (all != null && all.Length > 0)
            {
                available = string.Join(", ", Array.ConvertAll(all, c => c != null ? c.GetType().Name : "null"));
            }

            Debug.LogError($"[UniversalPropertyAnimator][500] TargetKind=ComponentMember but targetComponent is null on '{name}'. Available components on this GameObject: {available}", this);
            return false;
        }

        CacheComponentMember();
        
        // If CacheComponentMember resolved a dotted member chain, treat that as valid.
        if (_memberInfos != null && _memberInfos.Length > 0)
        {
            Debug.LogError($"[UniversalPropertyAnimator][200 OK] ComponentMember binding valid on '{name}'. Component={targetComponent.GetType().Name}, member='{memberName}', valueType={valueType}", this);
            return true;
        }

        // Legacy single-member check
        if (_fieldInfo != null || (_propertyInfo != null && _propertyInfo.CanRead))
        {
            Debug.LogError($"[UniversalPropertyAnimator][200 OK] ComponentMember binding valid on '{name}'. Component={targetComponent.GetType().Name}, member='{memberName}', valueType={valueType}", this);
            return true;
        }

        // Not found — produce richer diagnostics and fuzzy suggestions.
        SuggestMemberOptions(memberName);
        return false;
    }

    void SuggestMemberOptions(string path)
    {
        var type = targetComponent.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        string[] tokens = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

        // Traverse as far as possible and collect context
        Type cur = type;
        int depth = 0;
        for (; depth < tokens.Length; depth++)
        {
            string tok = tokens[depth];
            var f = cur.GetField(tok, flags);
            var p = cur.GetProperty(tok, flags);
            if (f != null)
            {
                cur = f.FieldType;
                continue;
            }
            if (p != null)
            {
                cur = p.PropertyType;
                continue;
            }
            // failure at this depth — provide suggestions for this type
            var fields = cur.GetFields(flags);
            var props = cur.GetProperties(flags);

            // Rank candidates: exact case-insensitive, startswith, contains
            System.Func<string, string, int> score = (candidate, needle) =>
            {
                if (string.Equals(candidate, needle, StringComparison.OrdinalIgnoreCase)) return 100;
                if (candidate.StartsWith(needle, StringComparison.OrdinalIgnoreCase)) return 50;
                if (candidate.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return 10;
                return 0;
            };

            var list = new System.Collections.Generic.List<(int score, string text)>();
            foreach (var ff in fields) list.Add((score(ff.Name, tok), $"field {ff.FieldType.Name} {ff.Name}"));
            foreach (var pp in props) list.Add((score(pp.Name, tok), $"property {pp.PropertyType.Name} {pp.Name}"));
            list.Sort((a, b) => b.score.CompareTo(a.score));

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"[UniversalPropertyAnimator][500] Could not resolve token '{tok}' while resolving path '{path}' at depth {depth} on type {cur.Name} for GameObject '{name}'.");
            sb.AppendLine("Closest matches on this type:");
            int shown = 0;
            foreach (var item in list)
            {
                if (item.score <= 0) continue;
                sb.AppendLine($"  ({item.score}) {item.text}");
                shown++;
                if (shown >= 12) break;
            }
            if (shown == 0)
            {
                sb.AppendLine("  (no close matches; available members below)");
                foreach (var ff in fields) sb.AppendLine($"  field {ff.FieldType.Name} {ff.Name}");
                foreach (var pp in props) sb.AppendLine($"  property {pp.PropertyType.Name} {pp.Name}");
            }

            Debug.LogError(sb.ToString(), this);
            return;
        }

        // If we traversed whole path but nothing was set, suggest available children of last type
        var finalFields = cur.GetFields(flags);
        var finalProps = cur.GetProperties(flags);
        System.Text.StringBuilder sb2 = new System.Text.StringBuilder();
        sb2.AppendLine($"[UniversalPropertyAnimator][500] Path '{path}' reached type {cur.Name} but final token may be invalid on that type. Available children:");
        foreach (var ff in finalFields) sb2.AppendLine($"  field {ff.FieldType.Name} {ff.Name}");
        foreach (var pp in finalProps) sb2.AppendLine($"  property {pp.PropertyType.Name} {pp.Name}");
        Debug.LogError(sb2.ToString(), this);
    }

    bool HealthCheckMaterialProperty()
    {
        if (targetRenderer == null)
        {
            var all = GetComponentsInChildren<Renderer>(true);
            string available = all != null && all.Length > 0
                ? string.Join("\n  ", Array.ConvertAll(all, r => r != null ? $"{r.GetType().Name} on {r.gameObject.name}" : "null"))
                : "(no Renderers found on this GameObject or children)";

            Debug.LogError($"[UniversalPropertyAnimator][500] TargetKind=MaterialProperty but targetRenderer is null on '{name}'.\nAvailable Renderers below this object:\n  {available}", this);
            return false;
        }

        var mats = targetRenderer.sharedMaterials;
        if (mats == null || mats.Length == 0)
        {
            Debug.LogError($"[UniversalPropertyAnimator][500] Renderer on '{name}' has no materials assigned.", this);
            return false;
        }

        if (materialIndex < 0 || materialIndex >= mats.Length)
        {
            string matNames = string.Join(", ", Array.ConvertAll(mats, m => m != null ? m.name : "null"));
            Debug.LogError($"[UniversalPropertyAnimator][500] materialIndex={materialIndex} is out of range for Renderer on '{name}'. Material count = {mats.Length}. Materials: {matNames}", this);
            return false;
        }

        var mat = mats[materialIndex];
        if (mat == null)
        {
            Debug.LogError($"[UniversalPropertyAnimator][500] Material at index {materialIndex} is null on Renderer '{targetRenderer.name}'.", this);
            return false;
        }

        if (string.IsNullOrEmpty(shaderProperty))
        {
            LogShaderPropertyOptions(mat, "Shader property name is empty.");
            return false;
        }

        var shader = mat.shader;
        if (shader == null)
        {
            Debug.LogError($"[UniversalPropertyAnimator][500] Material '{mat.name}' on '{name}' has no shader.", this);
            return false;
        }

        bool hasProp = mat.HasProperty(shaderProperty);
        if (!hasProp)
        {
            LogShaderPropertyOptions(mat, $"Shader property '{shaderProperty}' not found on shader '{shader.name}'.");
            return false;
        }

        Debug.LogError($"[UniversalPropertyAnimator][200 OK] MaterialProperty binding valid on '{name}'. Renderer='{targetRenderer.name}', materialIndex={materialIndex}, shader='{shader.name}', property='{shaderProperty}', valueType={valueType}", this);
        return true;
    }

    bool HealthCheckTransformTarget()
    {
        if (!(targetComponent is Transform))
        {
            var tr = GetComponent<Transform>();
            if (tr != null)
            {
                Debug.LogError($"[UniversalPropertyAnimator][500] TargetKind={targetKind} expects targetComponent to be a Transform on '{name}'. Suggested fix: set targetComponent = this.transform.", this);
            }
            else
            {
                Debug.LogError($"[UniversalPropertyAnimator][500] TargetKind={targetKind} expects targetComponent to be a Transform, but none is assigned and this GameObject has no Transform (unexpected).", this);
            }
            return false;
        }

        var targetTransform = (Transform)targetComponent;
        Debug.LogError($"[UniversalPropertyAnimator][200 OK] Transform binding valid on '{name}'. Mode={targetKind}, Transform={targetTransform.name}", this);
        return true;
    }

    void LogShaderPropertyOptions(Material mat, string prefix)
    {
        var shader = mat.shader;
        if (shader == null)
        {
            Debug.LogError($"[UniversalPropertyAnimator] {prefix} Material '{mat.name}' has no shader.", this);
            return;
        }

        int count = shader.GetPropertyCount();
        if (count == 0)
        {
            Debug.LogError($"[UniversalPropertyAnimator] {prefix} Shader '{shader.name}' on material '{mat.name}' has no exposed properties.", this);
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[UniversalPropertyAnimator] {prefix}");
        sb.AppendLine($"Shader: {shader.name} (material '{mat.name}')");
        sb.AppendLine("Available properties:");

        for (int i = 0; i < count; i++)
        {
            string propName = shader.GetPropertyName(i);
            var propType = shader.GetPropertyType(i);
            sb.AppendLine($"  {propType} {propName}");
        }

        Debug.LogError(sb.ToString(), this);
    }

    #endregion

    #region Reflection / Shader Binding

    void CacheComponentMember()
    {
        _fieldInfo = null;
        _propertyInfo = null;
        _memberInfos = null;
        _componentSelector = null;

        if (targetKind != TargetKind.ComponentMember || targetComponent == null || string.IsNullOrEmpty(memberName))
            return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Split dotted path
        string[] tokens = memberName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return;

        // If last token is a component selector (x,y,z,w,r,g,b,a), treat it specially
        string last = tokens[tokens.Length - 1];
        if (last == "x" || last == "y" || last == "z" || last == "w" || last == "r" || last == "g" || last == "b" || last == "a")
        {
            _componentSelector = last;
            Array.Resize(ref tokens, tokens.Length - 1);
            if (tokens.Length == 0)
            {
                Debug.LogWarning($"UniversalPropertyAnimator: component selector '{last}' has no parent member in '{memberName}'.", this);
                return;
            }
        }

        // Build member info chain
        var infos = new System.Collections.Generic.List<MemberInfo>();
        Type curType = targetComponent.GetType();
        object dummy = null;
        foreach (var tok in tokens)
        {
            var f = curType.GetField(tok, flags);
            if (f != null)
            {
                infos.Add(f);
                curType = f.FieldType;
                continue;
            }

            var p = curType.GetProperty(tok, flags);
            if (p != null)
            {
                infos.Add(p);
                curType = p.PropertyType;
                continue;
            }

            Debug.LogWarning($"UniversalPropertyAnimator: Could not find member '{tok}' while resolving '{memberName}' on type {curType.Name}.", this);
            return;
        }

        if (infos.Count > 0)
        {
            _memberInfos = infos.ToArray();
            // If path is a single token (no dots) keep legacy fields too for backwards compatibility
            if (_memberInfos.Length == 1)
            {
                var mi = _memberInfos[0];
                _fieldInfo = mi as FieldInfo;
                _propertyInfo = mi as PropertyInfo;
            }
            // store resolved type for the final member
            var lastMember = _memberInfos[_memberInfos.Length - 1];
            _resolvedMemberType = GetMemberType(lastMember);
            resolvedMemberTypeName = _resolvedMemberType != null ? _resolvedMemberType.FullName : string.Empty;
        }
        else
        {
            _resolvedMemberType = null;
            resolvedMemberTypeName = string.Empty;
        }
    }

    void SetMemberValue(object boxedValue)
    {
        if (targetComponent == null)
            return;

        // If we have a cached member chain (dotted path), traverse and set through the chain.
        if (_memberInfos != null && _memberInfos.Length > 0)
        {
            try
            {
                int n = _memberInfos.Length;
                // objChain[0] = targetComponent, objChain[i] = value after applying memberInfos[i-1]
                object[] objChain = new object[n + 1];
                objChain[0] = targetComponent;

                // Traverse to build object chain
                for (int i = 0; i < n; i++)
                {
                    var mi = _memberInfos[i];
                    var parent = objChain[i];
                    object child = GetMemberValue(parent, mi);
                    objChain[i + 1] = child;
                }

                int parentIndex = n - 1;
                var finalMember = _memberInfos[parentIndex];
                var parentObj = objChain[parentIndex];

                if (_componentSelector != null)
                {
                    // Modify a component of the final member (e.g., CenterUV.y)
                    object curVal = GetMemberValue(parentObj, finalMember);
                    object newVal = ModifyComponentValue(curVal, _componentSelector, boxedValue);
                    SetMemberValueOnObject(parentObj, finalMember, newVal);
                }
                else
                {
                    // Direct set — try to convert boxedValue to target type when possible
                    Type targetType = GetMemberType(finalMember);
                    object toSet = boxedValue;
                    if (toSet != null && !targetType.IsAssignableFrom(toSet.GetType()))
                    {
                        try
                        {
                            toSet = Convert.ChangeType(toSet, targetType);
                        }
                        catch { /* ignore conversion errors, attempt direct assignment below */ }
                    }
                    SetMemberValueOnObject(parentObj, finalMember, toSet);
                }

                // Propagate value-type changes back up the chain
                for (int i = parentIndex; i >= 1; i--)
                {
                    var mi = _memberInfos[i - 1];
                    var childObj = objChain[i];
                    // If the child was a value type (struct) we must set it back on its parent
                    if (childObj != null && childObj.GetType().IsValueType)
                    {
                        SetMemberValueOnObject(objChain[i - 1], mi, childObj);
                    }
                }
                return;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"UniversalPropertyAnimator: Failed to set dotted member '{memberName}': {ex.Message}", this);
                return;
            }
        }

        // Fallback: legacy single-member behavior
        if (_fieldInfo != null)
        {
            _fieldInfo.SetValue(targetComponent, boxedValue);
        }
        else if (_propertyInfo != null && _propertyInfo.CanWrite)
        {
            _propertyInfo.SetValue(targetComponent, boxedValue);
        }
        else
        {
            CacheComponentMember();
        }
    }

    object GetMemberValue(object obj, MemberInfo mi)
    {
        if (obj == null) return null;
        if (mi is FieldInfo fi) return fi.GetValue(obj);
        if (mi is PropertyInfo pi) return pi.CanRead ? pi.GetValue(obj) : null;
        return null;
    }

    void SetMemberValueOnObject(object obj, MemberInfo mi, object value)
    {
        if (obj == null) return;
        if (mi is FieldInfo fi)
        {
            object coerced = CoerceValueForType(value, fi.FieldType);
            fi.SetValue(obj, coerced);
            return;
        }
        if (mi is PropertyInfo pi && pi.CanWrite)
        {
            object coerced = CoerceValueForType(value, pi.PropertyType);
            pi.SetValue(obj, coerced);
            return;
        }
    }

    Type GetMemberType(MemberInfo mi)
    {
        if (mi is FieldInfo fi) return fi.FieldType;
        if (mi is PropertyInfo pi) return pi.PropertyType;
        return typeof(object);
    }

    object ModifyComponentValue(object original, string component, object boxedValue)
    {
        if (original == null) return original;

        try
        {
            float v = Convert.ToSingle(boxedValue);
            var t = original.GetType();
            if (t == typeof(Vector2))
            {
                Vector2 vv = (Vector2)original;
                if (component == "x") vv.x = v;
                else if (component == "y") vv.y = v;
                return vv;
            }
            if (t == typeof(Vector3))
            {
                Vector3 vv = (Vector3)original;
                if (component == "x") vv.x = v;
                else if (component == "y") vv.y = v;
                else if (component == "z") vv.z = v;
                return vv;
            }
            if (t == typeof(Vector4))
            {
                Vector4 vv = (Vector4)original;
                if (component == "x") vv.x = v;
                else if (component == "y") vv.y = v;
                else if (component == "z") vv.z = v;
                else if (component == "w") vv.w = v;
                return vv;
            }
            if (t == typeof(Color))
            {
                Color c = (Color)original;
                if (component == "r") c.r = v;
                else if (component == "g") c.g = v;
                else if (component == "b") c.b = v;
                else if (component == "a") c.a = v;
                return c;
            }
            if (t == typeof(Quaternion))
            {
                Quaternion q = (Quaternion)original;
                if (component == "x") q.x = v;
                else if (component == "y") q.y = v;
                else if (component == "z") q.z = v;
                else if (component == "w") q.w = v;
                return q;
            }
        }
        catch { }
        return original;
    }

    object CoerceValueForType(object value, Type targetType)
    {
        if (targetType == null) return value;
        if (value == null) return null;

        try
        {
            // If already assignable
            if (targetType.IsAssignableFrom(value.GetType())) return value;

            // Convert numeric -> float
            if (targetType == typeof(float))
            {
                return Convert.ToSingle(value);
            }

            if (targetType == typeof(int))
            {
                return Convert.ToInt32(value);
            }

            // Vector conversions
            if (targetType == typeof(Vector2))
            {
                if (value is Vector2 v2) return v2;
                if (value is Vector3 v3) return new Vector2(v3.x, v3.y);
                if (value is Vector4 v4) return new Vector2(v4.x, v4.y);
                if (value is Color c) return new Vector2(c.r, c.g);
                if (value is float f) return new Vector2(f, f);
            }

            if (targetType == typeof(Vector3))
            {
                if (value is Vector3 v3) return v3;
                if (value is Vector2 v2) return new Vector3(v2.x, v2.y, 0f);
                if (value is Vector4 v4) return new Vector3(v4.x, v4.y, v4.z);
                if (value is Color c) return new Vector3(c.r, c.g, c.b);
                if (value is float f) return new Vector3(f, f, f);
            }

            if (targetType == typeof(Vector4))
            {
                if (value is Vector4 v4) return v4;
                if (value is Vector3 v3) return new Vector4(v3.x, v3.y, v3.z, 0f);
                if (value is Vector2 v2) return new Vector4(v2.x, v2.y, 0f, 0f);
                if (value is Color c) return new Vector4(c.r, c.g, c.b, c.a);
                if (value is float f) return new Vector4(f, f, f, f);
            }

            if (targetType == typeof(Color))
            {
                if (value is Color c) return c;
                if (value is Vector4 v4) return new Color(v4.x, v4.y, v4.z, v4.w);
                if (value is Vector3 v3) return new Color(v3.x, v3.y, v3.z, 1f);
                if (value is Vector2 v2) return new Color(v2.x, v2.y, 0f, 1f);
                if (value is float f) return new Color(f, f, f, 1f);
            }

            if (targetType == typeof(Quaternion))
            {
                if (value is Quaternion q) return q;
                if (value is Vector4 v4) return new Quaternion(v4.x, v4.y, v4.z, v4.w);
            }

            // Try ChangeType as last resort
            return Convert.ChangeType(value, targetType);
        }
        catch { return value; }
    }

    void CacheShaderProperty()
    {
        _shaderIdValid = false;

        if (targetKind != TargetKind.MaterialProperty || targetRenderer == null || string.IsNullOrEmpty(shaderProperty))
            return;

        _shaderId = Shader.PropertyToID(shaderProperty);
        _shaderIdValid = true;

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();
    }

    void GetMPB()
    {
        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        targetRenderer.GetPropertyBlock(_mpb, materialIndex);
    }

    void ApplyMPB()
    {
        targetRenderer.SetPropertyBlock(_mpb, materialIndex);
    }

    void SetShaderFloat(float v)
    {
        if (!_shaderIdValid || targetRenderer == null)
            return;

        GetMPB();
        _mpb.SetFloat(_shaderId, v);
        ApplyMPB();
    }

    void SetShaderVector(Vector4 v)
    {
        if (!_shaderIdValid || targetRenderer == null)
            return;

        GetMPB();
        _mpb.SetVector(_shaderId, v);
        ApplyMPB();
    }

    void SetShaderVector(Vector3 v)
    {
        SetShaderVector((Vector4)v);
    }

    void SetShaderColor(Color c)
    {
        if (!_shaderIdValid || targetRenderer == null)
            return;

        GetMPB();
        _mpb.SetColor(_shaderId, c);
        ApplyMPB();
    }

    #endregion
}
