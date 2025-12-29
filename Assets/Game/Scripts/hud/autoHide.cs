using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class autoHide : MonoBehaviour
{
    public enum HideMode
    {
        Mesh,
        Object
    }

    [Tooltip("What to hide: individual meshes or the whole object (children).")]
    public HideMode mode = HideMode.Mesh;

    [Tooltip("If > 0, automatically restore after this many seconds. 0 = never restore.")]
    public float timeoutSeconds = 0f;

    // Internal tracking for restoration
    List<MeshRenderer> _meshRenderers;
    Dictionary<MeshFilter, Mesh> _meshFilterOriginals;
    Dictionary<GameObject, bool> _childActiveStates;

    Coroutine _restoreCoroutine;

    void Awake()
    {
        CacheState();
    }

    void OnEnable()
    {
        // Apply hide whenever this component (or its parent) is enabled.
        ApplyHide();
        if (timeoutSeconds > 0f)
        {
            if (_restoreCoroutine != null) StopCoroutine(_restoreCoroutine);
            _restoreCoroutine = StartCoroutine(RestoreAfterDelay(timeoutSeconds));
        }
    }

    void OnDisable()
    {
        // If the component is disabled (or parent disabled), ensure state is restored
        // so we don't leave children permanently hidden when re-enabled.
        if (_restoreCoroutine != null)
        {
            StopCoroutine(_restoreCoroutine);
            _restoreCoroutine = null;
        }
        RestoreState();
    }

    void CacheState()
    {
        _meshRenderers = new List<MeshRenderer>(GetComponentsInChildren<MeshRenderer>(includeInactive: true));
        var filters = GetComponentsInChildren<MeshFilter>(includeInactive: true);
        _meshFilterOriginals = new Dictionary<MeshFilter, Mesh>();
        foreach (var f in filters)
        {
            if (f != null)
                _meshFilterOriginals[f] = f.sharedMesh;
        }

        // Track children active states (exclude this.gameObject so script stays enabled)
        _childActiveStates = new Dictionary<GameObject, bool>();
        foreach (Transform t in transform)
        {
            if (t == null) continue;
            _childActiveStates[t.gameObject] = t.gameObject.activeSelf;
        }
    }

    void ApplyHide()
    {
        if (mode == HideMode.Mesh)
        {
            foreach (var r in _meshRenderers)
            {
                if (r == null) continue;
                r.enabled = false;
            }
            foreach (var kv in _meshFilterOriginals)
            {
                var f = kv.Key;
                if (f == null) continue;
                f.sharedMesh = null;
            }
        }
        else // HideMode.Object
        {
            // Disable direct children (preserve this GameObject so script runs on re-enable)
            foreach (var kv in _childActiveStates)
            {
                var go = kv.Key;
                if (go == null) continue;
                go.SetActive(false);
            }

            // Also disable renderers on this object itself
            foreach (var r in _meshRenderers)
            {
                if (r == null) continue;
                if (r.gameObject == this.gameObject)
                    r.enabled = false;
            }
        }
    }

    void RestoreState()
    {
        // Restore meshes/renderers
        if (_meshRenderers != null)
        {
            foreach (var r in _meshRenderers)
            {
                if (r == null) continue;
                r.enabled = true;
            }
        }

        if (_meshFilterOriginals != null)
        {
            foreach (var kv in _meshFilterOriginals)
            {
                var f = kv.Key;
                if (f == null) continue;
                f.sharedMesh = kv.Value;
            }
        }

        // Restore child active states
        if (_childActiveStates != null)
        {
            foreach (var kv in _childActiveStates)
            {
                var go = kv.Key;
                if (go == null) continue;
                go.SetActive(kv.Value);
            }
        }
    }

    IEnumerator RestoreAfterDelay(float secs)
    {
        yield return new WaitForSeconds(secs);
        RestoreState();
        _restoreCoroutine = null;
    }

    // Editor-friendly: expose a manual restore method
    public void RestoreNow()
    {
        if (_restoreCoroutine != null)
        {
            StopCoroutine(_restoreCoroutine);
            _restoreCoroutine = null;
        }
        RestoreState();
    }
}
