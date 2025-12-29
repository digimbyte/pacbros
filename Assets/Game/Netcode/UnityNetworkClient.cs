using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
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
        await JoinGame(code);
    }

    async Task InitServices()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    async Task JoinGame(string code)
    {
        JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(code);

        var transport = networkManager.GetComponent<UnityTransport>();
        transport.SetRelayServerData(new RelayServerData(joinAlloc, "dtls"));

        networkManager.StartClient();
    }
}
