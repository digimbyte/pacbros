using System.Collections;
using UnityEngine;

/// <summary>
/// Plays background music that reacts to the global Heat system.
/// - Loops a random ambient track while calm.
/// - When the player is "flaming" (heat threshold), crossfades into a random hot track.
/// - When cooling down, crossfades back to the previous ambient (or another ambient) track.
/// </summary>
[DefaultExecutionOrder(-50)]
public class SoundManager : MonoBehaviour
{
    [Header("Tracks")]
    [Tooltip("Regular background tracks to loop when not flaming.")]
    public AudioClip[] ambientTracks;

    [Tooltip("Tracks to loop while flaming/hot.")]
    public AudioClip[] hotTracks;

    [Header("Heat Thresholds (hysteresis)")]
    [Tooltip("Enter hot music when heat is at or above this kelvin.")]
    public int hotOnKelvin = 7500;

    [Tooltip("Exit hot music when heat falls to or below this kelvin (should be lower than hotOnKelvin to avoid flapping).")]
    public int hotOffKelvin = 7100;

    [Header("Playback")]
    [Tooltip("Seconds to crossfade between tracks.")]
    [Min(0f)] public float crossfadeDuration = 1.5f;

    [Tooltip("Target volume for active track.")]
    [Range(0f, 1f)] public float targetVolume = 1f;

    [Tooltip("If true, keep playing even when scenes change.")]
    public bool dontDestroyOnLoad = true;

    private AudioSource _a;
    private AudioSource _b;
    private AudioSource _active;
    private AudioSource _idle;
    private Coroutine _crossfadeRoutine;
    private bool _usingHot;
    private AudioClip _lastAmbient;

    private void Awake()
    {
        if (dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);
        }

        _a = gameObject.AddComponent<AudioSource>();
        _b = gameObject.AddComponent<AudioSource>();
        ConfigureSource(_a);
        ConfigureSource(_b);
        _active = _a;
        _idle = _b;
    }

    private void OnEnable()
    {
        Heat.OnStageChanged += HandleStageChanged;
        Heat.OnHeatUnitsChanged += HandleHeatChanged;
    }

    private void Start()
    {
        // Kick off with ambient if nothing is playing.
        if (_active.clip == null)
        {
            PlayAmbient(randomize: true);
        }
    }

    private void OnDisable()
    {
        Heat.OnStageChanged -= HandleStageChanged;
        Heat.OnHeatUnitsChanged -= HandleHeatChanged;
    }

    private void HandleStageChanged(int stage)
    {
        EvaluateHeat(Heat.GetHeatUnits(), stage);
    }

    private void HandleHeatChanged(int kelvin)
    {
        EvaluateHeat(kelvin, Heat.Stage);
    }

    private void EvaluateHeat(int kelvin, int stage)
    {
        bool flaming = IsFlaming(kelvin, stage);
        if (flaming == _usingHot)
        {
            return;
        }

        _usingHot = flaming;

        if (flaming)
        {
            PlayHot();
        }
        else
        {
            PlayAmbient(randomize: _lastAmbient == null); // prefer returning to previous ambient when available
        }
    }

    // Use Heat's notion of max stage/overcharge as a hard guarantee of flaming.
    private const int FlamingStage = 8;   // Heat.Stage runs 0..8 (StageCount-1)

    private void OnValidate()
    {
        hotOnKelvin = Mathf.Max(0, hotOnKelvin);
        hotOffKelvin = Mathf.Clamp(hotOffKelvin, 0, Mathf.Max(0, hotOnKelvin - 1));
    }

    private bool IsFlaming(int kelvin, int stage)
    {
        // Stage 8 is always flaming regardless of kelvin thresholds.
        if (stage >= FlamingStage)
        {
            return true;
        }

        // Hysteresis based on current state.
        if (_usingHot)
        {
            return kelvin > hotOffKelvin;
        }

        return kelvin >= hotOnKelvin;
    }

    private void PlayHot()
    {
        AudioClip clip = PickRandom(hotTracks, _active.clip);
        if (clip != null)
        {
            CrossfadeTo(clip);
        }
    }

    private void PlayAmbient(bool randomize)
    {
        AudioClip fallback = _lastAmbient;
        AudioClip target;
        if (!randomize && fallback != null)
        {
            target = fallback;
        }
        else
        {
            target = PickRandom(ambientTracks, _active.clip);
            if (target == null)
            {
                return;
            }
            _lastAmbient = target;
        }

        CrossfadeTo(target);
    }

    private void ConfigureSource(AudioSource src)
    {
        src.playOnAwake = false;
        src.loop = true;
        src.volume = 0f;
    }

    private AudioClip PickRandom(AudioClip[] list, AudioClip avoidClip)
    {
        if (list == null || list.Length == 0)
        {
            return null;
        }

        if (list.Length == 1)
        {
            return list[0];
        }

        int start = Random.Range(0, list.Length);
        for (int i = 0; i < list.Length; i++)
        {
            int idx = (start + i) % list.Length;
            AudioClip candidate = list[idx];
            if (candidate != avoidClip && candidate != null)
            {
                return candidate;
            }
        }

        // Fallback to original pick even if same as avoidClip.
        return list[start];
    }

    private void CrossfadeTo(AudioClip nextClip)
    {
        if (nextClip == null)
        {
            return;
        }

        if (_crossfadeRoutine != null)
        {
            StopCoroutine(_crossfadeRoutine);
        }

        _crossfadeRoutine = StartCoroutine(CrossfadeRoutine(nextClip));
    }

    private IEnumerator CrossfadeRoutine(AudioClip nextClip)
    {
        // Prepare idle source.
        _idle.clip = nextClip;
        _idle.volume = 0f;
        _idle.loop = true;
        _idle.Play();

        float duration = Mathf.Max(0f, crossfadeDuration);
        if (duration == 0f)
        {
            _active.Stop();
            _idle.volume = targetVolume;
            SwapSources();
            yield break;
        }

        float startVolActive = _active.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _active.volume = Mathf.Lerp(startVolActive, 0f, t);
            _idle.volume = Mathf.Lerp(0f, targetVolume, t);
            yield return null;
        }

        _active.Stop();
        _idle.volume = targetVolume;
        SwapSources();
    }

    private void SwapSources()
    {
        var temp = _active;
        _active = _idle;
        _idle = temp;
    }
}
