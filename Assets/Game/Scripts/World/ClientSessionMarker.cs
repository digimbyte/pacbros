using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Global session/connection coordinator that lives across scenes.
///
/// - Created once (usually from the main menu scene) and marked DontDestroyOnLoad.
/// - Decides whether this process is acting as a local host, network host, or network client.
/// - Owns minimal connection / friend-code metadata needed *before* loading a gameplay scene.
///
/// LevelRuntime uses this to decide whether to behave as a host (spawn authoritative entities)
/// or as a client (skip local spawning, rely on netcode sync).
/// If no ClientSessionMarker.Instance exists at the time a level loads, LevelRuntime assumes
/// a local hosted game (single-player / no net).
/// </summary>
[DisallowMultipleComponent]
public class ClientSessionMarker : MonoBehaviour
{
    public static ClientSessionMarker Instance { get; private set; }

    public enum SessionMode
    {
        LocalHost,  // purely local game, no net connection
        NetHost,    // this process is hosting a networked game
        NetClient   // this process is joining a remote host
    }

    [Header("Session Mode")]
    [Tooltip("How this process is currently participating in the game session.")]
    public SessionMode mode = SessionMode.LocalHost;

    [Header("Scene Flow")]
    [Tooltip("Name of the gameplay scene to load once a session has been prepared.")]
    public string gameSceneName = "GameLoop";

    [Header("Connection Meta")] 
    [Tooltip("Friend code / join code or any other identifier used to connect to a host.")]
    public string friendCode;

    [Tooltip("Local player index for this process (0 = first player). LevelRuntime may use this for spawn index.")]
    public int localPlayerIndex = 0;

    public bool IsClient => mode == SessionMode.NetClient;
    public bool IsHostLike => mode == SessionMode.LocalHost || mode == SessionMode.NetHost;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Start a purely local (no networking) game.
    /// Scene load is performed immediately.
    /// </summary>
    public void StartLocalGame()
    {
        mode = SessionMode.LocalHost;
        SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>
    /// Prepare to host a networked game using the given friend/join code.
    /// Insert your actual Unity networking host/bootstrap here before the scene load.
    /// </summary>
    public void StartHostGame(string joinCode)
    {
        mode = SessionMode.NetHost;
        friendCode = joinCode;

        // TODO: spin up Unity networking host/server here, then load the gameplay scene
        SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>
    /// Prepare to join a remote host using the given friend/join code.
    /// Insert your actual Unity networking client-connect flow here before the scene load.
    /// </summary>
    public void JoinGame(string joinCode)
    {
        mode = SessionMode.NetClient;
        friendCode = joinCode;

        // TODO: kick off Unity networking client connect here, then load the gameplay scene
        SceneManager.LoadScene(gameSceneName);
    }
}
