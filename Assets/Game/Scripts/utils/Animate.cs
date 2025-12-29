using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// General purpose animator/tween component. Supports animating arbitrary values
/// via getter/setter delegates and provides convenience helpers for common
/// Unity types (float, Vector3, Quaternion, Color).
/// - Can use the current value as the start or force a provided start value.
/// - Uses an <see cref="AnimationCurve"/> for easing.
/// </summary>
public class Animate : MonoBehaviour
{
	// Track started coroutines so we can stop them later if requested
	private readonly List<Coroutine> activeTweens = new List<Coroutine>();

	[Header("Configured Tweens (Inspector)")]
	[SerializeField]
	private List<TweenEntry> configuredTweens = new List<TweenEntry>();

	[Serializable]
	public enum TweenType
	{
		Position,
		LocalPosition,
		RotationEuler,
		LocalRotationEuler,
		Scale,
		CanvasGroupAlpha,
		RendererColor,
		MaterialFloat,
		Float,
		CustomProperty
	}

	public bool playAllOnStart = true;

	void Start()
	{
		if (playAllOnStart)
		{
			PlayAllConfigured();
			return;
		}

		// Legacy per-entry flag support
		foreach (var e in configuredTweens)
		{
			if (e != null && e.playOnStart)
				PlayEntry(e);
		}
	}

	[Serializable]
	public class TweenEntry
	{
		public string name;
		public GameObject targetObject;
		public Component targetComponent;
		public TweenType type = TweenType.Position;
		public bool playOnStart = false;
		public bool useCurrentAsFrom = true;
		public bool local = true; // used for position/rotation

		public Vector3 fromVec3;
		public Vector3 toVec3;

		public Color fromColor = Color.white;
		public Color toColor = Color.white;

		public float fromFloat = 0f;
		public float toFloat = 1f;
		public string materialProperty = "_Glossiness";
		public int materialIndex = 0;
		[Tooltip("Optional additional material color properties to set alongside materialProperty (e.g. _EmissionColor, _SpecColor).")]
		public string[] materialColorProperties = Array.Empty<string>();

		public bool fromBool = false;
		public bool toBool = true;
		public string propertyName;
		public CustomPropertyMode propertyMode = CustomPropertyMode.AutoTween;
		public string detectedPropertyType;

		public float duration = 1f;
		public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
	}

	public enum CustomPropertyMode
	{
		AutoTween,
		SetAtEnd,
		ToggleAtHalf
	}

	// ----------------
	// Generic Tween API
	// ----------------

	/// <summary>
	/// Tween from an explicit <paramref name="from"/> to <paramref name="to"/> using the provided lerp function.
	/// </summary>
	public Coroutine Tween<T>(Func<T> getter, Action<T> setter, T from, T to, float duration, Func<T, T, float, T> lerpFunc, AnimationCurve curve = null, Action onComplete = null)
	{
		if (duration <= 0f)
		{
			setter(to);
			onComplete?.Invoke();
			return null;
		}

		curve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
		IEnumerator routine = TweenCoroutine(getter, setter, from, to, duration, curve, lerpFunc, onComplete);
		Coroutine c = StartCoroutine(routine);
		activeTweens.Add(c);
		return c;
	}

	private const float BoolHighThreshold = 0.6f;
	private const float BoolLowThreshold = 0.4f;

	private IEnumerator DriveBoolWithCurve(object owner, MemberInfo member, bool startValue, bool endValue, float duration, AnimationCurve curve)
	{
		bool state = startValue;
		SetMemberValue(owner, member, state);
		float elapsed = 0f;
		while (elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / duration);
			float v = curve.Evaluate(t);

			if (!state && v > BoolHighThreshold)
			{
				state = true;
				SetMemberValue(owner, member, state);
			}
			else if (state && v < BoolLowThreshold)
			{
				state = false;
				SetMemberValue(owner, member, state);
			}
			yield return null;
		}
		SetMemberValue(owner, member, endValue);
	}

	private IEnumerator InvokeMethodOnCurve(MemberInfo member, object owner, float duration, AnimationCurve curve)
	{
		float elapsed = 0f;
		bool fired = false;
		while (elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / duration);
			float v = curve.Evaluate(t);
			if (!fired && v > BoolHighThreshold)
			{
				if (member is MethodInfo mi) mi.Invoke(owner, null);
				fired = true;
			}
			if (fired && v < BoolLowThreshold)
			{
				fired = false; // allow retrigger if curve goes up again
			}
			yield return null;
		}
	}

	// Resolve a dot-separated member path starting from a root object (usually a Component).
	// Returns the owner object that contains the final member and the MemberInfo for that member.
	private bool TryResolveMember(object root, string path, out object owner, out MemberInfo member, out Type memberType)
	{
		owner = root;
		member = null;
		memberType = null;
		if (string.IsNullOrEmpty(path) || owner == null) return false;

		string[] parts = path.Split('.');
		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i];
			if (owner == null) return false;
			Type t = owner.GetType();
			var pi = t.GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
			var fi = t.GetField(part, BindingFlags.Public | BindingFlags.Instance);

			if (pi != null)
			{
				if (i == parts.Length - 1)
				{
					member = pi;
					memberType = pi.PropertyType;
					return true;
				}
				owner = pi.GetValue(owner);
				continue;
			}

			if (fi != null)
			{
				if (i == parts.Length - 1)
				{
					member = fi;
					memberType = fi.FieldType;
					return true;
				}
				owner = fi.GetValue(owner);
				continue;
			}

			var mi = t.GetMethod(part, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
			if (mi != null && mi.GetParameters().Length == 0 && mi.ReturnType == typeof(void))
			{
				if (i == parts.Length - 1)
				{
					member = mi;
					memberType = typeof(void);
					return true;
				}
				owner = mi.Invoke(owner, null);
				continue;
			}

			// not found
			return false;
		}

		return false;
	}

	private object GetMemberValue(object owner, MemberInfo member)
	{
		if (member is PropertyInfo pi) return pi.GetValue(owner);
		if (member is FieldInfo fi) return fi.GetValue(owner);
		return null;
	}

	private bool SetMemberValue(object owner, MemberInfo member, object value)
	{
		if (member is PropertyInfo pi)
		{
			if (!pi.CanWrite) return false;
			pi.SetValue(owner, value);
			return true;
		}
		if (member is FieldInfo fi)
		{
			fi.SetValue(owner, value);
			return true;
		}
		return false;
	}

	/// <summary>
	/// Tween using the current getter value as the start.
	/// </summary>
	public Coroutine Tween<T>(Func<T> getter, Action<T> setter, T to, float duration, Func<T, T, float, T> lerpFunc, AnimationCurve curve = null, Action onComplete = null)
	{
		T from = getter();
		return Tween(getter, setter, from, to, duration, lerpFunc, curve, onComplete);
	}

	private IEnumerator TweenCoroutine<T>(Func<T> getter, Action<T> setter, T from, T to, float duration, AnimationCurve curve, Func<T, T, float, T> lerpFunc, Action onComplete)
	{
		float elapsed = 0f;
		setter(from);
		while (elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / duration);
			float e = curve.Evaluate(t);
			setter(lerpFunc(from, to, e));
			yield return null;
		}

		setter(to);
		onComplete?.Invoke();
	}

	// ----------------
	// Tween management
	// ----------------
	public void StopTween(Coroutine c)
	{
		if (c == null) return;
		try { StopCoroutine(c); } catch { }
		activeTweens.Remove(c);
	}

	public void StopAllTweens()
	{
		foreach (var c in activeTweens)
		{
			if (c != null)
			{
				try { StopCoroutine(c); } catch { }
			}
		}

		activeTweens.Clear();
	}

	// ----------------
	// Inspector-play helpers
	// ----------------

	/// <summary>
	/// Play all configured tweens in the inspector.
	/// </summary>
	public void PlayAllConfigured()
	{
		foreach (var e in configuredTweens)
		{
			if (e != null)
				PlayEntry(e);
		}
	}

	/// <summary>
	/// Play a configured entry by index.
	/// </summary>
	public void PlayByIndex(int index)
	{
		if (index < 0 || index >= configuredTweens.Count) return;
		PlayEntry(configuredTweens[index]);
	}

	/// <summary>
	/// Play the first configured entry with a matching name.
	/// </summary>
	public void PlayByName(string name)
	{
		var e = configuredTweens.Find(x => x != null && x.name == name);
		if (e != null) PlayEntry(e);
	}

	private Coroutine PlayEntry(TweenEntry e)
	{
		if (e == null || e.targetObject == null) return null;
		var go = e.targetObject;
		switch (e.type)
		{
			case TweenType.Position:
			case TweenType.LocalPosition:
				return TweenPosition(go.transform, e.toVec3, e.duration, e.curve, e.local, !e.useCurrentAsFrom, e.fromVec3);

			case TweenType.RotationEuler:
			case TweenType.LocalRotationEuler:
				{
					Quaternion toQ = Quaternion.Euler(e.toVec3);
					Quaternion fromQ = Quaternion.Euler(e.fromVec3);
					bool localRot = (e.type == TweenType.LocalRotationEuler) || e.local;
					return TweenRotation(go.transform, toQ, e.duration, e.curve, localRot, !e.useCurrentAsFrom, fromQ);
				}

			case TweenType.Scale:
				return TweenScale(go.transform, e.toVec3, e.duration, e.curve, !e.useCurrentAsFrom, e.fromVec3);

			case TweenType.CanvasGroupAlpha:
				{
					var cg = go.GetComponent<CanvasGroup>();
					if (cg == null) return null;
					return TweenCanvasAlpha(cg, e.toFloat, e.duration, e.curve, !e.useCurrentAsFrom, e.fromFloat);
				}

			case TweenType.RendererColor:
				{
					var r = go.GetComponent<Renderer>();
					if (r == null) return null;
					string prop = string.IsNullOrEmpty(e.materialProperty) ? "_Color" : e.materialProperty;
					int mIndex = Mathf.Clamp(e.materialIndex, 0, r.materials.Length - 1);
					return TweenMaterialColor(r, mIndex, prop, e.materialColorProperties, e.toColor, e.duration, e.curve, !e.useCurrentAsFrom, e.fromColor, e.useCurrentAsFrom);
				}

			case TweenType.MaterialFloat:
				{
					var r = go.GetComponent<Renderer>();
					if (r == null) return null;
					string prop = string.IsNullOrEmpty(e.materialProperty) ? "_Glossiness" : e.materialProperty;
					Func<float> getter = () => r.material.GetFloat(prop);
					Action<float> setter = v => r.material.SetFloat(prop, v);
					return TweenFloat(getter, setter, e.toFloat, e.duration, e.curve, !e.useCurrentAsFrom, e.fromFloat);
				}

			case TweenType.CustomProperty:
				{
					Component comp = e.targetComponent ?? go.GetComponent<Component>();
					if (comp == null)
					{
						Debug.LogWarning($"Animate: CustomProperty entry '{e.name}' has no component assigned on {go.name}.");
						return null;
					}

					// support nested member path via dot notation; strip enum backing-field suffix if user picked it
					string resolvedPath = e.propertyName;
					const string backingSuffix = ".value__";
					if (!string.IsNullOrEmpty(resolvedPath) && resolvedPath.EndsWith(backingSuffix, StringComparison.Ordinal))
						resolvedPath = resolvedPath.Substring(0, resolvedPath.Length - backingSuffix.Length);

					if (TryResolveMember(comp, resolvedPath, out var owner, out var memberInfo, out var memberType))
					{
						// handle numeric types
						if (memberType == typeof(float) || memberType == typeof(double) || memberType == typeof(int))
						{
							Func<float> getter = () => Convert.ToSingle(GetMemberValue(owner, memberInfo));
							Action<float> setter = v => SetMemberValue(owner, memberInfo, Convert.ChangeType(v, memberType));
							return TweenFloat(getter, setter, e.toFloat, e.duration, e.curve, !e.useCurrentAsFrom, e.fromFloat);
						}

						// handle enums by tweening over their underlying numeric value
						if (memberType.IsEnum)
						{
							Type underlying = Enum.GetUnderlyingType(memberType);
							Func<float> getter = () => Convert.ToSingle(Convert.ChangeType(GetMemberValue(owner, memberInfo), underlying));
							Action<float> setter = v => SetMemberValue(owner, memberInfo, Enum.ToObject(memberType, Convert.ChangeType(v, underlying)));
							return TweenFloat(getter, setter, e.toFloat, e.duration, e.curve, !e.useCurrentAsFrom, e.fromFloat);
						}

						if (memberType == typeof(Vector3))
						{
							Func<Vector3> getter = () => (Vector3)GetMemberValue(owner, memberInfo);
							Action<Vector3> setter = v => SetMemberValue(owner, memberInfo, v);
							return Tween(getter, setter, e.toVec3, e.duration, Vector3.LerpUnclamped, e.curve, null);
						}

						if (memberType == typeof(Color))
						{
							Func<Color> getter = () => (Color)GetMemberValue(owner, memberInfo);
							Action<Color> setter = v => SetMemberValue(owner, memberInfo, v);
							return Tween(getter, setter, e.toColor, e.duration, Color.LerpUnclamped, e.curve, null);
						}

						if (memberType == typeof(Quaternion))
						{
							Func<Quaternion> getter = () => (Quaternion)GetMemberValue(owner, memberInfo);
							Action<Quaternion> setter = v => SetMemberValue(owner, memberInfo, v);
							Func<Quaternion, Quaternion, float, Quaternion> slerp = (a, b, t) => Quaternion.SlerpUnclamped(a, b, t);
							Quaternion toQ = Quaternion.Euler(e.toVec3);
							Quaternion fromQ = Quaternion.Euler(e.fromVec3);
							if (!e.useCurrentAsFrom) return Tween(getter, setter, fromQ, toQ, e.duration, slerp, e.curve, null);
							return Tween(getter, setter, toQ, e.duration, slerp, e.curve, null);
						}

						if (memberType == typeof(bool))
						{
							StartCoroutine(DriveBoolWithCurve(owner, memberInfo, e.fromBool, e.toBool, e.duration, e.curve));
							return null;
						}
						if (memberType == typeof(void) && memberInfo is MethodInfo method)
						{
							// Use curve-driven trigger with hysteresis to avoid skim noise
							StartCoroutine(InvokeMethodOnCurve(method, owner, e.duration, e.curve));
							return null;
						}

						Debug.LogWarning($"Animate: Unsupported property type '{memberType.Name}' for CustomProperty on {comp.GetType().Name} (path '{e.propertyName}').");
						return null;
					}

					Debug.LogWarning($"Animate: Property/Field path '{e.propertyName}' not found on component {comp.GetType().Name}.");
					return null;
				}

			case TweenType.Float:
			default:
				// For generic float, user must wire getter/setter from code.
				return null;
		}
	}

	// ----------------
	// Convenience helpers
	// ----------------

	// Position
	public Coroutine TweenPosition(Transform tgt, Vector3 to, float duration, AnimationCurve curve = null, bool local = true, bool useExplicitFrom = false, Vector3 explicitFrom = default, Action onComplete = null)
	{
		Func<Vector3> getter = local ? (Func<Vector3>)(() => tgt.localPosition) : () => tgt.position;
		Action<Vector3> setter = local ? (Action<Vector3>)(v => tgt.localPosition = v) : v => tgt.position = v;
		if (useExplicitFrom)
			return Tween(getter, setter, explicitFrom, to, duration, Vector3.LerpUnclamped, curve, onComplete);
		return Tween(getter, setter, to, duration, Vector3.LerpUnclamped, curve, onComplete);
	}

	// Rotation (slerp)
	public Coroutine TweenRotation(Transform tgt, Quaternion to, float duration, AnimationCurve curve = null, bool local = true, bool useExplicitFrom = false, Quaternion explicitFrom = default, Action onComplete = null)
	{
		Func<Quaternion> getter = local ? (Func<Quaternion>)(() => tgt.localRotation) : () => tgt.rotation;
		Action<Quaternion> setter = local ? (Action<Quaternion>)(q => tgt.localRotation = q) : q => tgt.rotation = q;
		Func<Quaternion, Quaternion, float, Quaternion> slerp = (a, b, t) => Quaternion.SlerpUnclamped(a, b, t);
		if (useExplicitFrom)
			return Tween(getter, setter, explicitFrom, to, duration, slerp, curve, onComplete);
		return Tween(getter, setter, to, duration, slerp, curve, onComplete);
	}

	// Scale
	public Coroutine TweenScale(Transform tgt, Vector3 to, float duration, AnimationCurve curve = null, bool useExplicitFrom = false, Vector3 explicitFrom = default, Action onComplete = null)
	{
		Func<Vector3> getter = () => tgt.localScale;
		Action<Vector3> setter = v => tgt.localScale = v;
		if (useExplicitFrom)
			return Tween(getter, setter, explicitFrom, to, duration, Vector3.LerpUnclamped, curve, onComplete);
		return Tween(getter, setter, to, duration, Vector3.LerpUnclamped, curve, onComplete);
	}

	// Float (useful for CanvasGroup alpha, material floats, etc.)
	public Coroutine TweenFloat(Func<float> getter, Action<float> setter, float to, float duration, AnimationCurve curve = null, bool useExplicitFrom = false, float explicitFrom = 0f, Action onComplete = null)
	{
		Func<float, float, float, float> lerp = Mathf.LerpUnclamped;
		if (useExplicitFrom)
			return Tween(getter, setter, explicitFrom, to, duration, lerp, curve, onComplete);
		return Tween(getter, setter, to, duration, lerp, curve, onComplete);
	}

	// Color (Renderer material color)
	public Coroutine TweenColor(Renderer renderer, Color to, float duration, AnimationCurve curve = null, bool useExplicitFrom = false, Color explicitFrom = default, Action onComplete = null)
	{
		if (renderer == null) return null;
		Func<Color> getter = () => renderer.material.color;
		Action<Color> setter = c => renderer.material.color = c;
		if (useExplicitFrom)
			return Tween(getter, setter, explicitFrom, to, duration, Color.LerpUnclamped, curve, onComplete);
		return Tween(getter, setter, to, duration, Color.LerpUnclamped, curve, onComplete);
	}

	// CanvasGroup alpha helper
	public Coroutine TweenCanvasAlpha(CanvasGroup cg, float to, float duration, AnimationCurve curve = null, bool useExplicitFrom = false, float explicitFrom = 0f, Action onComplete = null)
	{
		if (cg == null) return null;
		return TweenFloat(() => cg.alpha, v => cg.alpha = v, to, duration, curve, useExplicitFrom, explicitFrom, onComplete);
	}

	// Renderer material color with property name and material index
	public Coroutine TweenMaterialColor(Renderer renderer, int materialIndex, string primaryProperty, string[] extraProperties, Color to, float duration, AnimationCurve curve = null, bool useExplicitFrom = false, Color explicitFrom = default, bool useCurrentAsFrom = false, Action onComplete = null)
	{
		if (renderer == null || renderer.materials == null || renderer.materials.Length == 0) return null;
		var mats = renderer.sharedMaterials;
		materialIndex = Mathf.Clamp(materialIndex, 0, mats.Length - 1);
		Material mat = mats[materialIndex];
		// build property list
		var props = new List<string>();
		if (!string.IsNullOrEmpty(primaryProperty)) props.Add(primaryProperty);
		if (extraProperties != null && extraProperties.Length > 0)
			props.AddRange(extraProperties.Where(p => !string.IsNullOrEmpty(p)));
		if (props.Count == 0) props.Add("_Color");
		string sampleProp = props[0];
		Func<Color> getter = () =>
		{
			if (mat != null && mat.HasProperty(sampleProp))
			{
				// Prefer the currently rendered value (PropertyBlock), fallback to material value.
				var mpb = new MaterialPropertyBlock();
				renderer.GetPropertyBlock(mpb, materialIndex);
				if (mpb.HasProperty(sampleProp))
					return mpb.GetColor(sampleProp);
				return mat.GetColor(sampleProp);
			}
			return Color.white;
		};
		Action<Color> setter = c =>
		{
			var mpb = new MaterialPropertyBlock();
			renderer.GetPropertyBlock(mpb, materialIndex);
			foreach (var p in props)
			{
				if (mat != null && mat.HasProperty(p))
					mpb.SetColor(p, c);
			}
			renderer.SetPropertyBlock(mpb, materialIndex);
		};

		if (useExplicitFrom && !useCurrentAsFrom)
			return Tween(getter, setter, explicitFrom, to, duration, Color.LerpUnclamped, curve, onComplete);

		// use current as from
		Color currentFrom = getter();
		return Tween(getter, setter, currentFrom, to, duration, Color.LerpUnclamped, curve, onComplete);
	}
}
