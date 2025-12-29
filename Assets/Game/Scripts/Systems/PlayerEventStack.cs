using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerEventType
{
    None,
    Death,
    Respawn,
    Attack,
    OutOfLives,
    DoorBlocked
}

public readonly struct PlayerEvent
{
    public readonly PlayerEventType type;
    public readonly PlayerEntity player;
    public readonly Vector3 worldPosition;
    public readonly string context;

    public PlayerEvent(PlayerEventType type, PlayerEntity player, Vector3 worldPosition, string context = null)
    {
        this.type = type;
        this.player = player;
        this.worldPosition = worldPosition;
        this.context = context;
    }
}

/// <summary>
/// Lightweight stack/bus so gameplay systems can react to player-centric events without tight coupling.
/// Consumers can subscribe to <see cref=\"OnEventPushed\"/> or poll the stack for debugging.
/// </summary>
public static class PlayerEventStack
{
    static readonly Stack<PlayerEvent> _events = new Stack<PlayerEvent>(32);

    public static event Action<PlayerEvent> OnEventPushed;

    public static int Count => _events.Count;

    public static void Push(PlayerEvent evt)
    {
        _events.Push(evt);
        OnEventPushed?.Invoke(evt);
    }

    public static bool TryPeek(out PlayerEvent evt)
    {
        if (_events.Count > 0)
        {
            evt = _events.Peek();
            return true;
        }

        evt = default;
        return false;
    }

    public static PlayerEvent[] ToArray()
    {
        return _events.ToArray();
    }

    public static void Clear()
    {
        _events.Clear();
    }
}
