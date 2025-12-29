using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;

public class UnityNetworkHost : MonoBehaviour
{
    public NetworkManager networkManager;
    public int maxPlayers = 4;

    void Awake()
    {
        if (networkManager == null)
            networkManager = FindFirstObjectByType<NetworkManager>();
    }

    public async Task<string> StartHosting()
    {
        await InitServices();

        var options = new SessionOptions
        {
            MaxPlayers = maxPlayers,
            IsPrivate = false
        };

        var session = await MultiplayerService.Instance.CreateSessionAsync(options);

        Debug.Log($"SESSION CREATED: Join code {session.Code}");

        return session.Code;
    }

    async Task InitServices()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
}
