using System;
using System.Threading.Tasks;
using UnityEngine;

using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public class RelayBootstrap : MonoBehaviour
{
    public NetworkManager networkManager;
    public int maxPlayers = 4;

    private bool servicesReady;

    async void Awake()
    {
        if (networkManager == null)
            networkManager = FindFirstObjectByType<NetworkManager>();

        await InitServices();
    }

    async Task InitServices()
    {
        if (servicesReady) return;

        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        servicesReady = true;
    }

    // =======================
    // HOST
    // =======================
    public async void Host()
    {
        Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

        Debug.Log($"JOIN CODE: {joinCode}");

        var transport = networkManager.GetComponent<UnityTransport>();
        transport.SetRelayServerData(new RelayServerData(alloc, "dtls"));

        networkManager.StartHost();
    }

    // =======================
    // CLIENT
    // =======================
    public async void Join(string joinCode)
    {
        JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);

        var transport = networkManager.GetComponent<UnityTransport>();
        transport.SetRelayServerData(new RelayServerData(joinAlloc, "dtls"));

        networkManager.StartClient();
    }
}