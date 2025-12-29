using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;

public class UnityNetworkClient : MonoBehaviour
{
    public NetworkManager networkManager;

    void Awake()
    {
        if (networkManager == null)
            networkManager = FindFirstObjectByType<NetworkManager>();
    }

    public async Task StartJoining(string code)
    {
        await InitServices();

        var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);

        Debug.Log($"JOINED SESSION: {session.Id}");
    }

    async Task InitServices()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
}
